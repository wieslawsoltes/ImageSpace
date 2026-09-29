using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using ImageSpace.Core;
using ImageSpace.Filters;
using ImageSpace.WebGpu;

namespace ImageSpace.App;

internal sealed class BrowserFilterSession : IFilterSession
{
    private readonly CpuFilterSession _fallback;
    private string? _handle;
    private readonly int _width, _height;
    private bool _disposed;
    public string Backend { get; private set; } = "WebGPU resident stack";

    private BrowserFilterSession(PixelSurface source, string handle)
    {
        _fallback = new CpuFilterSession(source);
        _width = source.Width; _height = source.Height; _handle = handle;
    }
    public static async Task<IFilterSession?> CreateAsync(PixelSurface source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handle = await BrowserFiles.CreateFilterSession(Convert.ToBase64String(source.ToRgba()), source.Width, source.Height);
        if (string.IsNullOrEmpty(handle)) return null;
        if (cancellationToken.IsCancellationRequested)
        {
            BrowserFiles.ReleaseFilterSession(handle);
            cancellationToken.ThrowIfCancellationRequested();
        }
        return new BrowserFilterSession(source, handle);
    }
    public async Task<PixelSurface> ApplyAsync(IReadOnlyList<FilterOperation> operations, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var captured = FilterRecipe.Capture(operations);
        if (_handle is not null && captured.Where(op => op.Enabled).All(op => GpuKernels.Supports(op.Kind)))
        {
            try
            {
                var json = JsonSerializer.Serialize(captured.Select(op => new
                {
                    kind = op.Kind.ToString(), amount = op.Amount, secondary = op.Secondary, enabled = op.Enabled
                }).ToArray());
                var result = await BrowserFiles.EvaluateFilterSession(_handle, json);
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_disposed, this);
                Backend = "WebGPU resident stack";
                return PixelSurface.FromRgba(_width, _height, Convert.FromBase64String(result));
            }
            catch (OperationCanceledException) { throw; }
            catch (ObjectDisposedException) { throw; }
            catch (Exception error)
            {
                Console.WriteLine("Resident GPU fallback: " + error.Message);
                BrowserFiles.ReleaseFilterSession(_handle); _handle = null;
            }
        }
        Backend = "CPU fallback (seeded Noise or unavailable GPU)";
        return await _fallback.ApplyAsync(captured, cancellationToken);
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle is not null) { BrowserFiles.ReleaseFilterSession(_handle); _handle = null; }
        await _fallback.DisposeAsync();
    }
}

internal static partial class BrowserFiles
{
    [JSImport("globalThis.imageSpaceGpu.createHandle")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> CreateFilterSession(string base64, int width, int height);
    [JSImport("globalThis.imageSpaceGpu.evaluateHandle")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> EvaluateFilterSession(string handle, string operations);
    [JSImport("globalThis.imageSpaceGpu.releaseHandle")]
    internal static partial void ReleaseFilterSession(string handle);
}
