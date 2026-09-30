using System.Buffers;
using System.Diagnostics;
using ImageSpace.Core;

namespace ImageSpace.Filters;

/// <summary>
/// Separable premultiplied Gaussian filtering with a rolling horizontal-row cache.
/// Tap order and float-horizontal/double-vertical precision match the original CPU kernel.
/// </summary>
public static class GaussianBlurProcessor
{
    public const float MaximumSigma = 32;
    private const double CooperativeSliceMilliseconds = 8;

    public static GaussianBlurWorkspace GetWorkspace(int width, int height, float sigma)
    {
        PixelSurface.ValidateSize(width, height);
        if (!float.IsFinite(sigma))
            throw new ArgumentOutOfRangeException(nameof(sigma));
        if (sigma <= 0)
            return default;
        var radius = (int)Math.Ceiling(Math.Clamp(sigma, .1f, MaximumSigma) * 3);
        var rows = Math.Min(height, radius * 2 + 1);
        return new GaussianBlurWorkspace(radius, rows, (long)width * rows * 4 * sizeof(float),
            (long)width * 8 + radius * 8, (long)(radius * 2 + 1) * (sizeof(double) + sizeof(float) + sizeof(int)));
    }

    /// <summary>Run synchronously. The caller must not mutate source concurrently.</summary>
    public static PixelSurface Apply(PixelSurface source, float sigma, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var workspace = GetWorkspace(source.Width, source.Height, sigma);
        cancellationToken.ThrowIfCancellationRequested();
        if (workspace.Radius == 0)
            return source.Snapshot();
        using var operation = new Operation(source, sigma, workspace);
        while (operation.Step(cancellationToken)) { }
        cancellationToken.ThrowIfCancellationRequested();
        return operation.Result;
    }

    /// <summary>
    /// Capture source before suspension and cooperatively process rows on the calling context.
    /// Timer yields occur after an elapsed-time budget, checked between rows; this is not a
    /// hard latency bound or automatic worker-thread offload. Cancellation is also checked
    /// every 64 output pixels within each horizontal/vertical row.
    /// </summary>
    public static Task<PixelSurface> ApplyAsync(PixelSurface source, float sigma, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var workspace = GetWorkspace(source.Width, source.Height, sigma);
        cancellationToken.ThrowIfCancellationRequested();
        var captured = source.Snapshot();
        if (workspace.Radius == 0)
            return Task.FromResult(captured);
        return EvaluateAsync();

        async Task<PixelSurface> EvaluateAsync()
        {
            // A timer gives the browser event loop an opportunity to service input.
            // Task.Yield alone does not guarantee input/render scheduling priority.
            await Task.Delay(1, cancellationToken);
            using var operation = new Operation(captured, sigma, workspace);
            var slice = Stopwatch.GetTimestamp();
            while (operation.Step(cancellationToken))
            {
                if (Stopwatch.GetElapsedTime(slice).TotalMilliseconds < CooperativeSliceMilliseconds)
                    continue;
                await Task.Delay(1, cancellationToken);
                slice = Stopwatch.GetTimestamp();
            }
            cancellationToken.ThrowIfCancellationRequested();
            return operation.Result;
        }
    }

    private sealed class Operation : IDisposable
    {
        private readonly PixelSurface _source;
        private readonly int _width, _height, _radius, _cachedRows, _rowLength, _paddedLength;
        private readonly double[] _weights;
        private readonly float[] _horizontalWeights;
        private readonly int[] _rowOffsets;
        private float[]? _ring;
        private byte[]? _rows;
        private int _nextSourceRow, _outputRow;
        public PixelSurface Result { get; }

        public Operation(PixelSurface source, float sigma, GaussianBlurWorkspace workspace)
        {
            _source = source;
            _width = source.Width;
            _height = source.Height;
            _radius = workspace.Radius;
            _cachedRows = workspace.CachedRows;
            _rowLength = checked(_width * 4);
            _paddedLength = checked((_width + _radius * 2) * 4);
            _weights = new double[_radius * 2 + 1];
            _horizontalWeights = new float[_weights.Length];
            _rowOffsets = new int[_weights.Length];
            sigma = Math.Clamp(sigma, .1f, MaximumSigma);
            double total = 0;
            for (var i = -_radius; i <= _radius; i++)
            {
                // Preserve the original float expression before Math.Exp(double).
                var weight = Math.Exp(-i * i / (2 * sigma * sigma));
                _weights[i + _radius] = weight;
                total += weight;
            }
            for (var i = 0; i < _weights.Length; i++)
            {
                _weights[i] /= total;
                _horizontalWeights[i] = (float)_weights[i];
            }
            Result = new PixelSurface(_width, _height);
            try
            {
                _ring = ArrayPool<float>.Shared.Rent(checked(_rowLength * _cachedRows));
                _rows = ArrayPool<byte>.Shared.Rent(checked(_paddedLength + _rowLength));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public bool Step(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_ring is null, this);
            if (_outputRow == _height)
                return false;
            var needed = Math.Min(_height - 1, _outputRow + _radius);
            if (_nextSourceRow <= needed)
                Horizontal(_nextSourceRow++, cancellationToken);
            else
                Vertical(_outputRow++, cancellationToken);
            return true;
        }

        private void Horizontal(int y, CancellationToken cancellationToken)
        {
            var input = _rows!.AsSpan(0, _paddedLength);
            _source.CopyRowTo(0, y, input.Slice(_radius * 4, _rowLength));
            // Populate clamped halo once instead of doing clamp/dictionary lookups per tap.
            var first = input.Slice(_radius * 4, 4);
            var last = input.Slice((_radius + _width - 1) * 4, 4);
            for (var i = 0; i < _radius; i++)
            {
                first.CopyTo(input.Slice(i * 4, 4));
                last.CopyTo(input.Slice((_radius + _width + i) * 4, 4));
            }
            var destination = _ring!.AsSpan((y % _cachedRows) * _rowLength, _rowLength);
            for (var x = 0; x < _width; x++)
            {
                if ((x & 63) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                float red = 0, green = 0, blue = 0, alpha = 0;
                var at = x * 4;
                for (var tap = 0; tap < _horizontalWeights.Length; tap++)
                {
                    var index = at + tap * 4;
                    var coverage = input[index + 3] / 255f;
                    var weight = _horizontalWeights[tap];
                    red += input[index] * coverage * weight;
                    green += input[index + 1] * coverage * weight;
                    blue += input[index + 2] * coverage * weight;
                    alpha += coverage * weight;
                }
                // All four channels are assigned: never read dirty pooled float contents.
                destination[at] = red;
                destination[at + 1] = green;
                destination[at + 2] = blue;
                destination[at + 3] = alpha;
            }
        }

        private void Vertical(int y, CancellationToken cancellationToken)
        {
            var output = _rows!.AsSpan(_paddedLength, _rowLength);
            output.Clear();
            for (var tap = 0; tap < _rowOffsets.Length; tap++)
                _rowOffsets[tap] = (Math.Clamp(y + tap - _radius, 0, _height - 1) % _cachedRows) * _rowLength;
            var ring = _ring!;
            for (var x = 0; x < _width; x++)
            {
                if ((x & 63) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                double red = 0, green = 0, blue = 0, alpha = 0;
                var at = x * 4;
                for (var tap = 0; tap < _weights.Length; tap++)
                {
                    var index = _rowOffsets[tap] + at;
                    var weight = _weights[tap];
                    red += ring[index] * weight;
                    green += ring[index + 1] * weight;
                    blue += ring[index + 2] * weight;
                    alpha += ring[index + 3] * weight;
                }
                if (alpha <= 0)
                    continue;
                output[at] = Rgba32.Byte(red / alpha);
                output[at + 1] = Rgba32.Byte(green / alpha);
                output[at + 2] = Rgba32.Byte(blue / alpha);
                output[at + 3] = Rgba32.Byte(alpha * 255);
            }
            // Keep computed hidden RGB when alpha rounds to zero, matching spatial stages.
            Result.WriteRow(0, y, output);
        }

        public void Dispose()
        {
            if (_ring is { } ring)
            {
                _ring = null;
                ArrayPool<float>.Shared.Return(ring, clearArray: true);
            }
            if (_rows is { } rows)
            {
                _rows = null;
                ArrayPool<byte>.Shared.Return(rows, clearArray: true);
            }
        }
    }
}
