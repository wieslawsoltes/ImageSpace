using ImageSpace.Skia;

namespace ImageSpace.Workbench;

public sealed class LayerPanel : UserControl
{
    private EditorSession? _session;
    private ImageRenderer? _renderer;
    private readonly Dictionary<Guid, LayerRow> _items = [];
    private readonly StudioButton _blend, _lock, _mode;
    private readonly NumericField _opacity;
    private bool _updating;
    public long RowsCreated { get; private set; }
    private readonly StackPanel _rows = new() { Spacing = 1 };
    private readonly StackPanel _settings = new() { Spacing = 5, Margin = new Thickness(8, 6, 8, 7) };
    private readonly ScrollViewer _scroll;
    private readonly StudioButton _addMask;
    private readonly StudioButton _deleteLayer;
    private Guid _lastActive;
    private bool _revealSelection;
    private bool _followActiveSelection;
    public event Action? AddMaskRequested;
    public event Action? AddAdjustmentRequested;
    public event Action? RenameRequested;
    public event Action<string>? Error;

    public LayerPanel()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        var grid = new Grid
        {
            RowDefinitions =
            {
                new() { Height = GridLength.Auto },
                new() { Height = new GridLength(1, GridUnitType.Star) },
                new() { Height = GridLength.Auto }
            }
        };
        _blend = new StudioButton("Normal", ShowBlendMenu) { Width = 148, HorizontalContentAlignment = HorizontalAlignment.Left };
        _opacity = new NumericField("Opacity", 100, 0, 100, 118) { Format = "0" };
        _opacity.ValueChanged += value =>
        {
            if (!_updating) Run(editor => editor.Execute("Layer opacity", document =>
            { if (document.ActiveLayer is { Locked: false } layer) layer.Opacity = (float)value / 100; }));
        };
        _lock = new StudioButton("Lock layer", () => Run(editor => editor.Execute("Layer lock", document =>
        { if (document.ActiveLayer is { } layer) layer.Locked = !layer.Locked; })), "lock") { Width = 26, Height = 22 };
        _mode = new StudioButton("Editing pixels", () =>
        {
            if (_session?.Document.ActiveLayer is not { Mask: not null } layer) return;
            if (_session.Document.EditMask) _session.SelectLayer(layer.Id); else _session.SelectMask(layer.Id);
        }) { Height = 22, FontSize = 10 };
        _settings.Children.Add(Studio.Row(_blend, _opacity));
        _settings.Children.Add(Studio.Row(Studio.Label("Lock:", 11, "#a6a6a6"), _lock, _mode));
        grid.Children.Add(_settings);
        _scroll = new ScrollViewer
        {
            Content = _rows,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        // Wait for actual row and viewport arrangement before scrolling. Merely changing
        // selection cannot reveal the row using the previous inspector's viewport height.
        _scroll.LayoutUpdated += (_, _) => RevealSelection();
        _scroll.SizeChanged += (_, _) =>
        {
            // A larger mask/tone inspector can shrink the viewport without changing
            // ActiveLayerId. Keep the editing target visible after the new arrangement.
            if (_followActiveSelection)
                _revealSelection = true;
        };
        Grid.SetRow(_scroll, 1);
        grid.Children.Add(_scroll);
        _addMask = new StudioButton("Add layer mask", () => AddMaskRequested?.Invoke(), "mask");
        _deleteLayer = new StudioButton("Delete layer", () => Run(session => session.DeleteLayer()), "trash");
        var actions = Studio.Row(
            new StudioButton("Move layer down", () => Run(session => session.MoveLayer(-1)), "down"),
            new StudioButton("Move layer up", () => Run(session => session.MoveLayer(1)), "up"),
            _addMask,
            new StudioButton("New adjustment layer", () => AddAdjustmentRequested?.Invoke(), "adjust"),
            new StudioButton("New pixel layer", () => Run(session => session.AddLayer("Layer " + (session.Document.Layers.Count + 1))), "plus"),
            _deleteLayer);
        actions.Spacing = 3;
        actions.Margin = new Thickness(5, 4, 5, 4);
        var actionsHost = Studio.Box(actions);
        Grid.SetRow(actionsHost, 2);
        grid.Children.Add(actionsHost);
        Content = grid;
    }

    public void Bind(EditorSession session, ImageRenderer renderer)
    {
        if (!ReferenceEquals(_session, session))
        {
            _items.Clear();
            _rows.Children.Clear();
            _lastActive = session.Document.ActiveLayerId;
            // Preserve the initial sample's top-of-stack presentation; switching to
            // an opened document must reveal its saved active layer.
            _followActiveSelection = _session is not null;
            _revealSelection = _followActiveSelection;
        }
        _session = session;
        _renderer = renderer;
        Refresh();
    }

    private void Run(Action<EditorSession> action)
    {
        if (_session is null || _session.IsInTransaction)
            return;
        try
        {
            action(_session);
        }
        catch (Exception error) { Error?.Invoke(error.Message); }
    }

    private void RevealSelection()
    {
        if (!_revealSelection || _session is null || _scroll.ViewportHeight <= 0)
            return;
        var index = _session.Document.Layers.FindIndex(layer => layer.Id == _session.Document.ActiveLayerId);
        if (index < 0)
        {
            _revealSelection = false;
            return;
        }
        var rowIndex = _session.Document.Layers.Count - index - 1;
        if (rowIndex >= _rows.Children.Count || _rows.Children[rowIndex] is not FrameworkElement { ActualHeight: > 0 })
            return;
        // Clearing/rebuilding the rows can temporarily reset the extent and offset.
        // Do not consume the request against that intermediate layout.
        var expectedExtent = _rows.Children.Count * 44.0 - 1;
        if (_scroll.ExtentHeight + .5 < expectedExtent)
            return;
        var top = rowIndex * 44.0;
        var bottom = top + 43;
        var offset = _scroll.VerticalOffset;
        if (top < offset)
            offset = top;
        else if (bottom > offset + _scroll.ViewportHeight)
            offset = bottom - _scroll.ViewportHeight;
        offset = Math.Clamp(offset, 0, Math.Max(0, _scroll.ScrollableHeight));
        if (Math.Abs(offset - _scroll.VerticalOffset) > .5)
        {
            // ChangeView may schedule its offset update. Verify the resulting offset
            // on the next layout instead of marking an unpresented request complete.
            _scroll.ChangeView(null, offset, null, true);
            return;
        }
        _revealSelection = false;
    }

    private void ShowBlendMenu() => Studio.Menu(_blend, Enum.GetValues<LayerBlend>().Select(mode =>
        (mode.ToString(), "", (Action)(() => Run(editor => editor.Execute("Blend mode", document =>
        { if (document.ActiveLayer is { Locked: false } layer && layer.Kind != LayerKind.Adjustment) layer.Blend = mode; }))), true)));

    public void RefreshSelection()
    {
        if (_session is null) return;
        var active = _session.Document.ActiveLayer;
        var previous = _lastActive;
        if (previous != _session.Document.ActiveLayerId) _followActiveSelection = true;
        _lastActive = _session.Document.ActiveLayerId;
        if (_session.Document.EditMask) _followActiveSelection = true;
        _revealSelection |= _followActiveSelection;
        _updating = true;
        try
        {
            _addMask.IsEnabled = active is { Locked: false, Mask: null };
            _deleteLayer.IsEnabled = active is { Locked: false };
            _blend.IsEnabled = active is { Locked: false } && active.Kind != LayerKind.Adjustment;
            _blend.SetLabel((active?.Blend.ToString() ?? "Normal") + "        ⌄");
            // Retain the original automation name used by the blend menu tests.
            _blend.SetName(active?.Blend.ToString() ?? "Normal");
            _opacity.IsEnabled = active is { Locked: false };
            _opacity.Value = (active?.Opacity ?? 1) * 100;
            if (previous != _lastActive) _opacity.ResetPendingEdit();
            _lock.IsEnabled = active is not null;
            _lock.Selected(active?.Locked == true);
            _lock.SetName(active?.Locked == true ? "Unlock layer" : "Lock layer");
            _mode.IsEnabled = active?.Mask is not null;
            _mode.SetLabel(_session.Document.EditMask ? "Editing mask" : active?.Kind == LayerKind.Adjustment ? "Adjustment" : "Editing pixels");
        }
        finally { _updating = false; }
        if (previous != _lastActive && _items.TryGetValue(previous, out var old)) old.RefreshSelection(false, false);
        if (_items.TryGetValue(_lastActive, out var current)) current.RefreshSelection(true, _session.Document.EditMask);
        RevealSelection();
    }

    public void Refresh()
    {
        if (_session is null || _renderer is null) return;
        var document = _session.Document;
        for (var index = 0; index < document.Layers.Count; index++)
        {
            var layer = document.Layers[document.Layers.Count - index - 1];
            if (!_items.TryGetValue(layer.Id, out var row))
            {
                row = new LayerRow(_session, layer.Id, message => Error?.Invoke(message), () => RenameRequested?.Invoke());
                _items.Add(layer.Id, row);
                RowsCreated++;
            }
            if (index >= _rows.Children.Count || !ReferenceEquals(_rows.Children[index], row))
            {
                if (_rows.Children.Contains(row)) _rows.Children.Remove(row);
                _rows.Children.Insert(index, row);
            }
            row.Bind(layer, _renderer, layer.Id == document.ActiveLayerId, document.EditMask);
        }
        while (_rows.Children.Count > document.Layers.Count)
        {
            var row = (LayerRow)_rows.Children[^1];
            _items.Remove(row.LayerId);
            _rows.Children.RemoveAt(_rows.Children.Count - 1);
        }
        RefreshSelection();
    }
}
