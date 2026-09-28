namespace ImageSpace.Workbench;

/// <summary>Schema-bound inspector; selection changes rebind values instead of reconstructing controls.</summary>
internal sealed class BasicPropertiesEditor : UserControl
{
    private readonly LayerKind? _kind;
    private readonly AdjustmentKind _adjustment;
    private readonly StackPanel _body = new() { Spacing = 8 };
    private readonly List<(NumericField Field, Func<Layer, double> Read)> _fields = [];
    private readonly TextBlock _description = Studio.Label("", 11, "#a0a0a0");
    private TextBox? _color;
    private StudioButton? _bold;
    private EditorSession? _session;
    private Guid _layerId;
    private bool _updating;
    public event Action<Layer>? TextEditRequested;
    public event Action<string>? Error;
    private Layer? Current => _session?.Document.ActiveLayerId == _layerId ? _session.Document.ActiveLayer : null;

    public BasicPropertiesEditor(LayerKind? kind, AdjustmentKind adjustment)
    {
        _kind = kind;
        _adjustment = adjustment;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Content = _body;
        if (kind is null)
        {
            _body.Children.Add(Studio.Label("Document"));
            _body.Children.Add(_description);
            return;
        }
        _body.Children.Add(Studio.Row(new IconView(kind == LayerKind.Text ? "text" : kind == LayerKind.Adjustment ? "adjust" : "rectangle"),
            Studio.Label(kind == LayerKind.Raster ? "Pixel layer" : kind == LayerKind.Adjustment ? adjustment + " adjustment" : kind + " layer")));
        if (kind == LayerKind.Adjustment)
        {
            if (adjustment is AdjustmentKind.GaussianBlur or AdjustmentKind.BrightnessContrast or AdjustmentKind.Saturation)
                _body.Children.Add(Field("Amount", adjustment == AdjustmentKind.GaussianBlur ? 0 : -100,
                    adjustment == AdjustmentKind.GaussianBlur ? 32 : 200, layer => layer.Amount, (layer, value) => layer.Amount = value, 252));
            if (adjustment == AdjustmentKind.BrightnessContrast)
                _body.Children.Add(Field("Contrast", -99, 300, layer => layer.Secondary, (layer, value) => layer.Secondary = value, 252));
            _body.Children.Add(Studio.Label("Use layer opacity to adjust the effect strength.", 11, "#a8a8a8"));
            return;
        }
        _body.Children.Add(Studio.Row(Field("X", -100000, 100000, layer => layer.X, (layer, value) => layer.X = value),
            Field("Y", -100000, 100000, layer => layer.Y, (layer, value) => layer.Y = value)));
        _body.Children.Add(Studio.Row(Field("W", 1, 100000, layer => Math.Abs(layer.Width * layer.ScaleX),
            (layer, value) => layer.ScaleX = value / Math.Max(1, layer.Width) * Math.Sign(layer.ScaleX)),
            Field("H", 1, 100000, layer => Math.Abs(layer.Height * layer.ScaleY),
            (layer, value) => layer.ScaleY = value / Math.Max(1, layer.Height) * Math.Sign(layer.ScaleY))));
        _body.Children.Add(Studio.Row(Field("Angle", -3600, 3600, layer => layer.Rotation, (layer, value) => layer.Rotation = value),
            new StudioButton("Reset transform", () => Edit("Reset transform", layer => { layer.ScaleX = layer.ScaleY = 1; layer.Rotation = 0; })) { Width = 123 }));
        if (kind is not (LayerKind.Text or LayerKind.Rectangle or LayerKind.Ellipse))
            return;
        _color = Studio.TextInput("", "Layer color", 123);
        _color.LostFocus += (_, _) =>
        {
            if (_updating || Current is not { } layer)
                return;
            try
            {
                var color = Rgba32.Parse(_color.Text);
                if (layer.Color != color)
                    Edit("Layer color", current => current.Color = color);
            }
            catch (Exception error) { Error?.Invoke(error.Message); }
        };
        if (kind == LayerKind.Text)
        {
            _body.Children.Add(Studio.Row(Field("Size", 1, 4096, layer => layer.FontSize, (layer, value) => layer.FontSize = value), _color));
            _bold = new StudioButton("Bold", () => Edit("Font weight", layer => layer.Bold = !layer.Bold)) { Width = 88 };
            _body.Children.Add(Studio.Row(new StudioButton("Edit text…", () => { if (Current is { } layer) TextEditRequested?.Invoke(layer); }) { Width = 158 }, _bold));
        }
        else
        {
            _body.Children.Add(Studio.Row(_color, Field("Stroke", 0, 200, layer => layer.StrokeWidth, (layer, value) => layer.StrokeWidth = value)));
            if (kind == LayerKind.Rectangle)
                _body.Children.Add(Field("Corner radius", 0, 2048, layer => layer.CornerRadius, (layer, value) => layer.CornerRadius = value, 252));
        }
    }

    public void Bind(EditorSession session)
    {
        var changed = !ReferenceEquals(_session, session) || _layerId != session.Document.ActiveLayerId;
        _session = session;
        _layerId = session.Document.ActiveLayerId;
        _updating = true;
        try
        {
            if (Current is not { } layer)
            {
                _description.Text = $"{session.Document.Width} × {session.Document.Height} px  ·  RGB / 8";
                return;
            }
            IsEnabled = !layer.Locked;
            foreach (var (field, read) in _fields)
            {
                field.Value = read(layer);
                if (changed)
                    field.ResetPendingEdit();
            }
            if (_color is not null && (changed || _color.FocusState == FocusState.Unfocused))
                _color.Text = layer.Color.Hex;
            _bold?.SetLabel(layer.Bold ? "Bold ✓" : "Bold");
        }
        finally { _updating = false; }
    }

    private NumericField Field(string name, double min, double max, Func<Layer, double> read, Action<Layer, float> write, double width = 123)
    {
        var field = new NumericField(name, Math.Clamp(0, min, max), min, max, width);
        _fields.Add((field, read));
        field.ValueChanged += value => { if (!_updating) Edit("Change " + name, layer => write(layer, (float)value)); };
        return field;
    }

    private void Edit(string name, Action<Layer> edit)
    {
        if (_updating || _session is null || _session.IsInTransaction || Current is not { Locked: false } layer)
            return;
        try
        {
            _session.Execute(name, _ => edit(layer));
        }
        catch (Exception error) { Error?.Invoke(error.Message); Bind(_session); }
    }
}
