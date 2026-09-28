using ImageSpace.Skia;

namespace ImageSpace.Workbench;

public sealed class MaskPropertiesEditor : UserControl
{
    private readonly EditorSession _session;
    private readonly NumericField _density;
    private readonly NumericField _feather;
    private readonly StudioButton _invert;
    private readonly StudioButton _toggle;
    private readonly StudioButton _delete;
    private readonly TextBlock _state;
    private readonly Dictionary<MaskPreviewMode, StudioButton> _previews = [];
    private bool _refreshing;
    public Guid LayerId { get; }
    public event Action<string>? Error;
    public event Action<MaskPreviewMode>? PreviewChanged;

    public MaskPropertiesEditor(EditorSession session, Guid layerId)
    {
        _session = session;
        LayerId = layerId;
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        var body = new StackPanel { Spacing = 9 };
        body.Children.Add(Studio.Row(new IconView("mask"), Studio.Label("Layer mask")));
        _state = Studio.Label("", 11, "#a9a9a9");
        body.Children.Add(_state);
        _density = new NumericField("Mask density (%)", 100, 0, 100, double.NaN);
        _feather = new NumericField("Mask feather (px)", 0, 0, 32, double.NaN) { Step = .5 };
        _density.ValueChanged += value => Change(() =>
        {
            if (Current is { } layer) _session.SetMaskProperties((float)value / 100, layer.MaskFeather);
        });
        _feather.ValueChanged += value => Change(() =>
        {
            if (Current is { } layer) _session.SetMaskProperties(layer.MaskDensity, (float)value);
        });
        body.Children.Add(_density);
        body.Children.Add(_feather);
        var previews = new Grid { ColumnSpacing = 4 };
        foreach (var mode in Enum.GetValues<MaskPreviewMode>())
        {
            previews.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var button = new StudioButton("Mask view " + mode, () => SelectPreview(mode))
            {
                Content = Studio.Label(mode == MaskPreviewMode.Grayscale ? "Mask" : mode.ToString(), 11),
                Padding = new Thickness(3), HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetColumn(button, _previews.Count);
            previews.Children.Add(button);
            _previews[mode] = button;
        }
        body.Children.Add(previews);
        _invert = new StudioButton("Invert mask", () => Change(_session.InvertMask));
        _toggle = new StudioButton("Toggle mask", () => Change(() =>
        {
            if (Current is { } layer) _session.SetMaskEnabled(!layer.MaskEnabled);
        }));
        _delete = new StudioButton("Delete mask", () => Change(_session.DeleteMask), "trash") { Width = 29 };
        body.Children.Add(Studio.Row(_invert, _toggle, _delete));
        body.Children.Add(new StudioButton("Back to layer content", () =>
        {
            if (_session.IsInTransaction) return;
            SelectPreview(MaskPreviewMode.Composite);
            _session.SelectLayer(LayerId);
        }));
        var description = Studio.Label("Black hides. White reveals. Density and feather preserve the authored mask.", 11, "#9c9c9c");
        description.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(description);
        Content = body;
        _previews[MaskPreviewMode.Composite].Selected(true);
        RefreshFromDocument();
    }

    private Layer? Current => _session.Document.ActiveLayerId == LayerId
        ? _session.Document.ActiveLayer : null;

    private void SelectPreview(MaskPreviewMode mode)
    {
        foreach (var (key, button) in _previews) button.Selected(key == mode);
        PreviewChanged?.Invoke(mode);
    }

    public void RefreshFromDocument()
    {
        _refreshing = true;
        try
        {
            var layer = Current;
            var editable = layer is { Locked: false, Mask: not null };
            _density.IsEnabled = _feather.IsEnabled = _invert.IsEnabled = _toggle.IsEnabled = _delete.IsEnabled = editable;
            if (layer is null) return;
            _density.Value = layer.MaskDensity * 100;
            _feather.Value = layer.MaskFeather;
            _state.Text = layer.Locked ? "Locked · inspection only" : layer.MaskEnabled ? "Enabled · editing authored coverage" : "Disabled · mask edits are retained";
            _toggle.Content = Studio.Label(layer.MaskEnabled ? "Disable" : "Enable");
        }
        finally { _refreshing = false; }
    }

    private void Change(Action action)
    {
        if (_refreshing || _session.IsInTransaction || Current is not { Locked: false, Mask: not null }) return;
        try { action(); }
        catch (Exception error) { Error?.Invoke(error.Message); RefreshFromDocument(); }
    }
}
