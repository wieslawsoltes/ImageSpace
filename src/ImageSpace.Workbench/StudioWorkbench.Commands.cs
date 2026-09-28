using ImageSpace.Skia;

namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private void ShowMenu(string name, FrameworkElement anchor)
    {
        var active = Session.Document.ActiveLayer;
        var has = active is not null;
        var pixels = PixelTarget.Get(Session.Document, active) is not null;
        var editable = has && !active!.Locked;
        List<(string Label, string Shortcut, Action Action, bool Enabled)> items = [];
        void Item(string label, string shortcut, Action action, bool enabled = true) => items.Add((label, shortcut, action, enabled));
        void Line() => Item("-", "", () => { });
        switch (name)
        {
            case "File":
                Item("New…", "Ctrl+N", () => _ = NewAsync());
                Item("Open…", "Ctrl+O", () => _ = OpenAsync(false));
                Item("Place image…", "", () => _ = OpenAsync(true));
                Item("Open sample artwork", "", () => Run(() => AddDocument(SampleDocument.Create())));
                Line();
                Item("Save editable document", "Ctrl+S", () => _ = SaveAsync());
                Item("Export PNG…", "", () => _ = ExportAsync("png"));
                Item("Export JPEG…", "", () => _ = ExportAsync("jpg"));
                Item("Export WebP…", "", () => _ = ExportAsync("webp"));
                Item("Export layered PSD…", "", () => _ = ExportAsync("psd"));
                Line();
                Item("Close document", "", () => _ = CloseAsync(Session));
                Item("Clear recovery copy…", "", () => _ = ClearRecoveryAsync());
                break;
            case "Edit":
                Item("Undo" + (Session.History.Count > 0 ? " " + Session.History[^1].Name : ""), "Ctrl+Z", () => Run(Session.Undo), Session.CanUndo);
                Item("Redo", "Ctrl+Shift+Z", () => Run(Session.Redo), Session.CanRedo);
                Line();
                Item("Cut", "Ctrl+X", () => Copy(true), editable && pixels && !Session.Document.EditMask);
                Item("Copy merged", "Ctrl+C", () => Copy(false));
                Item("Paste as new layer", "Ctrl+V", Paste, _clipboard is not null);
                Item("Clear selected pixels", "Delete", () => Run(Session.ClearPixels), editable && pixels);
                Line();
                Item("Free transform", "Ctrl+T", () => { SelectTool(EditorTool.Move); Surface.ShowTransform = true; Surface.Invalidate(); }, has);
                Item("Fill with foreground", "Alt+Backspace", () => Fill(Surface.Foreground), editable && pixels);
                Item("Fill with background", "Ctrl+Backspace", () => Fill(Surface.BackgroundColor), editable && pixels);
                Item("Keyboard shortcuts", "", () => _ = HelpAsync());
                break;
            case "Image":
                Item("Image size…", "", () => _ = ImageSizeAsync(false));
                Item("Canvas size…", "", () => _ = ImageSizeAsync(true));
                Line();
                Item("Rotate 90° clockwise", "", () => Run(() => { Session.RotateCanvas(true); Surface.Fit(); }));
                Item("Rotate 90° counterclockwise", "", () => Run(() => { Session.RotateCanvas(false); Surface.Fit(); }));
                Item("Flip canvas horizontal", "", () => FlipCanvas(true));
                Item("Flip canvas vertical", "", () => FlipCanvas(false));
                Item("Trim transparent pixels", "", Trim);
                Line();
                Item("Curves adjustment layer", "Ctrl+M", () => AddAdjustment(AdjustmentKind.Curves));
                Item("Levels adjustment layer", "Ctrl+L", () => AddAdjustment(AdjustmentKind.Levels));
                Item("Brightness / Contrast…", "", () => _ = FilterDialogAsync(FilterKind.BrightnessContrast), editable && pixels);
                Item("Saturation…", "", () => _ = FilterDialogAsync(FilterKind.Saturation), editable && pixels);
                Item("Invert", "Ctrl+I", () => _ = ApplyFilterAsync(FilterKind.Invert), editable && pixels);
                Item("Grayscale", "", () => _ = ApplyFilterAsync(FilterKind.Grayscale), editable && pixels);
                break;
            case "Layer":
                Item("New pixel layer", "Ctrl+Shift+N", () => Run(() => Session.AddLayer("Layer " + (Session.Document.Layers.Count + 1))));
                Item("Duplicate layer", "Ctrl+J", () => Run(Session.DuplicateLayer), has);
                Item("Rename layer…", "", () => _ = RenameLayerAsync(), has);
                Item("Delete layer", "", () => Run(Session.DeleteLayer), editable);
                Line();
                Item("Rasterize layer", "", RasterizeLayer, editable && active?.Kind is not LayerKind.Raster and not LayerKind.Adjustment);
                Item("Merge down", "Ctrl+E", MergeDown, editable && Session.Document.Layers.IndexOf(active!) > 0);
                Item("Flatten image…", "", () => _ = FlattenAsync());
                Line();
                Item("Add layer mask", "", () => Run(Session.AddMask), editable && active?.Mask is null);
                Item(active?.MaskEnabled == false ? "Enable mask" : "Disable mask", "",
                    () => Run(() => Session.SetMaskEnabled(active?.MaskEnabled == false)), editable && active?.Mask is not null);
                Item("Invert mask", "", () => Run(Session.InvertMask), editable && active?.Mask is not null);
                Item("Delete mask", "", () => Run(Session.DeleteMask), editable && active?.Mask is not null);
                Line();
                Item("New adjustment layer…", "", () => ShowAdjustmentMenu(anchor));
                Item(active?.Locked == true ? "Unlock layer" : "Lock layer", "", () => Run(() => Session.Execute("Layer lock", document =>
                {
                    if (document.ActiveLayer is { } layer) layer.Locked = !layer.Locked;
                })), has);
                break;
            case "Type":
                Item("Edit text…", "", () => { if (active is not null) _ = EditTextAsync(active); }, active?.Kind == LayerKind.Text);
                Item("Bold / Regular", "", () => Run(() => Session.Execute("Text weight", _ => active!.Bold = !active.Bold)), active?.Kind == LayerKind.Text && !active.Locked);
                Item("Rasterize type", "", RasterizeLayer, active?.Kind == LayerKind.Text && !active.Locked);
                break;
            case "Select":
                Item("All", "Ctrl+A", SelectAll);
                Item("Deselect", "Ctrl+D", Deselect, Session.Document.Selection is not null);
                Item("Inverse", "Ctrl+Shift+I", InvertSelection);
                Line();
                Item("Feather…", "", () => _ = FeatherAsync(), Session.Document.Selection is not null);
                Item(Session.Document.EditMask ? "Select mask coverage" : "Select layer alpha", "", SelectLayerAlpha,
                    has && (Session.Document.EditMask ? active?.Mask is not null : active?.Kind != LayerKind.Adjustment));
                Item("Create layer mask from selection", "", () => Run(Session.AddMask), editable && active?.Mask is null);
                break;
            case "Filter":
                Item("Gaussian blur…", "", () => _ = FilterDialogAsync(FilterKind.GaussianBlur), editable && pixels);
                Item("Sharpen…", "", () => _ = FilterDialogAsync(FilterKind.Sharpen), editable && pixels);
                Item("Add noise…", "", () => _ = FilterDialogAsync(FilterKind.Noise), editable && pixels);
                Item("Pixelate…", "", () => _ = FilterDialogAsync(FilterKind.Pixelate), editable && pixels);
                Line();
                Item("Invert", "", () => _ = ApplyFilterAsync(FilterKind.Invert), editable && pixels);
                Item("Black and white", "", () => _ = ApplyFilterAsync(FilterKind.Grayscale), editable && pixels);
                Item("Sepia", "", () => _ = ApplyFilterAsync(FilterKind.Sepia), editable && pixels);
                Item("Emboss", "", () => _ = ApplyFilterAsync(FilterKind.Emboss), editable && pixels);
                Item("Find edges", "", () => _ = ApplyFilterAsync(FilterKind.Edges), editable && pixels);
                Item("Posterize…", "", () => _ = FilterDialogAsync(FilterKind.Posterize), editable && pixels);
                Item("Threshold…", "", () => _ = FilterDialogAsync(FilterKind.Threshold), editable && pixels);
                Item("Gamma…", "", () => _ = FilterDialogAsync(FilterKind.Gamma), editable && pixels);
                break;
            case "View":
                Item("Fit on screen", "Ctrl+0", Surface.Fit);
                Item("100%", "Ctrl+1", () => Surface.SetZoom(1));
                Item("Zoom in", "+", () => Surface.SetZoom(Surface.Zoom * 1.25f));
                Item("Zoom out", "−", () => Surface.SetZoom(Surface.Zoom * .8f));
                Line();
                Item((Surface.ShowRulers ? "✓ " : "") + "Rulers", "Ctrl+R", () => { Surface.ShowRulers = !Surface.ShowRulers; Surface.Invalidate(); });
                Item((Surface.ShowGrid ? "✓ " : "") + "Pixel grid", "", () => { Surface.ShowGrid = !Surface.ShowGrid; Surface.Invalidate(); });
                Item("Hide / show panels", "Tab", TogglePanels);
                break;
            case "Window":
                Item("Layers", "", () => SetBottomMode("Layers"));
                Item("Channels and histogram", "", () => SetBottomMode("Channels"));
                Item("History", "", () => SetBottomMode("History"));
                Line();
                foreach (var document in _documents)
                {
                    var captured = document;
                    Item((document == Session ? "✓ " : "") + document.Document.Name, "", () => Switch(captured));
                }
                break;
            default:
                Item("ImageSpace user guide", "F1", () => _ = HelpAsync());
                Item("Features and compatibility", "", () => _ = CapabilitiesAsync());
                Item("Rendering diagnostics", "", () => _ = DiagnosticsAsync());
                Item("About ImageSpace", "", () => _ = AboutAsync());
                break;
        }
        Studio.Menu(anchor, items);
    }

    private void ShowAdjustmentMenu(FrameworkElement anchor)
    {
        AdjustmentKind[] kinds = [AdjustmentKind.Curves, AdjustmentKind.Levels, AdjustmentKind.BrightnessContrast,
            AdjustmentKind.Saturation, AdjustmentKind.Invert, AdjustmentKind.Grayscale, AdjustmentKind.Sepia, AdjustmentKind.GaussianBlur];
        Studio.Menu(anchor, kinds.Select(kind => (kind.ToString(), "", (Action)(() => AddAdjustment(kind)), true)));
    }

    private void AddAdjustment(AdjustmentKind kind) => Run(() => Session.Execute("New " + kind + " adjustment", document =>
    {
        var layer = new Layer
        {
            Name = kind.ToString(), Kind = LayerKind.Adjustment, Adjustment = kind,
            Amount = kind == AdjustmentKind.GaussianBlur ? 4 : 0, Width = document.Width, Height = document.Height
        };
        var index = document.ActiveLayer is { } active ? document.Layers.IndexOf(active) + 1 : document.Layers.Count;
        document.Layers.Insert(index, layer);
        document.ActiveLayerId = layer.Id;
        document.EditMask = false;
    }));
}
