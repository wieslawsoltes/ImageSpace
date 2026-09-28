namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private void RefreshOptions()
    {
        _options.Children.Clear();
        _options.Children.Add(Studio.Label(Surface.Tool + (Session.Document.EditMask ? " · Mask" : "")));
        _options.Children.Add(new Border { Width = 1, Height = 22, Background = Studio.Brush("#1f1f1f") });
        if (Surface.Tool is EditorTool.Brush or EditorTool.Pencil or EditorTool.Eraser or EditorTool.Clone or EditorTool.Dodge or EditorTool.Burn or EditorTool.Smudge)
        {
            AddBrushOptions();
        }
        else if (Surface.Tool == EditorTool.Move)
        {
            _options.Children.Add(Studio.Label("Auto-select: Layer", 11));
            _options.Children.Add(new StudioButton(Surface.ShowTransform ? "Transform controls ✓" : "Transform controls", () =>
            {
                Surface.ShowTransform = !Surface.ShowTransform;
                Surface.Invalidate();
                RefreshOptions();
            }));
            _options.Children.Add(new StudioButton("Fit on screen", Surface.Fit));
            _options.Children.Add(new StudioButton("100%", () => Surface.SetZoom(1)));
        }
        else if (Surface.Tool is EditorTool.Marquee or EditorTool.EllipseSelect or EditorTool.Lasso or EditorTool.Wand)
        {
            _options.Children.Add(Studio.Label("New  ·  Shift: add  ·  Alt: subtract", 11));
            AddToleranceOption();
            _options.Children.Add(new StudioButton("Deselect", Deselect));
            _options.Children.Add(new StudioButton("Invert selection", InvertSelection));
            _options.Children.Add(new StudioButton("Feather…", () => _ = FeatherAsync()));
        }
        else if (Surface.Tool == EditorTool.Crop)
        {
            _options.Children.Add(Studio.Label("Non-destructive canvas crop", 11));
            _options.Children.Add(new StudioButton("Apply crop", () => Run(Surface.ApplyCrop)));
            _options.Children.Add(new StudioButton("Cancel", Surface.CancelGesture));
        }
        else if (Surface.Tool == EditorTool.Text)
        {
            _options.Children.Add(Studio.Label("Inter    Regular / Bold    Anti-alias: smooth", 11));
            _options.Children.Add(new StudioButton("Edit selected text…", () =>
            {
                if (Session.Document.ActiveLayer is { Kind: LayerKind.Text } layer)
                {
                    _ = EditTextAsync(layer);
                }
            }));
        }
        else if (Surface.Tool is EditorTool.Rectangle or EditorTool.Ellipse)
        {
            _options.Children.Add(Studio.Label($"Shape  ·  Fill: {Surface.Foreground.Hex}  ·  Hold Shift to constrain", 11));
        }
        else if (Surface.Tool is EditorTool.Gradient or EditorTool.Fill)
        {
            _options.Children.Add(Studio.Label("Foreground → Background    Normal    100%", 11));
            AddToleranceOption();
        }
        else
        {
            _options.Children.Add(new StudioButton("Fit screen", Surface.Fit));
            _options.Children.Add(new StudioButton("100%", () => Surface.SetZoom(1)));
            _options.Children.Add(Studio.Label("Scroll to zoom at pointer · Space to pan", 11));
        }
    }

    private void AddToleranceOption()
    {
        var tolerance = new NumericField("Tolerance", Surface.Tolerance, 0, 255, 120);
        tolerance.ValueChanged += value => Surface.Tolerance = (int)value;
        _options.Children.Add(tolerance);
    }

    private void AddBrushOptions()
    {
        var size = new NumericField("Size", Surface.Brush.Size, 1, 1024, 93);
        size.ValueChanged += value => Surface.Brush = Surface.Brush with { Size = (float)value };
        var hardness = new NumericField("Hardness", Surface.Brush.Hardness * 100, 0, 100, 116);
        hardness.ValueChanged += value => Surface.Brush = Surface.Brush with { Hardness = (float)value / 100 };
        var opacity = new NumericField("Opacity", Surface.Brush.Opacity * 100, 1, 100, 108);
        opacity.ValueChanged += value => Surface.Brush = Surface.Brush with { Opacity = (float)value / 100 };
        var flow = new NumericField("Flow", Surface.Brush.Flow * 100, 1, 100, 94);
        flow.ValueChanged += value => Surface.Brush = Surface.Brush with { Flow = (float)value / 100 };
        _options.Children.Add(size);
        _options.Children.Add(hardness);
        _options.Children.Add(opacity);
        _options.Children.Add(flow);
        _options.Children.Add(new StudioButton(Surface.Brush.Pressure ? "Pressure ✓" : "Pressure", () =>
        {
            Surface.Brush = Surface.Brush with { Pressure = !Surface.Brush.Pressure };
            RefreshOptions();
        }));
        _options.Children.Add(new StudioButton("New paint layer", () => Run(() => Session.AddLayer("Paint")), "plus"));
    }
}
