using ImageSpace.Skia;
using SkiaSharp;

namespace ImageSpace.Workbench;

/// <summary>Transaction-aware tonal inspector. A complete pointer gesture is one undo state.</summary>
public sealed class ToneAdjustmentEditor : UserControl
{
    private readonly EditorSession _session;
    private readonly Guid _layerId;
    private readonly ImageRenderer? _renderer;
    private readonly Action _preview;
    private readonly AdjustmentKind _kind;
    private readonly StackPanel _body = new() { Spacing = 8 };
    private readonly Dictionary<ToneChannel, StudioButton> _channels = [];
    private readonly CurveEditor _curve = new();
    private readonly LevelsControl _levels = new();
    private readonly NumericField _input = new("Curves input", 0, 0, 255, 123);
    private readonly NumericField _output = new("Curves output", 0, 0, 255, 123);
    private readonly NumericField _black = new("Input black", 0, 0, 254, 123);
    private readonly NumericField _white = new("Input white", 255, 1, 255, 123);
    private readonly NumericField _gamma = new("Gamma", 1, 0.1, 10, 252) { Step = 0.05, Format = "0.00" };
    private readonly NumericField _outBlack = new("Output black", 0, 0, 255, 123);
    private readonly NumericField _outWhite = new("Output white", 255, 0, 255, 123);
    private readonly StudioButton _delete;
    private readonly StudioButton _previewButton;
    private ToneChannel _channel;
    private bool _refreshing;
    private bool _ownsTransaction;
    private CurvesAdjustment? _beforeCurves;
    private LevelsAdjustment? _beforeLevels;
    public Guid LayerId => _layerId;
    public AdjustmentKind Kind => _kind;
    public event Action<string>? Error;
    private Layer? Current => _session.Document.Layers.FirstOrDefault(layer => layer.Id == _layerId);

    public ToneAdjustmentEditor(EditorSession session, Guid layerId, ImageRenderer? renderer, Action preview)
    {
        _session = session;
        _layerId = layerId;
        _renderer = renderer;
        _preview = preview;
        _kind = Current?.Adjustment ?? throw new ArgumentException("The adjustment layer does not exist.", nameof(layerId));
        if (_kind is not (AdjustmentKind.Curves or AdjustmentKind.Levels))
        {
            throw new ArgumentException("A Curves or Levels layer is required.", nameof(layerId));
        }
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Content = _body;
        var channels = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var channel in Enum.GetValues<ToneChannel>())
        {
            var name = channel == ToneChannel.Rgb ? "RGB" : channel.ToString();
            var button = new StudioButton($"{_kind} channel {name}", () => SelectChannel(channel))
            {
                Width = 60, Content = Studio.Label(name, 11), Padding = new Thickness(4, 2, 4, 2)
            };
            _channels[channel] = button;
            channels.Children.Add(button);
        }
        _body.Children.Add(channels);
        _delete = new StudioButton("Delete curve point", _curve.DeleteSelectedPoint) { Width = 123 };
        _previewButton = new StudioButton("Adjustment preview", () => Edit("Toggle adjustment preview", layer => layer.Visible = !layer.Visible)) { Width = 123 };
        if (_kind == AdjustmentKind.Curves)
        {
            _body.Children.Add(_curve);
            _body.Children.Add(Studio.Row(_input, _output));
            _curve.TryBeginEdit = BeginEdit;
            _curve.CurveChanged += value =>
            {
                if (_ownsTransaction && Current is { } layer)
                {
                    layer.Curves = layer.Curves.WithChannel(_channel, value);
                    _preview();
                }
            };
            _curve.EditCompleted += CompleteEdit;
            _curve.EditCanceled += CancelEdit;
            _curve.SelectedPointChanged += _ => RefreshNumbers();
            _input.ValueChanged += value => { if (!_refreshing) _curve.SetSelectedPoint((int)value, _curve.SelectedPoint.Output); };
            _output.ValueChanged += value => { if (!_refreshing) _curve.SetSelectedPoint(_curve.SelectedPoint.Input, (int)value); };
        }
        else
        {
            _body.Children.Add(_levels);
            _body.Children.Add(Studio.Row(_black, _white));
            _body.Children.Add(_gamma);
            _body.Children.Add(Studio.Row(_outBlack, _outWhite));
            _levels.TryBeginEdit = BeginEdit;
            _levels.ValueChanged += value =>
            {
                if (_ownsTransaction && Current is { } layer)
                {
                    layer.Levels = layer.Levels.WithChannel(_channel, value);
                    RefreshNumbers();
                    _preview();
                }
            };
            _levels.EditCompleted += CompleteEdit;
            _levels.EditCanceled += CancelEdit;
            _black.ValueChanged += value => ChangeLevels(levels => levels with { InputBlack = value });
            _white.ValueChanged += value => ChangeLevels(levels => levels with { InputWhite = value });
            _gamma.ValueChanged += value => ChangeLevels(levels => levels with { Gamma = value });
            _outBlack.ValueChanged += value => ChangeLevels(levels => levels with { OutputBlack = value });
            _outWhite.ValueChanged += value => ChangeLevels(levels => levels with { OutputWhite = value });
        }
        StudioButton? presets = null;
        presets = new StudioButton(_kind + " presets", () => ShowPresets(presets!))
        {
            Width = 123, Content = Studio.Label("Presets  ⌄", 11)
        };
        _body.Children.Add(Studio.Row(presets, new StudioButton("Reset tone channel", ResetChannel)
        {
            Width = 123, Content = Studio.Label("Reset channel", 11)
        }));
        _body.Children.Add(Studio.Row(_kind == AdjustmentKind.Curves ? _delete : new StudioButton("Reset all levels", () => Edit("Reset Levels", layer => layer.Levels = new())) { Width = 123 }, _previewButton));
        _body.Children.Add(Studio.Label("Sampled input histogram · non-destructive", 10, "#989898"));
        Unloaded += (_, _) => { _curve.CancelEdit(); _levels.CancelEdit(); CancelEdit(); };
        RefreshFromDocument();
    }

    private void SelectChannel(ToneChannel channel)
    {
        if (_ownsTransaction) return;
        _channel = channel;
        RefreshFromDocument();
    }

    public void RefreshFromDocument()
    {
        if (_ownsTransaction || Current is not { } layer) return;
        _refreshing = true;
        try
        {
            IsEnabled = !layer.Locked;
            foreach (var (channel, button) in _channels) button.Selected(channel == _channel);
            _curve.CurveColor = _channel switch
            {
                ToneChannel.Red => new SKColor(237, 124, 124),
                ToneChannel.Green => new SKColor(125, 216, 147),
                ToneChannel.Blue => new SKColor(125, 170, 247),
                _ => new SKColor(224, 224, 224)
            };
            _curve.Curve = layer.Curves.GetChannel(_channel);
            _levels.Value = layer.Levels.GetChannel(_channel);
            _previewButton.Content = Studio.Label(layer.Visible ? "Preview ✓" : "Preview off", 11);
            RefreshNumbers();
            if (_renderer is not null)
            {
                var document = _session.Document;
                var index = document.Layers.IndexOf(layer);
                var prefix = new ImageDocument(document.Width, document.Height)
                {
                    Layers = document.Layers.Take(index).ToList()
                };
                var pixels = _renderer.RasterizePreview(prefix, 192);
                var channel = _channel switch { ToneChannel.Red => 0, ToneChannel.Green => 1, ToneChannel.Blue => 2, _ => -1 };
                var histogram = RasterOperations.Histogram(pixels, channel);
                _curve.SetHistogram(histogram);
                _levels.SetHistogram(histogram);
            }
        }
        catch (Exception error) { Error?.Invoke(error.Message); }
        finally { _refreshing = false; }
    }

    private void RefreshNumbers()
    {
        if (Current is not { } layer) return;
        var updating = _refreshing;
        _refreshing = true;
        try
        {
            var point = _curve.SelectedPoint;
            _input.Value = point.Input;
            _output.Value = point.Output;
            _delete.IsEnabled = _curve.CanDeletePoint && !layer.Locked;
            var levels = layer.Levels.GetChannel(_channel);
            _black.Maximum = levels.InputWhite - 1;
            _white.Minimum = levels.InputBlack + 1;
            _black.Value = levels.InputBlack;
            _white.Value = levels.InputWhite;
            _gamma.Value = levels.Gamma;
            _outBlack.Value = levels.OutputBlack;
            _outWhite.Value = levels.OutputWhite;
        }
        finally { _refreshing = updating; }
    }

    private bool BeginEdit()
    {
        if (_ownsTransaction || _session.IsInTransaction || Current is not { Locked: false } layer) return false;
        _session.Begin("Edit " + _kind);
        _beforeCurves = layer.Curves;
        _beforeLevels = layer.Levels;
        _ownsTransaction = true;
        return true;
    }

    private void CompleteEdit()
    {
        if (!_ownsTransaction) return;
        _ownsTransaction = false;
        try
        {
            if (Current is { } layer && (layer.Curves != _beforeCurves || layer.Levels != _beforeLevels)) _session.Commit();
            else _session.Cancel();
        }
        catch (Exception error) { _session.Cancel(); Error?.Invoke(error.Message); }
        _preview();
    }

    private void CancelEdit()
    {
        if (!_ownsTransaction) return;
        _ownsTransaction = false;
        _session.Cancel();
        _preview();
    }

    private void Edit(string name, Action<Layer> edit)
    {
        if (_refreshing || _ownsTransaction || _session.IsInTransaction || Current is not { Locked: false } layer) return;
        try { _session.Execute(name, _ => edit(layer)); _preview(); }
        catch (Exception error) { Error?.Invoke(error.Message); RefreshFromDocument(); }
    }

    private void ChangeLevels(Func<LevelsChannel, LevelsChannel> change) => Edit("Edit Levels", layer =>
        layer.Levels = layer.Levels.WithChannel(_channel, change(layer.Levels.GetChannel(_channel))));

    private void ResetChannel() => Edit("Reset tone channel", layer =>
    {
        if (_kind == AdjustmentKind.Curves) layer.Curves = layer.Curves.WithChannel(_channel, ToneCurve.Identity);
        else layer.Levels = layer.Levels.WithChannel(_channel, new LevelsChannel());
    });

    private void ShowPresets(FrameworkElement anchor)
    {
        if (_kind == AdjustmentKind.Curves)
        {
            (string Name, ToneCurve Curve)[] presets =
            [
                ("Linear", ToneCurve.Identity),
                ("Strong contrast", new() { Points = [new(0,0), new(64,38), new(192,218), new(255,255)] }),
                ("Lift shadows", new() { Points = [new(0,0), new(64,98), new(192,211), new(255,255)] }),
                ("Fade blacks", new() { Points = [new(0,28), new(128,139), new(255,245)] }),
                ("Negative", new() { Points = [new(0,255), new(255,0)] })
            ];
            Studio.Menu(anchor, presets.Select(preset => (preset.Name, "", (Action)(() => Edit("Curves: " + preset.Name,
                layer => layer.Curves = layer.Curves.WithChannel(_channel, preset.Curve))), true)));
        }
        else
        {
            (string Name, LevelsChannel Levels)[] presets =
            [
                ("Default levels", new()),
                ("Increase contrast", new() { InputBlack = 24, InputWhite = 232 }),
                ("Lighten midtones", new() { Gamma = 1.4 }),
                ("Darken midtones", new() { Gamma = 0.7 }),
                ("Fade output", new() { OutputBlack = 24, OutputWhite = 240 })
            ];
            Studio.Menu(anchor, presets.Select(preset => (preset.Name, "", (Action)(() => Edit("Levels: " + preset.Name,
                layer => layer.Levels = layer.Levels.WithChannel(_channel, preset.Levels))), true)));
        }
    }
}
