namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private void Deselect() => Run(() => Session.Execute("Deselect", document => document.Selection = null));
    private void SelectAll() => Run(() => Session.Execute("Select all", document =>
    {
        document.Selection = new PixelSurface(document.Width, document.Height);
        document.Selection.Fill(Rgba32.White);
    }));
    private void InvertSelection() => Run(() => Session.Execute("Inverse selection", document =>
        document.Selection = Selections.Invert(document.Selection, document.Width, document.Height)));
    private void SelectLayerAlpha() => Run(() =>
    {
        if (Session.Document.ActiveLayer is not { } layer) return;
        if (Session.Document.EditMask) { Session.LoadMaskSelection(); return; }
        var pixels = Surface.Renderer.RasterizeLayer(Session.Document, layer);
        Session.Execute("Select layer alpha", document =>
        {
            var mask = new PixelSurface(document.Width, document.Height);
            for (var y = 0; y < document.Height; y++)
                for (var x = 0; x < document.Width; x++) mask.Set(x, y, new Rgba32(255, 255, 255, pixels.Get(x, y).A));
            document.Selection = mask;
        });
    });
    private void Fill(Rgba32 color) => Run(() => Session.FillPixels(color));

    private void Copy(bool cut) => Run(() =>
    {
        var document = Session.Document;
        if (cut && (document.EditMask || document.ActiveLayer is not { Locked: false, Pixels: not null }))
            throw new InvalidOperationException("Cut requires an unlocked pixel layer, not a mask.");
        var bounds = document.Selection is null ? (0, 0, document.Width, document.Height) : Selections.Bounds(document.Selection);
        if (bounds is null) { ShowStatus("The selection is empty. Nothing was copied."); return; }
        var pixels = cut ? Surface.Renderer.RasterizeLayer(document, document.ActiveLayer!) : Surface.Renderer.Rasterize(document);
        var region = bounds.Value;
        _clipboard = RasterOperations.Crop(pixels, region.Item1, region.Item2, region.Item3, region.Item4);
        if (document.Selection is not null)
        {
            for (var y = 0; y < _clipboard.Height; y++)
                for (var x = 0; x < _clipboard.Width; x++)
                {
                    var color = _clipboard.Get(x, y);
                    _clipboard.Set(x, y, color.WithAlpha(Rgba32.Byte(color.A * document.Coverage(x + region.Item1, y + region.Item2))));
                }
        }
        if (cut) Session.ClearPixels();
        ShowStatus(cut ? "Active-layer pixels cut to the application clipboard." : "Merged pixels copied to the application clipboard.");
    });

    private void Paste() => Run(() =>
    {
        if (_clipboard is null) return;
        Session.Execute("Paste", document =>
        {
            var layer = Layer.Raster("Pasted pixels", _clipboard.Width, _clipboard.Height);
            layer.Pixels = _clipboard.Snapshot();
            layer.X = (document.Width - layer.Width) / 2;
            layer.Y = (document.Height - layer.Height) / 2;
            document.Layers.Add(layer);
            document.ActiveLayerId = layer.Id;
            document.EditMask = false;
        });
        SelectTool(EditorTool.Move);
    });

    private void RasterizeLayer() => Run(() =>
    {
        var document = Session.Document;
        var layer = document.ActiveLayer;
        if (layer is null || layer.Locked || layer.Kind == LayerKind.Adjustment) return;
        var pixels = Surface.Renderer.RasterizeLayer(document, layer);
        Session.Execute("Rasterize layer", _ =>
        {
            layer.Pixels = pixels;
            layer.Kind = LayerKind.Raster;
            layer.X = layer.Y = layer.Rotation = 0;
            layer.ScaleX = layer.ScaleY = 1;
            layer.Width = document.Width;
            layer.Height = document.Height;
            layer.Mask = null;
            document.EditMask = false;
        });
    });

    private void MergeDown() => Run(() =>
    {
        var document = Session.Document;
        var upper = document.ActiveLayer;
        var index = upper is null ? -1 : document.Layers.IndexOf(upper);
        if (index < 1 || upper!.Locked) return;
        var lower = document.Layers[index - 1];
        if (lower.Locked) throw new InvalidOperationException("Unlock the lower layer before merging.");
        if (upper.Kind == LayerKind.Adjustment || lower.Kind == LayerKind.Adjustment || upper.Blend != LayerBlend.Normal || lower.Blend != LayerBlend.Normal)
            throw new InvalidOperationException("For adjustments or interacting blend modes, use Flatten Image to preserve the visible result.");
        var pair = new ImageDocument(document.Width, document.Height) { Layers = [lower.Snapshot(), upper.Snapshot()] };
        var composite = Surface.Renderer.Rasterize(pair);
        Session.Execute("Merge down", state =>
        {
            var layer = Layer.Raster(upper.Name, document.Width, document.Height);
            layer.Pixels = composite;
            state.Layers.RemoveRange(index - 1, 2);
            state.Layers.Insert(index - 1, layer);
            state.ActiveLayerId = layer.Id;
            state.EditMask = false;
        });
    });

    private async Task FlattenAsync()
    {
        if (!await ConfirmAsync("Flatten image?", "All layers, including hidden layers, will be replaced by the visible composite. Undo remains available.", "Flatten")) return;
        Run(() =>
        {
            var composite = Surface.Renderer.Rasterize(Session.Document);
            Session.Execute("Flatten image", document =>
            {
                var layer = Layer.Raster("Background", document.Width, document.Height);
                layer.Pixels = composite;
                document.Layers = [layer];
                document.ActiveLayerId = layer.Id;
                document.EditMask = false;
            });
        });
    }

    private void FlipCanvas(bool horizontal) => Run(() => Session.Execute(horizontal ? "Flip canvas horizontal" : "Flip canvas vertical", document =>
    {
        foreach (var layer in document.Layers)
        {
            if (horizontal) { layer.X = document.Width - layer.X; layer.ScaleX = -layer.ScaleX; }
            else { layer.Y = document.Height - layer.Y; layer.ScaleY = -layer.ScaleY; }
            layer.Rotation = -layer.Rotation;
        }
        if (document.Selection is not null) document.Selection = RasterOperations.Flip(document.Selection, horizontal);
    }));

    private void Trim() => Run(() =>
    {
        var mask = Surface.Renderer.Rasterize(Session.Document);
        if (Selections.Bounds(mask) is not { } bounds) { ShowStatus("The canvas is fully transparent."); return; }
        Session.Crop(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        Surface.Fit();
    });

    private void TogglePanels()
    {
        _panelsHidden = !_panelsHidden;
        _workspace.ColumnDefinitions[3].Width = new GridLength(_panelsHidden ? 0 : 292);
        _workspace.ColumnDefinitions[2].Width = new GridLength(_panelsHidden ? 0 : 30);
    }

    private async Task ApplyFilterAsync(FilterKind kind, float amount = 0, float secondary = 0)
    {
        if (_busy || Session.IsInTransaction) return;
        var session = Session;
        var layer = session.Document.ActiveLayer;
        if (layer is not { Locked: false } || PixelTarget.Get(session.Document, layer) is not { } target)
        {
            ShowStatus("Select an unlocked pixel layer or its mask before applying a filter.");
            return;
        }
        var revision = session.Revision;
        var maskEditing = session.Document.EditMask;
        var source = target.Snapshot();
        var sourceRevision = target.Revision;
        _busy = true;
        _workspace.IsHitTestVisible = false;
        ShowStatus("Applying " + kind + "…");
        try
        {
            PixelSurface? output = null;
            var usedGpu = false;
            if (GpuFilter is not null && !maskEditing)
            {
                try { output = await GpuFilter(source, kind, amount, secondary); usedGpu = output is not null; }
                catch (Exception error) { ShowStatus("GPU unavailable; using the CPU kernel. " + error.Message); }
            }
            if (output is null)
            {
                await Task.Yield();
                output = OperatingSystem.IsBrowser()
                    ? PixelFilterPipeline.Apply(source, maskEditing, kind, amount, secondary)
                    : await Task.Run(() => PixelFilterPipeline.Apply(source, maskEditing, kind, amount, secondary));
            }
            // Do not apply an asynchronous result to a changed document, edit channel, layer or tile revision.
            if (!ReferenceEquals(session, Session) || session.Revision != revision || session.IsInTransaction ||
                !ReferenceEquals(session.Document.ActiveLayer, layer) || session.Document.EditMask != maskEditing ||
                !ReferenceEquals(PixelTarget.Get(session.Document, layer), target) || target.Revision != sourceRevision)
                throw new InvalidOperationException("The edit target changed while the filter was running. Its result was discarded.");
            session.Execute(kind.ToString(), document => PixelTarget.Replace(document, layer,
                PixelEdits.RestrictToSelection(document, layer, source, output)));
            ShowStatus(kind + " applied · " + (usedGpu ? "WebGPU compute" : maskEditing ? "Mask coverage kernel" : "CPU kernel"));
        }
        catch (Exception error) { ShowStatus(error.Message); }
        finally { _busy = false; _workspace.IsHitTestVisible = true; StateChanged?.Invoke(); }
    }
}
