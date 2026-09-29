using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using ImageSpace.Storage;
using ImageSpace.Core;
using ImageSpace.Filters;
using ImageSpace.WebGpu;
using ImageSpace.Workbench;
using Microsoft.UI.Xaml;
namespace ImageSpace.App;

internal sealed class BrowserEditorStorage : IEditorStorage
{
    public async Task<OpenedFile?> OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await BrowserFiles.Open();
        if (string.IsNullOrEmpty(value))
            return null;
        using var json = JsonDocument.Parse(value);
        return new(json.RootElement.GetProperty("name").GetString()!, Convert.FromBase64String(json.RootElement.GetProperty("data").GetString()!));
    }
    public async Task<bool> SaveAsync(string name, byte[] bytes, string contentType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await BrowserFiles.Download(name, Convert.ToBase64String(bytes), contentType) == "ok";
    }
    public async Task<byte[]?> ReadRecoveryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = await BrowserFiles.Load();
        return string.IsNullOrEmpty(value) ? null : Convert.FromBase64String(value);
    }
    public async Task WriteRecoveryAsync(byte[] bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await BrowserFiles.Save(Convert.ToBase64String(bytes));
    }
    public async Task ClearRecoveryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await BrowserFiles.Clear();
    }
}
internal sealed class BrowserGpuBackend
{
    public async Task<PixelSurface?> ApplyAsync(PixelSurface source, FilterKind kind, float amount, float secondary)
    {
        if (!GpuKernels.Supports(kind))
            return null;
        var result = await BrowserFiles.Filter(Convert.ToBase64String(source.ToRgba()), source.Width, source.Height, kind.ToString(), amount, secondary);
        return string.IsNullOrEmpty(result) ? null : FilterPixels.FromRgba(source.Width, source.Height, Convert.FromBase64String(result));
    }
}
internal static partial class BrowserFiles
{
    [JSImport("globalThis.imageSpaceHost.open")][return: JSMarshalAs<JSType.Promise<JSType.String>>] internal static partial Task<string> Open();
    [JSImport("globalThis.imageSpaceHost.download")][return: JSMarshalAs<JSType.Promise<JSType.String>>] internal static partial Task<string> Download(string name, string base64, string contentType);
    [JSImport("globalThis.imageSpaceHost.load")][return: JSMarshalAs<JSType.Promise<JSType.String>>] internal static partial Task<string> Load();
    [JSImport("globalThis.imageSpaceHost.save")][return: JSMarshalAs<JSType.Promise<JSType.String>>] internal static partial Task<string> Save(string base64);
    [JSImport("globalThis.imageSpaceHost.clear")][return: JSMarshalAs<JSType.Promise<JSType.String>>] internal static partial Task<string> Clear();
    [JSImport("globalThis.imageSpaceHost.initializeGpu")][return: JSMarshalAs<JSType.Promise<JSType.String>>] internal static partial Task<string> InitializeGpu();
    [JSImport("globalThis.imageSpaceHost.filter")][return: JSMarshalAs<JSType.Promise<JSType.String>>] internal static partial Task<string> Filter(string base64, int width, int height, string kind, double amount, double secondary);
    [JSImport("globalThis.imageSpaceHost.isTestMode")] internal static partial bool IsTestMode();
    [JSImport("globalThis.imageSpaceHost.publishState")] internal static partial void PublishState(string state, string controls);
    [JSImport("globalThis.imageSpaceHost.setDirty")] internal static partial void SetDirty(bool dirty);
}
internal static class BrowserDiagnostics
{
    public static void Attach(StudioWorkbench workbench)
    {
        workbench.FilterSessionFactory = BrowserFilterSession.CreateAsync;
        workbench.StateChanged += () => BrowserFiles.SetDirty(workbench.Documents.Any(s => s.IsDirty));
        if (!BrowserFiles.IsTestMode())
            return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) => { try { BrowserFiles.PublishState(workbench.CaptureDiagnostics(), workbench.CaptureControls()); } catch (Exception ex) { Console.WriteLine("Diagnostics: " + ex.Message); } };
        workbench.Unloaded += (_, _) => timer.Stop();
        timer.Start();
    }
}
