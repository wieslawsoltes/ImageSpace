using ImageSpace.Skia;
namespace ImageSpace.Workbench;

public sealed class LayerPanel : UserControl
{
    private EditorSession? _session; private ImageRenderer? _renderer; private readonly StackPanel _rows = new() { Spacing = 1 }; private readonly StackPanel _settings = new() { Spacing = 5, Margin = new Thickness(8, 6, 8, 7) };
    public event Action? AddMaskRequested; public event Action? AddAdjustmentRequested; public event Action? RenameRequested; public event Action<string>? Error;
    public LayerPanel()
    {
        var grid = new Grid { RowDefinitions = { new() { Height = GridLength.Auto }, new() { Height = new(1, GridUnitType.Star) }, new() { Height = GridLength.Auto } } };
        grid.Children.Add(_settings);
        var scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);
        var actions = Studio.Row(new StudioButton("Move layer down", () => Run(s => s.MoveLayer(-1)), "down"), new StudioButton("Move layer up", () => Run(s => s.MoveLayer(1)), "up"), new StudioButton("Add layer mask", () => AddMaskRequested?.Invoke(), "mask"), new StudioButton("New adjustment layer", () => AddAdjustmentRequested?.Invoke(), "adjust"), new StudioButton("New pixel layer", () => Run(s => s.AddLayer("Layer " + (s.Document.Layers.Count + 1))), "plus"), new StudioButton("Delete layer", () => Run(s => s.DeleteLayer()), "trash"));
        actions.Spacing = 3;
        actions.Margin = new Thickness(5, 4, 5, 4);
        var actionsHost = Studio.Box(actions);
        Grid.SetRow(actionsHost, 2);
        grid.Children.Add(actionsHost);
        Content = grid;
    }
    public void Bind(EditorSession session, ImageRenderer renderer)
    {
        _session = session;
        _renderer = renderer;
        Refresh();
    }
    private void Run(Action<EditorSession> action)
    {
        if (_session is null)
            return;
        try
        {
            action(_session);
        }
        catch (Exception ex) { Error?.Invoke(ex.Message); }
    }
    public void Refresh()
    {
        if (_session is null)
            return;
        var session = _session;
        var active = session.Document.ActiveLayer;
        _settings.Children.Clear();
        var blend = new StudioButton(active?.Blend.ToString() ?? "Normal", () => { }) { Width = 148, HorizontalContentAlignment = HorizontalAlignment.Left };
        blend.Content = Studio.Label((active?.Blend.ToString() ?? "Normal") + "        ⌄", 11);
        blend.Click += (_, _) => Studio.Menu(blend, Enum.GetValues<LayerBlend>().Select(b => (b.ToString(), "", (Action)(() => Run(s => s.Execute("Blend mode", d => { if (d.ActiveLayer is { } l) l.Blend = b; }))), active is not null)));
        var opacity = new NumericField("Opacity", (active?.Opacity ?? 1) * 100, 0, 100, 118) { Format = "0" };
        opacity.ValueChanged += v => Run(s => s.Execute("Layer opacity", d => { if (d.ActiveLayer is { } l && !l.Locked) l.Opacity = (float)v / 100; }));
        _settings.Children.Add(Studio.Row(blend, opacity));
        var lockButton = new StudioButton(active?.Locked == true ? "Unlock layer" : "Lock layer", () => Run(s => s.Execute("Layer lock", d => { if (d.ActiveLayer is { } l) l.Locked = !l.Locked; })), "lock") { Width = 26, Height = 22 };
        lockButton.Selected(active?.Locked == true);
        var maskButton = new StudioButton(session.Document.EditMask ? "Editing mask" : "Editing pixels", () => { if (active?.Mask is null) return; session.Document.EditMask = !session.Document.EditMask; session.Notify(); }) { Height = 22, FontSize = 10 };
        _settings.Children.Add(Studio.Row(Studio.Label("Lock:", 11, "#a6a6a6"), lockButton, maskButton));
        _rows.Children.Clear();
        foreach (var layer in session.Document.Layers.AsEnumerable().Reverse())
        {
            var row = new Grid { Height = 43, Background = Studio.Brush(layer.Id == session.Document.ActiveLayerId ? "#484848" : "#303030"), ColumnDefinitions = { new() { Width = new(28) }, new() { Width = new(54) }, new() { Width = new(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
            var eye = new StudioButton(layer.Visible ? "Hide " + layer.Name : "Show " + layer.Name, () => Run(s => s.Execute("Layer visibility", d => { var l = d.Layers.FirstOrDefault(x => x.Id == layer.Id); if (l is not null) l.Visible = !l.Visible; })), layer.Visible ? "eye" : "hidden") { Width = 27, Height = 32, Padding = new Thickness(4) };
            row.Children.Add(eye);
            var thumb = new Border { BorderThickness = new Thickness(layer.Id == session.Document.ActiveLayerId && !session.Document.EditMask ? 2 : 1), BorderBrush = Studio.Brush(layer.Id == session.Document.ActiveLayerId ? "#b6b6b6" : "#171717"), Child = new LayerThumbnail { Layer = layer, Renderer = _renderer }, Margin = new Thickness(3, 5, 4, 5) };
            Grid.SetColumn(thumb, 1);
            row.Children.Add(thumb);
            var label = new StudioButton(layer.Name, () => session.SelectLayer(layer.Id)) { Height = 41, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Background = Studio.Brush(layer.Id == session.Document.ActiveLayerId ? "#484848" : "#303030"), BorderThickness = new Thickness(0), Padding = new Thickness(5, 0, 2, 0) };
            label.DoubleTapped += (_, e) => { session.SelectLayer(layer.Id); RenameRequested?.Invoke(); e.Handled = true; };
            Grid.SetColumn(label, 2);
            row.Children.Add(label);
            var suffix = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (layer.Mask is not null)
            {
                var mask = new StudioButton("Edit mask for " + layer.Name, () => { session.SelectLayer(layer.Id); session.Document.EditMask = true; session.Notify(); }, "mask") { Width = 27, Height = 28 };
                mask.Selected(session.Document.EditMask && layer.Id == session.Document.ActiveLayerId);
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
