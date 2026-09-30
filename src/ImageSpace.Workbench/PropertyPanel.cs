using ImageSpace.Skia;

namespace ImageSpace.Workbench;

/// <summary>Retains one inspector per schema, independent of layer count and undo snapshot identity.</summary>
public sealed class PropertyPanel : UserControl, IDisposable
{
    private EditorSession? _session;
    private readonly Dictionary<(LayerKind?, AdjustmentKind), BasicPropertiesEditor> _basic = [];
    private ToneAdjustmentEditor? _curves, _levels;
    private ColorAdjustmentEditor? _mixer, _exposure;
    private MaskPropertiesEditor? _mask;
    private UIElement? _current;
    private double _height;
    private readonly StackPanel _body = new() { Spacing = 8, Margin = new Thickness(10, 9, 10, 10) };
    public long InspectorBuilds
    {
        get; private set;
    }
    public long HistogramBuilds => (_curves?.HistogramBuilds ?? 0) + (_levels?.HistogramBuilds ?? 0);
    public ImageRenderer? Renderer
    {
        get; set;
    }
    public event Action<MaskPreviewMode>? MaskPreviewChanged;
    public event Action<Layer>? TextEditRequested;
    public event Action<string>? Error;
    public event Action? PreviewInvalidated;
    public event Action<double>? PreferredHeightChanged;

    public PropertyPanel()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Content = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    public void Bind(EditorSession session)
    {
        if (!ReferenceEquals(_session, session))
        {
            _curves?.ResetSession(session);
            _levels?.ResetSession(session);
            _mask?.Bind(session, session.Document.ActiveLayerId);
            foreach (var editor in _basic.Values)
                editor.Bind(session);
        }
        _session = session;
        Refresh();
    }

    public void Refresh()
    {
        if (_session is null)
            return;
        var layer = _session.Document.ActiveLayer;
        var maskEditing = _session.Document.EditMask && layer?.Mask is not null;
        var tone = !maskEditing && layer is { Kind: LayerKind.Adjustment, Adjustment: AdjustmentKind.Curves or AdjustmentKind.Levels };
        var color = !maskEditing && layer is { Kind: LayerKind.Adjustment, Adjustment: AdjustmentKind.ChannelMixer or AdjustmentKind.Exposure };
        var height = maskEditing ? (layer!.MaskLinked ? 352 : 452) : tone ? (layer!.Adjustment == AdjustmentKind.Levels ? 470 : 414)
            : color ? (layer!.Adjustment == AdjustmentKind.ChannelMixer ? 390 : 292) : 224;
        if (_height != height)
        {
            _height = height;
            PreferredHeightChanged?.Invoke(height);
        }
        UIElement next;
        if (maskEditing)
        {
            if (_mask is null)
            {
                _mask = new MaskPropertiesEditor(_session, layer!.Id);
                _mask.Error += message => Error?.Invoke(message);
                _mask.PreviewChanged += mode => MaskPreviewChanged?.Invoke(mode);
                InspectorBuilds++;
            }
            _mask.Bind(_session, layer!.Id);
            next = _mask;
        }
        else if (tone)
        {
            var editor = layer!.Adjustment == AdjustmentKind.Curves ? _curves : _levels;
            if (editor is null)
            {
                editor = new ToneAdjustmentEditor(_session, layer.Id, Renderer, () => PreviewInvalidated?.Invoke());
                editor.Error += message => Error?.Invoke(message);
                if (layer.Adjustment == AdjustmentKind.Curves)
                    _curves = editor;
                else
                    _levels = editor;
                InspectorBuilds++;
            }
            editor.Bind(_session, layer.Id, Renderer);
            next = editor;
        }
        else if (color)
        {
            var editor = layer!.Adjustment == AdjustmentKind.ChannelMixer ? _mixer : _exposure;
            if (editor is null)
            {
                editor = new ColorAdjustmentEditor(layer.Adjustment, () => PreviewInvalidated?.Invoke());
                editor.Error += message => Error?.Invoke(message);
                if (layer.Adjustment == AdjustmentKind.ChannelMixer)
                    _mixer = editor;
                else
                    _exposure = editor;
                InspectorBuilds++;
            }
            editor.Bind(_session, layer.Id);
            next = editor;
        }
        else
        {
            var key = (layer?.Kind, layer?.Kind == LayerKind.Adjustment ? layer.Adjustment : AdjustmentKind.None);
            if (!_basic.TryGetValue(key, out var editor))
            {
                editor = new BasicPropertiesEditor(key.Item1, key.Item2);
                editor.Error += message => Error?.Invoke(message);
                editor.TextEditRequested += value => TextEditRequested?.Invoke(value);
                _basic.Add(key, editor);
                InspectorBuilds++;
            }
            editor.Bind(_session);
            next = editor;
        }
        if (!ReferenceEquals(_current, next))
        {
            _body.Children.Clear();
            _body.Children.Add(next);
            _current = next;
        }
    }

    public new void Dispose()
    {
        _curves?.Dispose();
        _levels?.Dispose();
        _mixer?.Dispose();
        _exposure?.Dispose();
        _body.Children.Clear();
        _basic.Clear();
        _current = null;
        _mask = null;
        _session = null;
    }
}
