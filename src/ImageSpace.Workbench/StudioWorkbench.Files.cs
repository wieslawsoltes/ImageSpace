using ImageSpace.Documents;
using ImageSpace.Skia;
using SkiaSharp;
namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    public async Task OpenAsync(bool place)
    {
        if (_busy)
            return;
        try
        {
            var file = await _storage.OpenAsync();
            if (file is null)
                return;
            if (file.Bytes.Length > DocumentArchive.MaximumArchiveBytes)
                throw new InvalidDataException("File exceeds the 128 MiB import limit.");
            var extension = Path.GetExtension(file.Name).ToLowerInvariant();
            ImageDocument document;
            string? warning = null;
            if (extension == ".imagespace")
                document = DocumentArchive.Load(file.Bytes);
            else if (extension == ".psd")
            {
                var result = PsdCodec.Load(file.Bytes, Path.GetFileNameWithoutExtension(file.Name));
                document = result.Document;
                warning = string.Join(" ", result.Warnings);
            }
            else
            {
                var pixels = ImageRenderer.Decode(file.Bytes);
                document = new(pixels.Width, pixels.Height, Path.GetFileNameWithoutExtension(file.Name));
                var layer = Layer.Raster("Background", pixels.Width, pixels.Height);
                layer.Pixels = pixels;
                document.Layers.Add(layer);
                document.ActiveLayerId = layer.Id;
            }
            if (place)
            {
                var pixels = Surface.Renderer.Rasterize(document);
                Session.Execute("Place image", d => { var layer = Layer.Raster(Path.GetFileNameWithoutExtension(file.Name), pixels.Width, pixels.Height); layer.Pixels = pixels; var scale = Math.Min(1, Math.Min((float)d.Width / pixels.Width, (float)d.Height / pixels.Height)); layer.ScaleX = layer.ScaleY = scale; layer.X = (d.Width - pixels.Width * scale) / 2; layer.Y = (d.Height - pixels.Height * scale) / 2; d.Layers.Add(layer); d.ActiveLayerId = layer.Id; d.EditMask = false; });
                SelectTool(EditorTool.Move);
            }
            else
                AddDocument(document);
            ShowStatus(warning ?? "Opened " + file.Name);
            if (warning is not null)
                await ShowTextAsync("PSD import compatibility", warning);
        }
        catch (Exception ex) { ShowStatus("Could not open the file: " + ex.Message); }
    }
    public async Task SaveAsync()
    {
        if (_busy)
            return;
        var session = Session;
        try
        {
            Surface.CancelGesture();
            var data = DocumentArchive.Save(session.Document);
            if (await _storage.SaveAsync(SafeName(session.Document.Name) + ".imagespace", data, "application/x-imagespace"))
            {
                session.MarkSaved();
                ShowStatus("Saved editable ImageSpace document.");
            }
        }
        catch (Exception ex) { ShowStatus("Save failed: " + ex.Message); }
    }
    private async Task ExportAsync(string extension)
    {
        if (_busy)
            return;
        try
        {
            var document = Session.Document;
            byte[] bytes;
            string contentType;
            if (extension == "psd")
            {
                if (!await ConfirmAsync("Export Photoshop PSD", "This exports RGB/8 raster layers. Type, shapes, transforms and masks are rasterized. Documents with live adjustments are flattened to preserve the visible image. Save an .imagespace copy to retain editability.", "Export PSD"))
                    return;
                var composite = Surface.Renderer.Rasterize(document);
                var export = document;
                if (document.Layers.Any(l => l.Kind == LayerKind.Adjustment && l.Visible))
                {
                    export = new(document.Width, document.Height, document.Name);
                    var l = Layer.Raster("Composite", document.Width, document.Height);
                    l.Pixels = composite;
                    export.Layers.Add(l);
                    export.ActiveLayerId = l.Id;
                }
                bytes = PsdCodec.Save(export, l => Surface.Renderer.RasterizeLayer(export, l), composite);
                contentType = "image/vnd.adobe.photoshop";
            }
            else
            {
                var format = extension switch
                {
                    "jpg" => SKEncodedImageFormat.Jpeg,
                    "webp" => SKEncodedImageFormat.Webp,
                    _ => SKEncodedImageFormat.Png
                };
                bytes = Surface.Renderer.Export(document, format, 95);
                contentType = extension == "jpg" ? "image/jpeg" : "image/" + extension;
            }
            if (await _storage.SaveAsync(SafeName(document.Name) + "." + extension, bytes, contentType))
                ShowStatus("Exported " + extension.ToUpperInvariant() + ". The editable document remains unchanged.");
        }
        catch (Exception ex) { ShowStatus("Export failed: " + ex.Message); }
    }
    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var value = new string(name.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(value) ? "Untitled" : value;
    }
    private async Task RecoverAsync()
    {
        if (_savingRecovery || _busy || Session.IsInTransaction || _disposed)
            return;
        var session = Session;
        var revision = session.Revision;
        if (_lastRecovered == revision && _lastRecoveredDocument == session.Document.Id)
            return;
        _savingRecovery = true;
        try
        {
            var snapshot = session.Document.Snapshot();
            var bytes = DocumentArchive.Save(snapshot);
            await _storage.WriteRecoveryAsync(bytes);
            _lastRecovered = revision;
            _lastRecoveredDocument = snapshot.Id;
        }
        catch (Exception ex) { ShowStatus("Recovery copy could not be saved: " + ex.Message + " Save an editable file now."); }
        finally { _savingRecovery = false; }
    }
    private async Task ClearRecoveryAsync()
    {
        if (!await ConfirmAsync("Clear recovery copy?", "This removes the local recovery file. Open documents and exported files are not deleted.", "Clear recovery"))
            return;
        try
        {
            await _storage.ClearRecoveryAsync();
            _lastRecovered = Session.Revision;
            _lastRecoveredDocument = Session.Document.Id;
            ShowStatus("Local recovery copy cleared.");
        }
        catch (Exception ex) { ShowStatus(ex.Message); }
    }
}
