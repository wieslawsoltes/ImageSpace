namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private sealed record ToolOptions(StackPanel Panel, List<Action> Update);
    private readonly Dictionary<EditorTool, ToolOptions> _toolOptions = [];
    private ToolOptions? _visibleOptions;
    public long OptionsBuilds { get; private set; }

    private void RefreshOptions()
    {
        if (!_toolOptions.TryGetValue(Surface.Tool, out var options))
        {
            options = BuildOptions(Surface.Tool);
            _toolOptions.Add(Surface.Tool, options);
            OptionsBuilds++;
        }
        if (!ReferenceEquals(_visibleOptions, options))
        {
            _options.Children.Clear();
            _options.Children.Add(options.Panel);
            _visibleOptions = options;
        }
        foreach (var update in options.Update) update();
    }

    private ToolOptions BuildOptions(EditorTool tool)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        List<Action> updates = [];
        var title = Studio.Label("");
        panel.Children.Add(title);
        updates.Add(() => title.Text = tool + (Session.Document.EditMask ? " · Mask" : ""));
        panel.Children.Add(new Border { Width = 1, Height = 22, Background = Studio.Brush("#1f1f1f") });
        void Number(string name, double min, double max, double width, Func<double> read, Action<double> write)
        {
            var field = new NumericField(name, read(), min, max, width);
            field.ValueChanged += write;
            panel.Children.Add(field);
            updates.Add(() => field.Value = read());
        }
        void Tolerance() => Number("Tolerance", 0, 255, 120, () => Surface.Tolerance, value => Surface.Tolerance = (int)value);
        if (tool is EditorTool.Brush or EditorTool.Pencil or EditorTool.Eraser or EditorTool.Clone or EditorTool.Dodge or EditorTool.Burn or EditorTool.Smudge)
        {
            Number("Size", 1, 1024, 93, () => Surface.Brush.Size, value => Surface.Brush = Surface.Brush with { Size = (float)value });
            Number("Hardness", 0, 100, 116, () => Surface.Brush.Hardness * 100, value => Surface.Brush = Surface.Brush with { Hardness = (float)value / 100 });
            Number("Opacity", 1, 100, 108, () => Surface.Brush.Opacity * 100, value => Surface.Brush = Surface.Brush with { Opacity = (float)value / 100 });
            Number("Flow", 1, 100, 94, () => Surface.Brush.Flow * 100, value => Surface.Brush = Surface.Brush with { Flow = (float)value / 100 });
            var pressure = new StudioButton("Pressure", () => { Surface.Brush = Surface.Brush with { Pressure = !Surface.Brush.Pressure }; RefreshOptions(); });
            panel.Children.Add(pressure);
            updates.Add(() => pressure.SetLabel(Surface.Brush.Pressure ? "Pressure ✓" : "Pressure"));
            panel.Children.Add(new StudioButton("New paint layer", () => Run(() => Session.AddLayer("Paint")), "plus"));
        }
        else if (tool == EditorTool.Move)
        {
            panel.Children.Add(Studio.Label("Auto-select: Layer", 11));
            var transform = new StudioButton("Transform controls", () => { Surface.ShowTransform = !Surface.ShowTransform; Surface.InvalidateOverlay(); RefreshOptions(); });
            panel.Children.Add(transform);
            updates.Add(() => transform.SetLabel(Surface.ShowTransform ? "Transform controls ✓" : "Transform controls"));
            panel.Children.Add(new StudioButton("Fit on screen", Surface.Fit));
            panel.Children.Add(new StudioButton("100%", () => Surface.SetZoom(1)));
        }
        else if (tool is EditorTool.Marquee or EditorTool.EllipseSelect or EditorTool.Lasso or EditorTool.Wand)
        {
            panel.Children.Add(Studio.Label("New  ·  Shift: add  ·  Alt: subtract", 11));
            Tolerance();
            panel.Children.Add(new StudioButton("Deselect", Deselect));
            panel.Children.Add(new StudioButton("Invert selection", InvertSelection));
            panel.Children.Add(new StudioButton("Feather…", () => _ = FeatherAsync()));
        }
        else if (tool == EditorTool.Crop)
        {
            panel.Children.Add(Studio.Label("Non-destructive canvas crop", 11));
            panel.Children.Add(new StudioButton("Apply crop", () => Run(Surface.ApplyCrop)));
            panel.Children.Add(new StudioButton("Cancel", Surface.CancelGesture));
        }
        else if (tool == EditorTool.Text)
        {
            panel.Children.Add(Studio.Label("Inter    Regular / Bold    Anti-alias: smooth", 11));
            panel.Children.Add(new StudioButton("Edit selected text…", () => { if (Session.Document.ActiveLayer is { Kind: LayerKind.Text } layer) _ = EditTextAsync(layer); }));
        }
        else if (tool is EditorTool.Rectangle or EditorTool.Ellipse)
        {
            var label = Studio.Label("", 11);
            panel.Children.Add(label);
            updates.Add(() => label.Text = $"Shape  ·  Fill: {Surface.Foreground.Hex}  ·  Hold Shift to constrain");
        }
        else if (tool is EditorTool.Gradient or EditorTool.Fill)
        {
            panel.Children.Add(Studio.Label("Foreground → Background    Normal    100%", 11));
            Tolerance();
        }
        else
        {
            panel.Children.Add(new StudioButton("Fit screen", Surface.Fit));
            panel.Children.Add(new StudioButton("100%", () => Surface.SetZoom(1)));
            panel.Children.Add(Studio.Label("Scroll to zoom at pointer · Space to pan", 11));
        }
        return new ToolOptions(panel, updates);
    }
}
