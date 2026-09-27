namespace ImageSpace.Workbench;

public sealed class PropertyPanel : UserControl
{
    private EditorSession? _session; private bool _refreshing; private readonly StackPanel _body = new() { Spacing = 8, Margin = new Thickness(10, 9, 10, 10) };
    public event Action<Layer>? TextEditRequested; public event Action<string>? Error;
    public PropertyPanel()
    {
        Content = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    public void Bind(EditorSession session)
    {
        _session = session;
        Refresh();
    }
    public void Refresh()
    {
        if (_session is null)
            return;
        _refreshing = true;
        try
        {
            _body.Children.Clear();
            var layer = _session.Document.ActiveLayer;
            if (layer is null)
            {
                _body.Children.Add(Studio.Label("Document", 12));
                _body.Children.Add(Studio.Label($"{_session.Document.Width} × {_session.Document.Height} px  ·  RGB / 8", 11, "#a0a0a0"));
                return;
            }
            _body.Children.Add(Studio.Row(new IconView(layer.Kind == LayerKind.Text ? "text" : layer.Kind == LayerKind.Adjustment ? "adjust" : "rectangle"), Studio.Label(layer.Kind == LayerKind.Raster ? "Pixel layer" : layer.Kind + " layer", 12)));
            if (layer.Kind == LayerKind.Adjustment)
            {
                _body.Children.Add(Studio.Label(layer.Adjustment.ToString(), 11, "#b5b5b5"));
                _body.Children.Add(Field("Amount", layer.Amount, layer.Adjustment == AdjustmentKind.GaussianBlur ? 0 : -100, layer.Adjustment == AdjustmentKind.GaussianBlur ? 32 : 200, (l, v) => l.Amount = v, 252));
                if (layer.Adjustment == AdjustmentKind.BrightnessContrast)
                    _body.Children.Add(Field("Contrast", layer.Secondary, -99, 300, (l, v) => l.Secondary = v, 252));
                return;
            }
            _body.Children.Add(Studio.Row(Field("X", layer.X, -100000, 100000, (l, v) => l.X = v, 123), Field("Y", layer.Y, -100000, 100000, (l, v) => l.Y = v, 123)));
            _body.Children.Add(Studio.Row(Field("W", Math.Abs(layer.Width * layer.ScaleX), 1, 100000, (l, v) => l.ScaleX = v / Math.Max(1, l.Width) * Math.Sign(l.ScaleX), 123), Field("H", Math.Abs(layer.Height * layer.ScaleY), 1, 100000, (l, v) => l.ScaleY = v / Math.Max(1, l.Height) * Math.Sign(l.ScaleY), 123)));
            _body.Children.Add(Studio.Row(Field("Angle", layer.Rotation, -3600, 3600, (l, v) => l.Rotation = v, 123), new StudioButton("Reset transform", () => Edit("Reset transform", l => { l.ScaleX = l.ScaleY = 1; l.Rotation = 0; })) { Width = 123 }));
            if (layer.Kind is LayerKind.Text or LayerKind.Rectangle or LayerKind.Ellipse)
            {
                var color = Studio.TextInput(layer.Color.Hex, "Layer color", 123);
                color.LostFocus += (_, _) => { if (_refreshing || _session?.Document.ActiveLayer?.Id != layer.Id) return; try { var c = Rgba32.Parse(color.Text); if (c != layer.Color) Edit("Layer color", l => l.Color = c); } catch (Exception ex) { Error?.Invoke(ex.Message); } };
                if (layer.Kind == LayerKind.Text)
                {
                    _body.Children.Add(Studio.Row(Field("Size", layer.FontSize, 1, 4096, (l, v) => l.FontSize = v, 123), color));
                    _body.Children.Add(Studio.Row(new StudioButton("Edit text…", () => TextEditRequested?.Invoke(layer)) { Width = 158 }, new StudioButton(layer.Bold ? "Bold ✓" : "Bold", () => Edit("Font weight", l => l.Bold = !l.Bold)) { Width = 88 }));
                }
                else
                {
                    _body.Children.Add(Studio.Row(color, Field("Stroke", layer.StrokeWidth, 0, 200, (l, v) => l.StrokeWidth = v, 123)));
                    if (layer.Kind == LayerKind.Rectangle)
                        _body.Children.Add(Field("Corner radius", layer.CornerRadius, 0, 2048, (l, v) => l.CornerRadius = v, 252));
                }
            }
        }
        finally { _refreshing = false; }
    }
    private NumericField Field(string label, double value, double min, double max, Action<Layer, float> set, double width)
    {
        var field = new NumericField(label, value, min, max, width);
        field.ValueChanged += v => { if (!_refreshing) Edit("Change " + label, l => set(l, (float)v)); };
        return field;
    }
    private void Edit(string name, Action<Layer> action)
    {
        if (_session?.Document.ActiveLayer is not { } layer || layer.Locked)
            return;
        try
        {
            _session.Execute(name, _ => action(layer));
        }
        catch (Exception ex) { Error?.Invoke(ex.Message); }
    }
}
