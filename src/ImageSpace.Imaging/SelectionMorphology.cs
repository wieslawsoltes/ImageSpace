using System.Buffers;
using ImageSpace.Core;

namespace ImageSpace.Imaging;

public enum SelectionModification
{
    Expand, Contract, Border, Smooth
}

/// <summary>
/// Grayscale coverage morphology using separable monotonic deques: O(width × height),
/// independent of radius. The structuring element is a square of side 2r+1;
/// samples outside the canvas are transparent. Source coverage is never modified.
/// </summary>
public static class SelectionMorphology
{
    public const int MaximumRadius = 256;

    public static PixelSurface Apply(PixelSurface selection, SelectionModification modification, int radius,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!Enum.IsDefined(modification))
            throw new ArgumentOutOfRangeException(nameof(modification));
        if (radius is < 0 or > MaximumRadius)
            throw new ArgumentOutOfRangeException(nameof(radius));
        cancellationToken.ThrowIfCancellationRequested();
        if (radius == 0)
            return modification == SelectionModification.Border
            ? new PixelSurface(selection.Width, selection.Height) : selection.Snapshot();
        var count = checked(selection.Width * selection.Height);
        var a = ArrayPool<byte>.Shared.Rent(count);
        var b = ArrayPool<byte>.Shared.Rent(count);
        var scratch = ArrayPool<byte>.Shared.Rent(count);
        var deque = ArrayPool<int>.Shared.Rent(Math.Max(selection.Width, selection.Height) + radius * 2);
        try
        {
            selection.CopyAlphaTo(a.AsSpan(0, count));
            switch (modification)
            {
                case SelectionModification.Expand:
                    Morph(a, b, true);
                    return PixelSurface.FromAlpha(selection.Width, selection.Height, b.AsSpan(0, count));
                case SelectionModification.Contract:
                    Morph(a, b, false);
                    return PixelSurface.FromAlpha(selection.Width, selection.Height, b.AsSpan(0, count));
                case SelectionModification.Border:
                    Morph(a, b, true);
                    // Reuse the input only after the complete horizontal pass has read it.
                    Morph(a, a, false);
                    for (var i = 0; i < count; i++)
                        b[i] = (byte)Math.Max(0, b[i] - a[i]);
                    return PixelSurface.FromAlpha(selection.Width, selection.Height, b.AsSpan(0, count));
                default:
                    // Opening removes protrusions; closing fills narrow notches and holes.
                    Morph(a, b, false);
                    Morph(b, a, true);
                    Morph(a, b, true);
                    Morph(b, a, false);
                    return PixelSurface.FromAlpha(selection.Width, selection.Height, a.AsSpan(0, count));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(a, clearArray: true);
            ArrayPool<byte>.Shared.Return(b, clearArray: true);
            ArrayPool<byte>.Shared.Return(scratch, clearArray: true);
            ArrayPool<int>.Shared.Return(deque);
        }

        void Morph(byte[] input, byte[] output, bool maximum)
        {
            for (var y = 0; y < selection.Height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ExtremaLine(input, scratch, y * selection.Width, 1, selection.Width, radius, maximum, deque);
            }
            for (var x = 0; x < selection.Width; x++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ExtremaLine(scratch, output, x, selection.Width, selection.Height, radius, maximum, deque);
            }
        }
    }

    private static void ExtremaLine(ReadOnlySpan<byte> source, Span<byte> destination,
        int offset, int stride, int length, int radius, bool maximum, Span<int> deque)
    {
        var head = 0;
        var tail = 0;
        for (var position = -radius; position < length + radius; position++)
        {
            while (head < tail && deque[head] < position - radius * 2)
                head++;
            var value = (uint)position < (uint)length ? source[offset + position * stride] : (byte)0;
            while (head < tail)
            {
                var index = deque[tail - 1];
                var previous = (uint)index < (uint)length ? source[offset + index * stride] : (byte)0;
                if (maximum ? previous > value : previous < value)
                    break;
                tail--;
            }
            deque[tail++] = position;
            var center = position - radius;
            if ((uint)center >= (uint)length)
                continue;
            var first = deque[head];
            destination[offset + center * stride] = (uint)first < (uint)length ? source[offset + first * stride] : (byte)0;
        }
    }
}
