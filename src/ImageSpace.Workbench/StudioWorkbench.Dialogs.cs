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
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = primary,
                CloseButtonText = close,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = ElementTheme.Dark,
                Background = Studio.Brush("#323232"),
                Foreground = Studio.Brush("#dddddd"),
                FontFamily = Studio.Font
            };
            return await dialog.ShowAsync();
        }
        finally { _dialogOpen = false; }
    }

    private async Task<bool> ConfirmAsync(string title, string message, string action) =>
        await ShowDialogAsync(title, new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 430,
            FontFamily = Studio.Font,
            FontSize = 13
        }, action) == ContentDialogResult.Primary;

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
        Run(() =>
        {
            var document = new ImageDocument((int)width.Value, (int)height.Value, string.IsNullOrWhiteSpace(name.Text) ? "Untitled" : name.Text.Trim());
            var layer = Layer.Raster(background.IsChecked == true ? "Background" : "Layer 1", document.Width, document.Height);
            if (background.IsChecked == true)
                layer.Pixels!.Fill(Rgba32.White);
            document.Layers.Add(layer);
            document.ActiveLayerId = layer.Id;
            AddDocument(document, true);
        });
    }

    private async Task ImageSizeAsync(bool canvasOnly)
    {
        var document = Session.Document;
        var width = new NumericField("Width", document.Width, 1, 8192, 180);
        var height = new NumericField("Height", document.Height, 1, 8192, 180);
        var panel = new StackPanel { Spacing = 13 };
        panel.Children.Add(Studio.Row(width, height));
        panel.Children.Add(Studio.Label(canvasOnly ? "Centered canvas resize preserves off-canvas pixels." : "Layers retain original pixels with non-destructive scaling.", 11, "#aaaaaa"));
        if (await ShowDialogAsync(canvasOnly ? "Canvas size" : "Image size", panel, "Resize") != ContentDialogResult.Primary)
            return;
        Run(() =>
        {
            if (canvasOnly)
                Session.Crop((document.Width - (int)width.Value) / 2, (document.Height - (int)height.Value) / 2, (int)width.Value, (int)height.Value);
            else
                Session.ResizeImage((int)width.Value, (int)height.Value);
            Surface.Fit();
        });
    }

    private async Task RenameLayerAsync()
    {
        if (Session.Document.ActiveLayer is not { } layer)
            return;
        var input = Studio.TextInput(layer.Name, "Layer name", 360);
        if (await ShowDialogAsync("Rename layer", input, "Rename") != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(input.Text))
            return;
        Run(() => Session.Execute("Rename layer", _ => layer.Name = input.Text.Trim()));
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
        Run(() => Session.Execute("Edit text", _ =>
        {
            layer.Text = input.Text;
            layer.FontSize = (float)size.Value;
            layer.Bold = bold.IsChecked == true;
            var lines = layer.Text.Split('\n');
            layer.Width = Math.Max(1, lines.Max(line => line.Length) * layer.FontSize * .64f);
            layer.Height = Math.Max(1, lines.Length * layer.FontSize * 1.18f);
        }));
    }

    private async Task ColorAsync(bool foreground)
    {
        var value = Studio.TextInput((foreground ? Surface.Foreground : Surface.BackgroundColor).Hex, "Hex color", 240);
        var spectrum = new ColorSpectrum { Width = 350, Height = 220 };
        spectrum.ColorChanged += color => value.Text = color.Hex;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(spectrum);
        panel.Children.Add(value);
        panel.Children.Add(Studio.Label("Enter a hexadecimal RGB color, for example #3691E6.", 11, "#aaaaaa"));
        if (await ShowDialogAsync(foreground ? "Foreground color" : "Background color", panel, "Choose") != ContentDialogResult.Primary)
            return;
        Run(() =>
        {
            var color = Rgba32.Parse(value.Text);
            if (foreground)
                Surface.Foreground = color;
            else
                Surface.BackgroundColor = color;
            RefreshColors();
            RefreshOptions();
        });
    }

    private async Task FeatherAsync()
    {
        if (Session.Document.Selection is null)
            return;
        var radius = new NumericField("Radius", 3, .1, 32, 230);
        if (await ShowDialogAsync("Feather selection", radius, "Apply") != ContentDialogResult.Primary)
            return;
        Run(() => Session.Execute("Feather selection", document => document.Selection = FilterEngine.Blur(document.Selection!, (float)radius.Value)));
    }

    private async Task FilterDialogAsync(FilterKind kind)
    {
        var (initial, minimum, maximum) = kind switch
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
        var amount = new NumericField(kind == FilterKind.BrightnessContrast ? "Brightness" : "Amount", initial, minimum, maximum, 340);
        var contrast = new NumericField("Contrast", 0, -99, 300, 340);
        var panel = new StackPanel { Spacing = 13 };
        panel.Children.Add(amount);
        if (kind == FilterKind.BrightnessContrast)
            panel.Children.Add(contrast);
        panel.Children.Add(Studio.Label("Applies to selected pixels. Undo is available after applying.", 11, "#aaaaaa"));
        if (await ShowDialogAsync(kind.ToString(), panel, "Apply") == ContentDialogResult.Primary)
            await ApplyFilterAsync(kind, (float)amount.Value, (float)contrast.Value);
    }

    private Task HelpAsync() => ShowTextAsync("ImageSpace user guide", StudioGuide.UserGuide);
    private Task CapabilitiesAsync() => ShowTextAsync("Features and compatibility", StudioGuide.Capabilities);
    private Task DiagnosticsAsync() => ShowTextAsync("Rendering diagnostics",
        $"Application: ImageSpace 0.2.0-alpha.1\nHost: {(OperatingSystem.IsBrowser() ? "Uno WebAssembly" : "Uno desktop")}\nCompute: {ComputeBackend}\nSkia managed/native ABI: 3.119.4\nCanvas: {Surface.ActualWidth:0} × {Surface.ActualHeight:0} logical px\nZoom: {Surface.Zoom * 100:0.0}%\nLast draw submission: {Surface.Renderer.LastRenderMilliseconds:0.00} ms (CPU submission, not GPU time)\nCached tile images: {Surface.Renderer.CachedTiles}\nTile uploads: {Surface.Renderer.TileUploads}\nTone filter builds: {Surface.Renderer.ToneFilterBuilds}\nHistory states: {Session.History.Count}\nHistory target budget: {Session.HistoryBudget / 1024 / 1024} MiB\n\nPhysical GPU timing and device performance are not inferred from these counters.");
    private Task AboutAsync() => ShowTextAsync("About ImageSpace",
        "ImageSpace\nIndependent, local-first image editing.\n\nVersion 0.2.0-alpha.1\nBuilt with Uno Platform 6.7.30, .NET 10 and SkiaSharp 3.119.4.\n\nImageSpace: MIT. Uno: Apache-2.0. SkiaSharp: MIT. Skia: BSD-3-Clause. Inter: SIL OFL. See THIRD-PARTY-NOTICES.md.\n\nNo Adobe code, icons, fonts, sample images or cloud services are included. Photoshop is an Adobe trademark. ImageSpace is not affiliated with Adobe.");
    private async Task ShowTextAsync(string title, string text)
    {
        await ShowDialogAsync(title, new ScrollViewer
        {
            MaxHeight = 520,
            Content = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 540,
                FontSize = 12,
                FontFamily = Studio.Font,
                LineHeight = 19
            }
        }, "Done", "");
    }
}
