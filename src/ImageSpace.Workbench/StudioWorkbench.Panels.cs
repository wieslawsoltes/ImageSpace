namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private readonly Dictionary<string, StudioButton> _panelTabs = [];

    private UIElement BuildRightPanels()
    {
        var grid = new AdaptivePanelGrid
        {
            Background = Studio.Brush("#303030"),
            BorderBrush = Studio.Brush("#171717"),
            BorderThickness = new Thickness(1, 0, 0, 0)
        };
        _properties.Renderer = Surface.Renderer;
        _properties.PreviewInvalidated += Surface.Invalidate;
        _properties.PreferredHeightChanged += height =>
        {
            grid.PreferredMiddleHeight = Session.Document.ActiveLayer?.Adjustment == AdjustmentKind.Levels ? Math.Max(height, 470) : height;
            grid.UpdateLayoutAllocation();
        };
        var color = new Grid
        {
            RowDefinitions = { new() { Height = new GridLength(30) }, new() { Height = new GridLength(1, GridUnitType.Star) } }
        };
        color.Children.Add(PanelHeader("Color", "Swatches", ShowSwatches));
        var spectrum = new ColorSpectrum { Height = 122 };
        spectrum.ColorChanged += value => { Surface.Foreground = value; RefreshColors(); };
        grid.TopHeightChanged += height => spectrum.Height = Math.Max(26, height - 70);
        var colors = new StackPanel { Spacing = 5, Margin = new Thickness(11, 7, 11, 7) };
        colors.Children.Add(spectrum);
        colors.Children.Add(Studio.Row(_colorHex, Studio.Label("  sRGB", 10, "#777777"),
            new StudioButton("Edit color…", () => _ = ColorAsync(true)) { Height = 20, Padding = new Thickness(7, 0, 7, 0) }));
        Grid.SetRow(colors, 1);
        color.Children.Add(colors);
        grid.Children.Add(color);

        var properties = new Grid
        {
            RowDefinitions = { new() { Height = new GridLength(30) }, new() { Height = new GridLength(1, GridUnitType.Star) } }
        };
        properties.Children.Add(PanelHeader("Properties", "Adjustments", () => ShowAdjustmentMenu(_properties)));
        Grid.SetRow(_properties, 1);
        properties.Children.Add(_properties);
        Grid.SetRow(properties, 1);
        grid.Children.Add(properties);

        var bottom = new Grid
        {
            RowDefinitions = { new() { Height = new GridLength(30) }, new() { Height = new GridLength(1, GridUnitType.Star) } }
        };
        var modes = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var name in new[] { "Layers", "Channels", "History" })
        {
            var button = new StudioButton(name, () => SetBottomMode(name))
            {
                Height = 29, Padding = new Thickness(11, 0, 11, 0),
                CornerRadius = new CornerRadius(0), BorderThickness = new Thickness(0)
            };
            button.Selected(name == _bottomMode);
            _panelTabs[name] = button;
            modes.Children.Add(button);
        }
        bottom.Children.Add(Studio.Box(modes, "#272727"));
        _bottomPanel.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _bottomPanel.VerticalContentAlignment = VerticalAlignment.Stretch;
        _bottomPanel.Content = _layers;
        Grid.SetRow(_bottomPanel, 1);
        bottom.Children.Add(_bottomPanel);
        Grid.SetRow(bottom, 2);
        grid.Children.Add(bottom);
        return grid;
    }

    private void SetBottomMode(string mode)
    {
        _bottomMode = mode;
        foreach (var (name, button) in _panelTabs) button.Selected(name == mode);
        _bottomPanel.Content = mode switch
        {
            "History" => new ScrollViewer { Content = _history, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            "Channels" => new ScrollViewer { Content = _channels, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            _ => _layers
        };
        if (mode == "Channels") UpdateHistogram();
    }

    private void RefreshHistory()
    {
        _history.Children.Clear();
        _history.Children.Add(Studio.Box(Studio.Label($"  History states  ·  {Session.History.Count}", 11, "#a7a7a7"), "#303030", new Thickness(6, 10, 6, 8)));
        _history.Children.Add(new StudioButton("Open document", () => Run(() =>
        {
            while (Session.CanUndo) Session.Undo();
        }))
        {
            Height = 32, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 3, 8, 3)
        });
        for (var index = 0; index < Session.History.Count; index++)
        {
            var target = index + 1;
            var button = new StudioButton(Session.History[index].Name, () => Run(() =>
            {
                while (Session.History.Count > target) Session.Undo();
            }))
            {
                Height = 31, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(20, 3, 8, 3)
            };
            button.Selected(index == Session.History.Count - 1);
            _history.Children.Add(button);
        }
        _history.Children.Add(Studio.Row(new StudioButton("Undo", () => Run(Session.Undo), "undo"), new StudioButton("Redo", () => Run(Session.Redo), "redo")));
    }

    private void RefreshChannels()
    {
        _channels.Children.Clear();
        _channels.Margin = new Thickness(9);
        _channels.Children.Add(Studio.Label("Composite histogram", 12));
        _channels.Children.Add(_histogram);
        foreach (var (name, index) in new[] { ("RGB", -1), ("Red", 0), ("Green", 1), ("Blue", 2) })
        {
            _channels.Children.Add(new StudioButton(name + " histogram", () => UpdateHistogram(index))
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left, Height = 30
            });
        }
        _channels.Children.Add(new StudioButton("Load alpha as selection", () => Run(() =>
        {
            var composite = Surface.Renderer.Rasterize(Session.Document);
            Session.Execute("Select composite alpha", document =>
            {
                var selection = new PixelSurface(document.Width, document.Height);
                for (var y = 0; y < document.Height; y++)
                    for (var x = 0; x < document.Width; x++) selection.Set(x, y, new Rgba32(255, 255, 255, composite.Get(x, y).A));
                document.Selection = selection;
            });
        })));
    }

    private void UpdateHistogram(int channel = -1)
    {
        try { _histogram.Values = RasterOperations.Histogram(Surface.Renderer.Rasterize(Session.Document), channel); }
        catch (Exception error) { ShowStatus(error.Message); }
    }

    private void ShowSwatches()
    {
        var panel = new StackPanel { Spacing = 8 };
        string[] colors = ["#101820", "#ffffff", "#e94848", "#f39b46", "#f5d76e", "#73be6e", "#49a9a0", "#4e9dd9",
            "#575fcf", "#a766ca", "#d764a0", "#a47255", "#e9d4b5", "#53616b", "#b8c7c3", "#263e4e"];
        for (var y = 0; y < 4; y++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var hex in colors.Skip(y * 4).Take(4))
            {
                row.Children.Add(new StudioButton(hex, () => { Surface.Foreground = Rgba32.Parse(hex); RefreshColors(); })
                {
                    Width = 48, Height = 36, Background = Studio.Brush(hex), Content = null
                });
            }
            panel.Children.Add(row);
        }
        _ = ShowDialogAsync("Swatches", panel, "Done");
    }
}
