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
    private volatile bool _disposed;
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
        // Take ownership on the caller's thread, before scheduling native worker work.
        var input = _source.Snapshot();
        return OperatingSystem.IsBrowser() ? EvaluateAsync() : Task.Run(EvaluateAsync, cancellationToken);

        async Task<PixelSurface> EvaluateAsync()
        {
            var output = input;
            foreach (var operation in captured)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!operation.Enabled)
                    continue;
                if (operation.Kind == FilterKind.GaussianBlur)
                {
                    output = OperatingSystem.IsBrowser()
                        ? await GaussianBlurProcessor.ApplyAsync(output, operation.Amount, cancellationToken)
                        : GaussianBlurProcessor.Apply(output, operation.Amount, cancellationToken);
                }
                else
                {
                    // Other scalar kernels still cancel between stages, not within a stage.
                    await Task.Yield();
                    cancellationToken.ThrowIfCancellationRequested();
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    output = FilterEngine.Apply(output, operation.Kind, operation.Amount, operation.Secondary);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            return output;
        }
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
