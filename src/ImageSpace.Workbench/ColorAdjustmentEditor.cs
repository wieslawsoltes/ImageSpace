namespace ImageSpace.Workbench;

/// <summary>Retained Channel Mixer/Exposure inspector. No histogram/readback on parameter or target changes.</summary>
public sealed class ColorAdjustmentEditor : UserControl, IDisposable
{
    private readonly AdjustmentKind _kind;
    private readonly Action _preview;
    private readonly StackPanel _body = new() { Spacing = 6 };
    private readonly List<AdjustmentParameter> _parameters = [];
    private readonly Dictionary<ToneChannel, StudioButton> _channels = [];
    private readonly Dictionary<Guid, ToneChannel> _lastChannels = [];
    private readonly TextBlock _total = Studio.Label("", 11, "#b5b5b5");
    private readonly StudioButton _previewButton;
    private readonly CheckBox _monochrome = new() { Content = "Monochrome", FontSize = 12, MinHeight = 28 };
    private EditorSession? _session;
    private Guid _layerId;
    private ToneChannel _channel = ToneChannel.Red;
    private EditorSession.ColorAdjustmentGesture? _gesture;
    private bool _refreshing, _disposed;
    public event Action<string>? Error;
    private Layer? Current => _session?.Document is { EditMask: false } document && document.ActiveLayerId == _layerId &&
        document.ActiveLayer is { Kind: LayerKind.Adjustment } layer && layer.Adjustment == _kind ? layer : null;

    public ColorAdjustmentEditor(AdjustmentKind kind, Action preview)
    {
        if (kind is not (AdjustmentKind.ChannelMixer or AdjustmentKind.Exposure))
            throw new ArgumentOutOfRangeException(nameof(kind));
        _kind = kind;
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Content = _body;
        _body.Children.Add(Studio.Label(kind == AdjustmentKind.ChannelMixer ? "CHANNEL MIXER" : "EXPOSURE", 11, "#b5b5b5"));
        if (kind == AdjustmentKind.ChannelMixer)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (var channel in new[] { ToneChannel.Red, ToneChannel.Green, ToneChannel.Blue })
            {
                var selected = channel;
                var button = new StudioButton("Mixer output " + channel, () => SelectChannel(selected))
                {
                    Content = Studio.Label(channel.ToString(), 11),
                    Width = 77,
                    Padding = new Thickness(4, 2, 4, 2)
                };
                _channels[channel] = button;
                row.Children.Add(button);
            }
            _body.Children.Add(row);
            _body.Children.Add(_monochrome);
            _monochrome.Click += (_, _) =>
            {
                if (_refreshing || Current is not { } layer)
                    return;
                var enabled = _monochrome.IsChecked == true;
                EditMixer(layer.ChannelMixer with
                {
                    Monochrome = enabled
                });
            };
            AddParameter("Mixer red", -200, 200, 1, "0.##");
            AddParameter("Mixer green", -200, 200, 1, "0.##");
            AddParameter("Mixer blue", -200, 200, 1, "0.##");
            AddParameter("Mixer constant", -200, 200, 1, "0.##");
            _body.Children.Add(_total);
        }
        else
        {
            AddParameter("Exposure EV", -20, 20, .01, "0.00");
            AddParameter("Exposure offset", -.5, .5, .0001, "0.0000");
            AddParameter("Exposure gamma", .01, 9.99, .01, "0.00");
            var description = Studio.Label("Linear-light sRGB · RGB/8 output", 10, "#989898");
            description.TextWrapping = TextWrapping.Wrap;
            _body.Children.Add(description);
        }
        StudioButton? presets = null;
        presets = new StudioButton(kind + " presets", () => ShowPresets(presets!))
        {
            Width = 123,
            Content = Studio.Label("Presets  ⌄", 11)
        };
        var reset = new StudioButton("Reset " + kind, Reset) { Width = 123, Content = Studio.Label("Reset", 11) };
        _body.Children.Add(Studio.Row(presets, reset));
        _previewButton = new StudioButton("Color adjustment preview", TogglePreview)
        {
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _body.Children.Add(_previewButton);
        Unloaded += (_, _) => CancelGesture();
    }

    private void AddParameter(string name, double min, double max, double step, string format)
    {
        var index = _parameters.Count;
        var parameter = new AdjustmentParameter(name, min, max, step, format) { TryBeginEdit = BeginGesture };
        parameter.ValueChanged += value => Change(index, value);
        parameter.EditCompleted += CompleteGesture;
        parameter.EditCanceled += CancelGesture;
        _parameters.Add(parameter);
        _body.Children.Add(parameter);
    }

    public void Bind(EditorSession session, Guid layerId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var switched = !ReferenceEquals(_session, session) || _layerId != layerId;
        if (switched)
        {
            CancelGesture();
            if (!ReferenceEquals(_session, session))
                _lastChannels.Clear();
            else if (_layerId != Guid.Empty)
                _lastChannels[_layerId] = _channel;
            _session = session;
            _layerId = layerId;
            _channel = _lastChannels.GetValueOrDefault(layerId, ToneChannel.Red);
            // Bound view-only channel state by the document's actual layer identities.
            foreach (var id in _lastChannels.Keys.Where(id => !session.Document.Layers.Any(layer => layer.Id == id)).ToArray())
                _lastChannels.Remove(id);
        }
        if (_gesture is not null && !_gesture.IsActive)
            CancelGesture();
        Refresh();
        if (switched)
        foreach (var parameter in _parameters)
            parameter.ResetPendingEdit();
    }

    private void Refresh()
    {
        if (_gesture is not null || Current is not { } layer)
            return;
        _refreshing = true;
        try
        {
            IsEnabled = !layer.Locked;
            if (_kind == AdjustmentKind.ChannelMixer)
            {
                _monochrome.IsChecked = layer.ChannelMixer.Monochrome;
                foreach (var (channel, button) in _channels)
                {
                    button.Selected(channel == _channel && !layer.ChannelMixer.Monochrome);
                    button.IsEnabled = !layer.Locked && !layer.ChannelMixer.Monochrome;
                }
                var mix = layer.ChannelMixer.GetChannel(EffectiveChannel(layer));
                _parameters[0].Value = mix.Red;
                _parameters[1].Value = mix.Green;
                _parameters[2].Value = mix.Blue;
                _parameters[3].Value = mix.Constant;
                ShowTotal(mix);
            }
            else
            {
                _parameters[0].Value = layer.Exposure.Exposure;
                _parameters[1].Value = layer.Exposure.Offset;
                _parameters[2].Value = layer.Exposure.Gamma;
            }
            _previewButton.SetLabel(layer.Visible ? "Preview ✓" : "Preview off");
            _previewButton.SetName("Color adjustment preview");
        }
        finally { _refreshing = false; }
    }
    private ToneChannel EffectiveChannel(Layer layer) => layer.ChannelMixer.Monochrome ? ToneChannel.Rgb : _channel;
    private void ShowTotal(ChannelMix mix)
    {
        var text = $"Total: {mix.Total:0.##}%" + (Math.Abs(mix.Total - 100) > .0001 ? "  ·  brightness may change" : "");
        if (_total.Text != text)
            _total.Text = text;
    }
    private void SelectChannel(ToneChannel channel)
    {
        if (_gesture is not null || Current?.ChannelMixer.Monochrome != false)
            return;
        _channel = channel;
        Refresh();
        foreach (var parameter in _parameters)
            parameter.ResetPendingEdit();
    }
    private bool BeginGesture()
    {
        if (_disposed || _refreshing || _gesture is not null || _session is null || _session.IsInTransaction || Current is not { Locked: false })
            return false;
        try
        {
            _gesture = _session.BeginColorAdjustmentEdit();
            return true;
        }
        catch (Exception error) { Error?.Invoke(error.Message); return false; }
    }
    private void Change(int index, double value)
    {
        if (_gesture?.IsActive != true || Current is not { } layer)
            return;
        try
        {
            if (_kind == AdjustmentKind.ChannelMixer)
            {
                var channel = EffectiveChannel(layer);
                var mix = layer.ChannelMixer.GetChannel(channel);
                mix = index switch
                {
                    0 => mix with { Red = value },
                    1 => mix with { Green = value },
                    2 => mix with { Blue = value },
                    _ => mix with { Constant = value }
                };
                _gesture.Set(layer.ChannelMixer.WithChannel(channel, mix));
                ShowTotal(mix);
            }
            else
            {
                var settings = layer.Exposure;
                _gesture.Set(index switch
                {
                    0 => settings with { Exposure = value },
                    1 => settings with { Offset = value },
                    _ => settings with { Gamma = value }
                });
            }
            _preview();
        }
        catch (Exception error) { CancelGesture(); Error?.Invoke(error.Message); }
    }
    private void CompleteGesture()
    {
        var gesture = _gesture;
        _gesture = null;
        if (gesture is null)
            return;
        try
        {
            gesture.Commit();
        }
        catch (Exception error) { Error?.Invoke(error.Message); }
        finally { gesture.Dispose(); }
        Refresh();
        _preview();
    }
    private void CancelGesture()
    {
        var gesture = _gesture;
        _gesture = null;
        foreach (var parameter in _parameters)
            parameter.CancelEdit();
        if (gesture is null)
            return;
        gesture.Dispose();
        Refresh();
        _preview();
    }
    private void EditMixer(ChannelMixerAdjustment settings)
    {
        if (_session is null || _gesture is not null || _session.IsInTransaction || Current is not { Locked: false })
            return;
        try
        {
            _session.SetChannelMixer(settings);
        }
        catch (Exception error) { Error?.Invoke(error.Message); }
        Refresh();
        _preview();
    }
    private void EditExposure(ExposureAdjustment settings)
    {
        if (_session is null || _gesture is not null || _session.IsInTransaction || Current is not { Locked: false })
            return;
        try
        {
            _session.SetExposure(settings);
        }
        catch (Exception error) { Error?.Invoke(error.Message); }
        Refresh();
        _preview();
    }
    private void TogglePreview()
    {
        if (_session is null || _gesture is not null || _session.IsInTransaction || Current is not { Locked: false } layer)
            return;
        _session.Execute("Toggle adjustment preview", _ => layer.Visible = !layer.Visible);
        _preview();
    }
    private void Reset()
    {
        if (_kind == AdjustmentKind.ChannelMixer)
            EditMixer(new());
        else
            EditExposure(new());
    }
    private void ShowPresets(FrameworkElement anchor)
    {
        if (_kind == AdjustmentKind.ChannelMixer)
        {
            (string Name, ChannelMixerAdjustment Settings)[] presets =
            [
                ("Mixer identity", new()),
                ("Swap red and blue", new() { Red = new(0, 0, 100), Blue = new(100, 0, 0) }),
                ("Monochrome red filter", new() { Monochrome = true, Gray = new(100, 0, 0) }),
                ("Monochrome green filter", new() { Monochrome = true, Gray = new(0, 100, 0) }),
                ("Monochrome blue filter", new() { Monochrome = true, Gray = new(0, 0, 100) }),
                ("Monochrome yellow filter", new() { Monochrome = true, Gray = new(34, 66, 0) }),
                ("Monochrome infrared", new() { Monochrome = true, Gray = new(-70, 200, -30) })
            ];
            Studio.Menu(anchor, presets.Select(preset => (preset.Name, "", (Action)(() => EditMixer(preset.Settings)), true)));
        }
        else
        {
            (string Name, ExposureAdjustment Settings)[] presets =
            [
                ("Exposure default", new()), ("Exposure plus one stop", new() { Exposure = 1 }),
                ("Exposure minus one stop", new() { Exposure = -1 }),
                ("Exposure lift shadows", new() { Offset = .02 }), ("Exposure lift midtones", new() { Gamma = 1.2 })
            ];
            Studio.Menu(anchor, presets.Select(preset => (preset.Name, "", (Action)(() => EditExposure(preset.Settings)), true)));
        }
    }
    public new void Dispose()
    {
        if (_disposed)
            return;
        CancelGesture();
        _disposed = true;
        _lastChannels.Clear();
        _session = null;
    }
}
