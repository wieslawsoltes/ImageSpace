using ImageSpace.Skia;

namespace ImageSpace.Workbench;

public sealed class LayerPanel : UserControl
{
    private EditorSession? _session;
    private ImageRenderer? _renderer;
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

    public void Refresh()
    {
        if (_session is null)
            return;
        var session = _session;
        var active = session.Document.ActiveLayer;
        if (_lastActive != session.Document.ActiveLayerId)
        {
            _lastActive = session.Document.ActiveLayerId;
            _followActiveSelection = true;
        }
        if (session.Document.EditMask)
            _followActiveSelection = true;
        // Undo/redo and same-layer property edits rebuild rows too, so identity
        // changes alone are not a sufficient condition for restoring visibility.
        _revealSelection |= _followActiveSelection;
        _addMask.IsEnabled = active is { Locked: false, Mask: null };
        _deleteLayer.IsEnabled = active is { Locked: false };
        _settings.Children.Clear();
        var blend = new StudioButton(active?.Blend.ToString() ?? "Normal", () => { })
        {
            Width = 148,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            IsEnabled = active is { Locked: false } && active.Kind != LayerKind.Adjustment,
            Content = Studio.Label((active?.Blend.ToString() ?? "Normal") + "        ⌄", 11)
        };
        blend.Click += (_, _) => Studio.Menu(blend, Enum.GetValues<LayerBlend>().Select(mode =>
            (mode.ToString(), "", (Action)(() => Run(editor => editor.Execute("Blend mode", document =>
            {
                if (document.ActiveLayer is { Locked: false } layer && layer.Kind != LayerKind.Adjustment)
                    layer.Blend = mode;
            }))), true)));
        var opacity = new NumericField("Opacity", (active?.Opacity ?? 1) * 100, 0, 100, 118)
        {
            Format = "0",
            IsEnabled = active is { Locked: false }
        };
        opacity.ValueChanged += value => Run(editor => editor.Execute("Layer opacity", document =>
        {
            if (document.ActiveLayer is { Locked: false } layer)
                layer.Opacity = (float)value / 100;
        }));
        _settings.Children.Add(Studio.Row(blend, opacity));
        var lockButton = new StudioButton(active?.Locked == true ? "Unlock layer" : "Lock layer", () => Run(editor => editor.Execute("Layer lock", document =>
        {
            if (document.ActiveLayer is { } layer)
                layer.Locked = !layer.Locked;
        })), "lock")
        {
            Width = 26,
            Height = 22,
            IsEnabled = active is not null
        };
        lockButton.Selected(active?.Locked == true);
        var modeName = session.Document.EditMask ? "Editing mask" : active?.Kind == LayerKind.Adjustment ? "Adjustment" : "Editing pixels";
        var maskButton = new StudioButton(modeName, () =>
        {
            if (session.IsInTransaction || active?.Mask is null)
                return;
            session.Document.EditMask = !session.Document.EditMask;
            session.Notify();
        })
        {
            Height = 22,
            FontSize = 10,
            IsEnabled = active?.Mask is not null
        };
        _settings.Children.Add(Studio.Row(Studio.Label("Lock:", 11, "#a6a6a6"), lockButton, maskButton));
        _rows.Children.Clear();
        foreach (var layer in session.Document.Layers.AsEnumerable().Reverse())
        {
            var selected = layer.Id == session.Document.ActiveLayerId;
            var row = new Grid
            {
                Height = 43,
                Background = Studio.Brush(selected ? "#484848" : "#303030"),
                ColumnDefinitions =
                {
                    new() { Width = new GridLength(28) }, new() { Width = new GridLength(54) },
                    new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto }
                }
            };
            row.Children.Add(new StudioButton((layer.Visible ? "Hide " : "Show ") + layer.Name, () => Run(editor => editor.Execute("Layer visibility", document =>
            {
                var current = document.Layers.FirstOrDefault(item => item.Id == layer.Id);
                if (current is not null)
                    current.Visible = !current.Visible;
            })), layer.Visible ? "eye" : "hidden")
            {
                Width = 27,
                Height = 32,
                Padding = new Thickness(4)
            });
            var thumbnail = new Border
            {
                BorderThickness = new Thickness(selected && !session.Document.EditMask ? 2 : 1),
                BorderBrush = Studio.Brush(selected ? "#b6b6b6" : "#171717"),
                Child = new LayerThumbnail { Layer = layer, Renderer = _renderer },
                Margin = new Thickness(3, 5, 4, 5)
            };
            Grid.SetColumn(thumbnail, 1);
            row.Children.Add(thumbnail);
            var label = new StudioButton(layer.Name, () => session.SelectLayer(layer.Id))
            {
                Height = 41,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Studio.Brush(selected ? "#484848" : "#303030"),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(5, 0, 2, 0)
            };
            label.DoubleTapped += (_, e) => { session.SelectLayer(layer.Id); RenameRequested?.Invoke(); e.Handled = true; };
            Grid.SetColumn(label, 2);
            row.Children.Add(label);
            var suffix = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (layer.Mask is not null)
            {
                var link = new StudioButton((layer.MaskLinked ? "Unlink mask for " : "Link mask for ") + layer.Name, () => Run(editor =>
                {
                    editor.SelectMask(layer.Id);
                    if (editor.Document.ActiveLayer is { } selectedLayer)
                        editor.SetMaskLinked(!selectedLayer.MaskLinked);
                }), layer.MaskLinked ? "link" : "unlink")
                {
                    Width = 24,
                    Height = 28,
                    Padding = new Thickness(3),
                    IsEnabled = !layer.Locked
                };
                suffix.Children.Add(link);
                var mask = new StudioButton("Edit mask for " + layer.Name, () =>
                {
                    session.SelectMask(layer.Id);
                }, "mask")
                {
                    Width = 34,
                    Height = 28,
                    Padding = new Thickness(2),
                    Content = new LayerThumbnail { Layer = layer, ShowMask = true, Renderer = _renderer, Width = 28, Height = 20 }
                };
                mask.Selected(session.Document.EditMask && selected);
                suffix.Children.Add(mask);
            }
            if (layer.Locked)
                suffix.Children.Add(new IconView("lock") { Width = 14, Height = 14, Margin = new Thickness(4) });
            if (layer.Kind == LayerKind.Text)
                suffix.Children.Add(Studio.Label("T", 12, "#a9a9a9"));
            if (layer.Kind == LayerKind.Adjustment)
                suffix.Children.Add(new IconView("adjust") { Margin = new Thickness(4) });
            Grid.SetColumn(suffix, 3);
            row.Children.Add(suffix);
            _rows.Children.Add(row);
        }
    }
}
