using ImageSpace.Documents;
using ImageSpace.Skia;
using SkiaSharp;
namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private async Task<ContentDialogResult> ShowDialogAsync(string title, UIElement content, string primary = "OK", string close = "Cancel")
    {
        if (_dialogOpen)
            return ContentDialogResult.None;
        _dialogOpen = true;
        try
        {
            Surface.IsSpaceDown = false;
            var dialog = new ContentDialog { Title = title, Content = content, PrimaryButtonText = primary, CloseButtonText = close, DefaultButton = ContentDialogButton.Primary, XamlRoot = XamlRoot, RequestedTheme = ElementTheme.Dark, Background = Studio.Brush("#323232"), Foreground = Studio.Brush("#dddddd"), FontFamily = Studio.Font };
            return await dialog.ShowAsync();
        }
        finally { _dialogOpen = false; }
    }
    private async Task<bool> ConfirmAsync(string title, string message, string action) => await ShowDialogAsync(title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 430, FontFamily = Studio.Font, FontSize = 13 }, action) == ContentDialogResult.Primary;
    private async Task NewAsync()
    {
        var name = Studio.TextInput("Untitled", "Document name", 380);
        var width = new NumericField("Width", 1600, 1, 8192, 184);
        var height = new NumericField("Height", 1000, 1, 8192, 184);
        var background = new CheckBox { Content = "White background", IsChecked = true, FontSize = 12 };
        var panel = new StackPanel { Spacing = 13 };
        panel.Children.Add(Studio.Label("Create a document", 14));
        panel.Children.Add(name);
        panel.Children.Add(Studio.Row(width, height));
        panel.Children.Add(background);
        panel.Children.Add(Studio.Label("RGB · 8 bits/channel · sRGB · maximum 16 megapixels", 11, "#aaaaaa"));
        if (await ShowDialogAsync("New document", panel, "Create") != ContentDialogResult.Primary)
            return;
        Run(() => { var d = new ImageDocument((int)width.Value, (int)height.Value, string.IsNullOrWhiteSpace(name.Text) ? "Untitled" : name.Text.Trim()); var layer = Layer.Raster(background.IsChecked == true ? "Background" : "Layer 1", d.Width, d.Height); if (background.IsChecked == true) layer.Pixels!.Fill(Rgba32.White); d.Layers.Add(layer); d.ActiveLayerId = layer.Id; AddDocument(d, true); });
    }
    private async Task ImageSizeAsync(bool canvasOnly)
    {
        var d = Session.Document;
        var width = new NumericField("Width", d.Width, 1, 8192, 180);
        var height = new NumericField("Height", d.Height, 1, 8192, 180);
        var panel = new StackPanel { Spacing = 13 };
        panel.Children.Add(Studio.Row(width, height));
        panel.Children.Add(Studio.Label(canvasOnly ? "Canvas resize is centered and preserves pixels outside the canvas." : "Layers retain original pixels and use non-destructive scaling.", 11, "#aaaaaa"));
        if (await ShowDialogAsync(canvasOnly ? "Canvas size" : "Image size", panel, "Resize") != ContentDialogResult.Primary)
            return;
        Run(() => { if (canvasOnly) Session.Crop((d.Width - (int)width.Value) / 2, (d.Height - (int)height.Value) / 2, (int)width.Value, (int)height.Value); else Session.ResizeImage((int)width.Value, (int)height.Value); Surface.Fit(); });
    }
    private async Task RenameLayerAsync()
    {
        if (Session.Document.ActiveLayer is not { } l)
            return;
        var input = Studio.TextInput(l.Name, "Layer name", 360);
        if (await ShowDialogAsync("Rename layer", input, "Rename") != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(input.Text))
            return;
        Run(() => Session.Execute("Rename layer", _ => l.Name = input.Text.Trim()));
    }
    private async Task EditTextAsync(Layer layer)
    {
        if (layer.Kind != LayerKind.Text || layer.Locked)
            return;
        var input = Studio.TextInput(layer.Text, "Text content", 440);
        input.AcceptsReturn = true;
        input.TextWrapping = TextWrapping.Wrap;
        input.MinHeight = 150;
        input.MaxHeight = 350;
        var size = new NumericField("Size", layer.FontSize, 1, 4096, 170);
        var bold = new CheckBox { Content = "Bold", IsChecked = layer.Bold, FontSize = 12 };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(input);
        panel.Children.Add(Studio.Row(size, bold));
        if (await ShowDialogAsync("Edit type layer", panel, "Apply") != ContentDialogResult.Primary)
            return;
        Run(() => Session.Execute("Edit text", _ => { layer.Text = input.Text; layer.FontSize = (float)size.Value; layer.Bold = bold.IsChecked == true; var lines = layer.Text.Split('\n'); layer.Width = Math.Max(1, lines.Max(x => x.Length) * layer.FontSize * .64f); layer.Height = Math.Max(1, lines.Length * layer.FontSize * 1.18f); }));
    }
    private async Task ColorAsync(bool foreground)
    {
        var initial = foreground ? Surface.Foreground : Surface.BackgroundColor;
        var value = Studio.TextInput(initial.Hex, "Hex color", 240);
        var spectrum = new ColorSpectrum { Width = 350, Height = 220 };
        spectrum.ColorChanged += c => value.Text = c.Hex;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(spectrum);
        panel.Children.Add(value);
        panel.Children.Add(Studio.Label("Enter a hexadecimal RGB color, for example #3691E6.", 11, "#aaaaaa"));
        if (await ShowDialogAsync(foreground ? "Foreground color" : "Background color", panel, "Choose") != ContentDialogResult.Primary)
            return;
        Run(() => { var c = Rgba32.Parse(value.Text); if (foreground) Surface.Foreground = c; else Surface.BackgroundColor = c; RefreshColors(); RefreshOptions(); });
    }
    private async Task FeatherAsync()
    {
        if (Session.Document.Selection is null)
            return;
        var radius = new NumericField("Radius", 3, .1, 32, 230);
        if (await ShowDialogAsync("Feather selection", radius, "Apply") != ContentDialogResult.Primary)
            return;
        Run(() => Session.Execute("Feather selection", d => d.Selection = FilterEngine.Blur(d.Selection!, (float)radius.Value)));
    }
    private async Task FilterDialogAsync(FilterKind kind)
    {
        var (initial, min, max) = kind switch
        {
            FilterKind.GaussianBlur => (4d, .1, 32d),
            FilterKind.Sharpen => (1d, .1, 5d),
            FilterKind.Noise => (12d, 0d, 100d),
            FilterKind.Pixelate => (12d, 2d, 128d),
            FilterKind.Posterize => (5d, 2d, 256d),
            FilterKind.Threshold => (128d, 0d, 255d),
            FilterKind.Gamma => (1d, .1, 10d),
            _ => (0d, -100d, 100d)
        };
        var amount = new NumericField(kind == FilterKind.BrightnessContrast ? "Brightness" : "Amount", initial, min, max, 340);
        var second = new NumericField("Contrast", 0, -99, 300, 340);
        var panel = new StackPanel { Spacing = 13 };
        panel.Children.Add(amount);
        if (kind == FilterKind.BrightnessContrast)
            panel.Children.Add(second);
        panel.Children.Add(Studio.Label("Applies to the selected pixels. Undo is available after applying.", 11, "#aaaaaa"));
        if (await ShowDialogAsync(kind.ToString(), panel, "Apply") == ContentDialogResult.Primary)
            await ApplyFilterAsync(kind, (float)amount.Value, (float)second.Value);
    }
    private Task HelpAsync()
    {
        const string help = "GETTING STARTED\nOpen an image with File → Open, or create a document with Ctrl+N. The sample contains separate pixel, shape and editable text layers.\n\nPAINTING\nCreate a pixel layer with the + button below Layers. Select Brush (B), set size, hardness, opacity and flow in the options bar, then paint. [ and ] change size. E erases. Pen pressure affects brush radius and opacity. Alt-click with Clone (S) sets the source.\n\nSELECTIONS AND MASKS\nM draws a rectangle; Shift+M selects an ellipse; L is lasso; W is contiguous color selection. Shift adds, Alt subtracts. Ctrl+D deselects. Select → Feather softens edges. Add a layer mask to turn the selection into non-destructive transparency. Painting black on the mask hides; white reveals.\n\nTRANSFORMS\nV selects and moves layers. Drag any of eight handles to resize, or the upper handle to rotate. Shift constrains motion, proportions or rotation. X/Y/W/H/Angle fields allow precise edits. C previews a crop; Enter applies and Escape cancels. Cropping preserves off-canvas pixels.\n\nLAYERS AND TYPE\nUse eye buttons for visibility, the lock button for protection, and blend/opacity controls above the layer list. Double-click a layer name to rename. T adds editable text; double-click text to edit. Shapes and type can be rasterized from the Layer menu.\n\nFILTERS\nColor kernels use WebGPU when available in the browser, otherwise the deterministic CPU implementation. Adjustment layers apply live to layers below them. Filter commands respect selections.\n\nFILES AND RECOVERY\n.imagespace preserves editable ImageSpace layers, transforms, pixels and masks. PNG/JPEG/WebP export the composite. PSD is a bounded RGB/8 raster-layer interchange format, not a lossless Photoshop editor. The original PSD must be retained. A recovery copy of the active document is saved every eight seconds after changes. It is local to this browser or desktop profile, not a cloud backup.\n\nKEYBOARD\nCtrl+N/O/S: new/open/save. Ctrl+Z/Shift+Z: undo/redo. Ctrl+J: duplicate layer. Ctrl+A/D/Shift+I: select all/deselect/invert. Ctrl+C/V: application clipboard. Ctrl+0/1: fit/100%. Space: temporary hand. X: swap colors. D: reset black/white. Tab: hide panels. Arrow keys move the selected layer; Shift moves by 10 pixels. Browser-reserved shortcuts vary by browser.\n\nLIMITS\nRGB, 8-bit, sRGB workflow. Up to 8192 pixels per side and 16 megapixels per surface, 128 layers per document, 12 open documents. Complex edits can still consume substantial memory. See Help → Features and compatibility for the current parity boundary.";
        return ShowTextAsync("ImageSpace user guide", help);
    }
    private Task CapabilitiesAsync() => ShowTextAsync("Features and compatibility", "IMPLEMENTED\nLayered RGBA pixels; sparse copy-on-write tiles; bounded undo/redo; pressure-aware brush, pencil, eraser, clone stamp, basic smudge/dodge/burn; rectangular, elliptical, lasso and contiguous selections; feather, invert and alpha selections; gradient and flood fill; eight-handle transforms and rotation; non-destructive canvas crop/resize; editable multiline text and vector rectangles/ellipses; layer masks, visibility, locks, 16 blend modes and opacity; 14 CPU filters and six live adjustment types; native archives and bounded raster PSD; PNG/JPEG/WebP import/export; local recovery; desktop and genuine Uno WebAssembly hosts.\n\nNOT PHOTOSHOP PARITY\nNo Camera Raw, CMYK/Lab/spot channels, 16/32-bit HDR, ICC proofing, Photoshop smart objects, adjustment compatibility, vector paths/pen tool, healing/content-aware reconstruction, AI/generative fill, liquify, puppet/perspective warps, layer effects, advanced typography, font discovery, group/clipping semantics, actions/macros, plug-ins, timeline/video, Photoshop cloud services, PSB, or lossless PSD roundtrips. The interface follows Photoshop workspace conventions, but is not pixel-identical.\n\nPSD DETAILS\nPSD v1 RGB/8, raw or PackBits pixel layers, positions, visibility, opacity and supported blend keys. Masks, groups, text, profiles, smart objects and unsupported metadata are not preserved. Export rasterizes editable type, shapes and transforms; live adjustment documents export a flattened compatibility image. Native .imagespace is the editable roundtrip format.\n\nGPU DETAILS\nUno's Skia renderer presents the workspace and document using the host's available graphics backend. Browser WebGPU is an optional compute accelerator with readback to the shared tile model; it is not a GPU-only Photoshop engine. Software rendering remains possible when the host has no compatible GPU.");
    private Task DiagnosticsAsync() => ShowTextAsync("Rendering diagnostics", $"Application: ImageSpace 0.1.0-alpha.1\nHost: {(OperatingSystem.IsBrowser() ? "Uno WebAssembly" : "Uno desktop")}\nCompute: {ComputeBackend}\nSkia managed/native ABI: 3.119.2\nCanvas: {Surface.ActualWidth:0} × {Surface.ActualHeight:0} logical px\nZoom: {Surface.Zoom * 100:0.0}%\nLast draw submission: {Surface.Renderer.LastRenderMilliseconds:0.00} ms (CPU submission, not GPU time)\nCached tile images: {Surface.Renderer.CachedTiles}\nTile uploads: {Surface.Renderer.TileUploads}\nHistory states: {Session.History.Count}\nHistory target budget: {Session.HistoryBudget / 1024 / 1024} MiB\n\nHardware GPU timing and physical device performance are not inferred from these counters.");
    private Task AboutAsync() => ShowTextAsync("About ImageSpace", "ImageSpace\nIndependent, local-first image editing.\n\nVersion 0.1.0-alpha.1\nBuilt with Uno Platform 6.7.30, .NET 10 and SkiaSharp 3.119.2.\n\nImageSpace source: MIT. Uno Platform: Apache-2.0. SkiaSharp: MIT. Skia: BSD-3-Clause. Inter: SIL Open Font License. See THIRD-PARTY-NOTICES.md.\n\nNo Adobe code, icons, fonts, sample images or cloud services are included. Photoshop is an Adobe trademark. ImageSpace is not affiliated with or endorsed by Adobe.");
    private async Task ShowTextAsync(string title, string text)
    {
        await ShowDialogAsync(title, new ScrollViewer { MaxHeight = 520, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 540, FontSize = 12, FontFamily = Studio.Font, LineHeight = 19 } }, "Done", "");
    }
}
