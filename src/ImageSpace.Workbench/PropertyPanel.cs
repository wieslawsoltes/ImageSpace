using ImageSpace.Skia;

namespace ImageSpace.Workbench;

public sealed class PropertyPanel : UserControl
{
    private EditorSession? _session;
    private bool _refreshing;
    private ToneAdjustmentEditor? _toneEditor;
    private readonly StackPanel _body = new() { Spacing = 8, Margin = new Thickness(10, 9, 10, 10) };
    public ImageRenderer? Renderer { get; set; }
    public event Action<Layer>? TextEditRequested;
    public event Action<string>? Error;
    public event Action? PreviewInvalidated;
    public event Action<double>? PreferredHeightChanged;

    public PropertyPanel()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Content = new ScrollViewer
        {
            Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    public void Bind(EditorSession session)
    {
        if (!ReferenceEquals(_session, session)) _toneEditor = null;
        _session = session;
        Refresh();
    }

    public void Refresh()
    {
        if (_session is null) return;
        var layer = _session.Document.ActiveLayer;
        var tone = layer is { Kind: LayerKind.Adjustment, Adjustment: AdjustmentKind.Curves or AdjustmentKind.Levels };
        PreferredHeightChanged?.Invoke(tone ? 414 : 224);
        if (tone && _toneEditor is not null && _toneEditor.LayerId == layer!.Id && _toneEditor.Kind == layer.Adjustment)
        {
            // Preserve the graph instance, keyboard focus, selected channel and scroll position on commit/undo.
            _toneEditor.RefreshFromDocument();
            return;
        }
        _refreshing = true;
        try
        {
            _toneEditor = null;
            _body.Children.Clear();
            if (layer is null)
            {
                _body.Children.Add(Studio.Label("Document"));
                _body.Children.Add(Studio.Label($"{_session.Document.Width} × {_session.Document.Height} px  ·  RGB / 8", 11, "#a0a0a0"));
                return;
            }
            _body.Children.Add(Studio.Row(
                new IconView(layer.Kind == LayerKind.Text ? "text" : layer.Kind == LayerKind.Adjustment ? "adjust" : "rectangle"),
                Studio.Label(layer.Kind == LayerKind.Raster ? "Pixel layer" : layer.Kind == LayerKind.Adjustment ? layer.Adjustment + " adjustment" : layer.Kind + " layer")));
            if (tone)
            {
                _toneEditor = new ToneAdjustmentEditor(_session, layer.Id, Renderer, () => PreviewInvalidated?.Invoke());
                _toneEditor.Error += message => Error?.Invoke(message);
                _body.Children.Add(_toneEditor);
                return;
            }
            if (layer.Kind == LayerKind.Adjustment)
            {
                if (layer.Adjustment is AdjustmentKind.GaussianBlur or AdjustmentKind.BrightnessContrast or AdjustmentKind.Saturation)
                {
                    _body.Children.Add(Field("Amount", layer.Amount, layer.Adjustment == AdjustmentKind.GaussianBlur ? 0 : -100,
                        layer.Adjustment == AdjustmentKind.GaussianBlur ? 32 : 200, (item, value) => item.Amount = value, 252));
                }
                if (layer.Adjustment == AdjustmentKind.BrightnessContrast)
                    _body.Children.Add(Field("Contrast", layer.Secondary, -99, 300, (item, value) => item.Secondary = value, 252));
                _body.Children.Add(Studio.Label("Use layer opacity to adjust the effect strength.", 11, "#a8a8a8"));
                return;
            }
            _body.Children.Add(Studio.Row(Field("X", layer.X, -100000, 100000, (item, value) => item.X = value, 123),
                Field("Y", layer.Y, -100000, 100000, (item, value) => item.Y = value, 123)));
            _body.Children.Add(Studio.Row(Field("W", Math.Abs(layer.Width * layer.ScaleX), 1, 100000,
                (item, value) => item.ScaleX = value / Math.Max(1, item.Width) * Math.Sign(item.ScaleX), 123),
                Field("H", Math.Abs(layer.Height * layer.ScaleY), 1, 100000,
                (item, value) => item.ScaleY = value / Math.Max(1, item.Height) * Math.Sign(item.ScaleY), 123)));
            _body.Children.Add(Studio.Row(Field("Angle", layer.Rotation, -3600, 3600, (item, value) => item.Rotation = value, 123),
                new StudioButton("Reset transform", () => Edit("Reset transform", item =>
                {
                    item.ScaleX = item.ScaleY = 1;
                    item.Rotation = 0;
                })) { Width = 123 }));
            if (layer.Kind is not (LayerKind.Text or LayerKind.Rectangle or LayerKind.Ellipse)) return;
            var color = Studio.TextInput(layer.Color.Hex, "Layer color", 123);
            color.LostFocus += (_, _) =>
            {
                if (_refreshing || _session.Document.ActiveLayer?.Id != layer.Id) return;
                try
                {
                    var value = Rgba32.Parse(color.Text);
                    if (value != layer.Color) Edit("Layer color", item => item.Color = value);
                }
                catch (Exception error) { Error?.Invoke(error.Message); }
            };
            if (layer.Kind == LayerKind.Text)
            {
                _body.Children.Add(Studio.Row(Field("Size", layer.FontSize, 1, 4096, (item, value) => item.FontSize = value, 123), color));
                _body.Children.Add(Studio.Row(new StudioButton("Edit text…", () => TextEditRequested?.Invoke(layer)) { Width = 158 },
                    new StudioButton(layer.Bold ? "Bold ✓" : "Bold", () => Edit("Font weight", item => item.Bold = !item.Bold)) { Width = 88 }));
            }
            else
            {
                _body.Children.Add(Studio.Row(color, Field("Stroke", layer.StrokeWidth, 0, 200, (item, value) => item.StrokeWidth = value, 123)));
                if (layer.Kind == LayerKind.Rectangle)
                    _body.Children.Add(Field("Corner radius", layer.CornerRadius, 0, 2048, (item, value) => item.CornerRadius = value, 252));
            }
        }
        finally { _refreshing = false; }
    }

    private NumericField Field(string label, double value, double minimum, double maximum, Action<Layer, float> set, double width)
    {
        var field = new NumericField(label, value, minimum, maximum, width) { IsEnabled = _session?.Document.ActiveLayer?.Locked != true };
        field.ValueChanged += number => { if (!_refreshing) Edit("Change " + label, layer => set(layer, (float)number)); };
        return field;
    }

    private void Edit(string name, Action<Layer> action)
    {
        if (_session?.Document.ActiveLayer is not { Locked: false } layer || _session.IsInTransaction) return;
        try { _session.Execute(name, _ => action(layer)); }
        catch (Exception error) { Error?.Invoke(error.Message); }
    }
}
