using ImageSpace.Core;

namespace ImageSpace.Filters;

/// <summary>
/// Owns an immutable source snapshot. Every evaluation begins from that source,
/// not from the preceding preview. Returned pixels have independent ownership.
/// </summary>
public interface IFilterSession : IAsyncDisposable
{
    string Backend
    {
        get;
    }
    Task<PixelSurface> ApplyAsync(IReadOnlyList<FilterOperation> operations, CancellationToken cancellationToken = default);
}

public sealed class CpuFilterSession : IFilterSession
{
    private readonly PixelSurface _source;
    private bool _disposed;
    public string Backend => "CPU RGBA8 kernels";

    public CpuFilterSession(PixelSurface source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source.Snapshot();
    }

    public Task<PixelSurface> ApplyAsync(IReadOnlyList<FilterOperation> operations, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var captured = FilterRecipe.Capture(operations);
        cancellationToken.ThrowIfCancellationRequested();
        return OperatingSystem.IsBrowser() ? EvaluateAsync() : Task.Run(EvaluateAsync, cancellationToken);

        async Task<PixelSurface> EvaluateAsync()
        {
            var output = _source.Snapshot();
            foreach (var operation in captured)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!operation.Enabled)
                    continue;
                // Browser CPU fallback yields between stages. It does not pretend to
                // preempt an already-running scalar kernel on the single UI thread.
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                output = FilterEngine.Apply(output, operation.Kind, operation.Amount, operation.Secondary);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return output;
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
