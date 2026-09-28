using System.Text.Json;
using System.Diagnostics;
using ImageSpace.Storage;
using ImageSpace.Documents;
using ImageSpace.Skia;
using Microsoft.UI.Xaml.Automation;
using Windows.System;
using Windows.Foundation;
namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench : UserControl, IDisposable
{
    private readonly IEditorStorage _storage; private readonly List<EditorSession> _documents = []; private readonly Grid _root = new(); private readonly Grid _workspace = new();
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 1 }; private readonly StackPanel _options = new() { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(9, 5, 9, 5) };
    private readonly Dictionary<EditorTool, StudioButton> _toolButtons = []; private readonly StackPanel _toolPalette = new() { Spacing = 1 }; private readonly LayerPanel _layers = new(); private readonly PropertyPanel _properties = new();
    private readonly TextBlock _status = Studio.Label("Ready", 11, "#b5b5b5"); private readonly TextBlock _metrics = Studio.Label("", 11, "#9e9e9e"); private readonly TextBlock _zoom = Studio.Label("100%", 11); private readonly TextBlock _colorHex = Studio.Label("#3691E6", 11, "#b4b4b4");
    private readonly HistogramView _histogram = new(); private readonly ContentControl _bottomPanel = new(); private readonly StackPanel _history = new() { Spacing = 1 }; private readonly StackPanel _channels = new() { Spacing = 4 };
    private readonly DispatcherTimer _recoveryTimer = new() { Interval = TimeSpan.FromSeconds(8) }; private long _lastRecovered = -1; private Guid _lastRecoveredDocument; private bool _savingRecovery, _disposed, _dialogOpen, _busy;
    private string _bottomMode = "Layers"; private bool _panelsHidden; private PixelSurface? _clipboard; private StudioButton? _fgButton, _bgButton;
    private Rgba32? _shownForeground, _shownBackground;
    public EditorSession Session { get; private set; }
    public ImageViewport Surface { get; }
    public IReadOnlyList<EditorSession> Documents => _documents;
    public Func<PixelSurface, FilterKind, float, float, Task<PixelSurface?>>? GpuFilter { get; set; }
    public string ComputeBackend { get; set; } = "CPU kernels / Skia compositor";
    public event Action? StateChanged;
    public StudioWorkbench(EditorSession session, IEditorStorage storage)
    {
        _storage = storage;
        Session = session;
        _documents.Add(session);
        session.Changed += SessionChanged;
        Surface = new(session);
        Surface.Status += ShowStatus;
        Surface.ColorPicked += c => { Surface.Foreground = c; RefreshColors(); };
        Surface.TextEditRequested += l => _ = EditTextAsync(l);
        Surface.ViewChanged += () => { RefreshStatus(); StateChanged?.Invoke(); };
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        IsTabStop = true;
        Background = Studio.Brush("#292929");
        _root.RowDefinitions.Add(new() { Height = new(28) });
        _root.RowDefinitions.Add(new() { Height = new(39) });
        _root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new() { Height = new(24) });
        var menu = BuildMenuBar();
        _root.Children.Add(menu);
        var optionsHost = new ScrollViewer { Content = _options, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Studio.Brush("#353535") };
        Grid.SetRow(optionsHost, 1);
        _root.Children.Add(optionsHost);
        _workspace.ColumnDefinitions.Add(new() { Width = new(42) });
        _workspace.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        _workspace.ColumnDefinitions.Add(new() { Width = new(30) });
        _workspace.ColumnDefinitions.Add(new() { Width = new(292) });
        var tools = BuildToolPalette();
        _workspace.Children.Add(tools);
        var documentArea = new Grid { RowDefinitions = { new() { Height = new(29) }, new() { Height = new(1, GridUnitType.Star) } } };
        Grid.SetColumn(documentArea, 1);
        _workspace.Children.Add(documentArea);
        documentArea.Children.Add(new ScrollViewer { Content = _tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = Studio.Brush("#242424") });
        Grid.SetRow(Surface, 1);
        documentArea.Children.Add(Surface);
        var rail = BuildPanelRail();
        Grid.SetColumn(rail, 2);
        _workspace.Children.Add(rail);
        var right = BuildRightPanels();
        Grid.SetColumn(right, 3);
        _workspace.Children.Add(right);
        Grid.SetRow(_workspace, 2);
        _root.Children.Add(_workspace);
        var statusbar = new Grid { Background = Studio.Brush("#303030"), Padding = new Thickness(10, 0, 10, 0), ColumnDefinitions = { new() { Width = new(72) }, new() { Width = new(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
        statusbar.Children.Add(_zoom);
        Grid.SetColumn(_status, 1);
        statusbar.Children.Add(_status);
        Grid.SetColumn(_metrics, 2);
        statusbar.Children.Add(_metrics);
        Grid.SetRow(statusbar, 3);
        _root.Children.Add(statusbar);
        Content = _root;
        AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(KeyDownHandler), true);
        KeyUp += (_, e) => { if (e.Key == VirtualKey.Space) Surface.IsSpaceDown = false; };
        _layers.Error += ShowStatus;
        _layers.AddMaskRequested += () => Run(() => Session.AddMask());
        _layers.AddAdjustmentRequested += () => ShowAdjustmentMenu(_layers);
        _layers.RenameRequested += () => _ = RenameLayerAsync();
        _properties.MaskPreviewChanged += Surface.SetMaskPreview;
        _properties.Error += ShowStatus;
        _properties.TextEditRequested += l => _ = EditTextAsync(l);
        _recoveryTimer.Tick += async (_, _) => await RecoverAsync();
        Loaded += (_, _) => { _recoveryTimer.Start(); Refresh(); };
        Unloaded += (_, _) => _recoveryTimer.Stop();
        SizeChanged += (_, _) => { var compact = ActualWidth < 800; _workspace.ColumnDefinitions[3].Width = new GridLength(_panelsHidden ? 0 : compact ? 252 : 292); _workspace.ColumnDefinitions[2].Width = new GridLength(_panelsHidden || compact ? 0 : 30); };
        Refresh();
    }
    private int _historyTravel;
    public long UiRefreshes { get; private set; }
    public long SelectionRefreshes { get; private set; }
    public double LastUiRefreshMilliseconds { get; private set; }
    public double LastSelectionRefreshMilliseconds { get; private set; }
    public double MaxSelectionRefreshMilliseconds { get; private set; }
    private void SessionChanged(object? sender, EventArgs e)
    {
        if (_historyTravel > 0) return;
        if (!ReferenceEquals(sender, Session)) { RefreshTabs(); return; }
        Refresh(e is EditorChangedEventArgs change ? change.Change : EditorChange.Document);
    }
    private void Refresh(EditorChange change = EditorChange.Document)
    {
        if (_disposed) return;
        var started = Stopwatch.GetTimestamp();
        UiRefreshes++;
        if (change == EditorChange.SavedState)
        {
            RefreshTabs();
        }
        else
        {
            // Primary selection UI is synchronous and contains no preview rendering.
            _properties.Bind(Session);
            if (change == EditorChange.ActiveTarget) _layers.RefreshSelection();
            else
            {
                RefreshTabs();
                _layers.Bind(Session, Surface.Renderer);
                _historyDirty = true;
                if (_bottomMode == "History" && !_panelsHidden) RefreshHistory();
                if (_bottomMode == "Channels" && !_panelsHidden) { RefreshChannels(); UpdateHistogram(_histogramChannel); }
                RefreshStatus();
            }
            // Target selection cannot change document dimensions, zoom, or swatches.
            // Tool options still follow the selected editing channel (content/mask).
            RefreshOptions();
        }
        LastUiRefreshMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (change == EditorChange.ActiveTarget)
        {
            SelectionRefreshes++;
            LastSelectionRefreshMilliseconds = LastUiRefreshMilliseconds;
            MaxSelectionRefreshMilliseconds = Math.Max(MaxSelectionRefreshMilliseconds, LastUiRefreshMilliseconds);
        }
        StateChanged?.Invoke();
    }
    public void ShowStatus(string text) { if (_status.Text != text) _status.Text = text; }
    private void Run(Action action)
    {
        if (_busy) return;
        try { action(); }
        catch (Exception ex) { ShowStatus(ex.Message); }
    }
    private void RefreshStatus()
    {
        var zoom = $"{Surface.Zoom * 100:0.#}%";
        var metrics = $"{Session.Document.Width} × {Session.Document.Height} px   ·   RGB/8   ·   {Session.Document.Layers.Count} layers";
        if (_zoom.Text != zoom)
        {
            _zoom.Text = zoom;
            // View changes do not refresh the document or rebuild its tabs. Keep
            // the active retained caption in sync after Fit, resize and wheel zoom.
            if (_documentTabs.TryGetValue(Session, out var tab))
                tab.Title.SetLabel(Session.Document.Name + (Session.IsDirty ? " *" : "") + "  @ " + zoom);
        }
        if (_metrics.Text != metrics) _metrics.Text = metrics;
    }
    private sealed record DocumentTab(Border Host, StudioButton Title, StudioButton Close);
    private readonly Dictionary<EditorSession, DocumentTab> _documentTabs = [];
    private StudioButton? _newDocument;
    public long TabsCreated { get; private set; }
    private void RefreshTabs()
    {
        for (var index = 0; index < _documents.Count; index++)
        {
            var session = _documents[index];
            if (!_documentTabs.TryGetValue(session, out var tab))
            {
                var title = new StudioButton("", () => Switch(session)) { Height = 28, MaxWidth = 260, Padding = new Thickness(13, 0, 8, 0), CornerRadius = new CornerRadius(0), BorderThickness = new Thickness(0) };
                var close = new StudioButton("", () => _ = CloseAsync(session), "close") { Height = 28, Width = 23, Padding = new Thickness(4), CornerRadius = new CornerRadius(0) };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
                row.Children.Add(title); row.Children.Add(close);
                tab = new DocumentTab(Studio.Box(row), title, close);
                _documentTabs.Add(session, tab);
                TabsCreated++;
            }
            tab.Title.SetLabel(session.Document.Name + (session.IsDirty ? " *" : "") + "  @ " + (session == Session ? $"{Surface.Zoom * 100:0.#}%" : "RGB/8"));
            tab.Title.Selected(session == Session);
            tab.Close.SetName("Close " + session.Document.Name);
            if (index >= _tabs.Children.Count || !ReferenceEquals(_tabs.Children[index], tab.Host))
            {
                _tabs.Children.Remove(tab.Host);
                _tabs.Children.Insert(index, tab.Host);
            }
        }
        foreach (var session in _documentTabs.Keys.Where(session => !_documents.Contains(session)).ToArray())
        {
            _tabs.Children.Remove(_documentTabs[session].Host);
            _documentTabs.Remove(session);
        }
        _newDocument ??= new StudioButton("New document", () => _ = NewAsync(), "plus") { Height = 28, Width = 28, Padding = new Thickness(6) };
        if (!_tabs.Children.Contains(_newDocument)) _tabs.Children.Add(_newDocument);
    }
    private void Switch(EditorSession session)
    {
        if (Session == session) return;
        Surface.CancelGesture();
        Session = session;
        _shownHistory.Clear(); _historyButtons.Clear(); _history.Children.Clear();
        _historySession = null; _historyHeader = null; _historyDirty = true;
        _histogramTimer?.Stop(); _channelPreview.Clear();
        Surface.Session = session;
        _lastRecovered = -1;
        Refresh();
        Surface.Fit();
    }
    public void AddDocument(ImageDocument document, bool markDirty = false)
    {
        if (_documents.Count >= 12) throw new InvalidOperationException("Close a document before opening more than 12 tabs.");
        var session = new EditorSession(document);
        session.Changed += SessionChanged;
        _documents.Add(session);
        Switch(session);
        if (markDirty) session.Execute("New document", _ => { });
    }
    private async Task CloseAsync(EditorSession session)
    {
        if (session.IsDirty && !await ConfirmAsync("Close without saving?", $"Unsaved changes to {session.Document.Name} will be lost.", "Discard changes")) return;
        if (_documents.Count == 1)
        {
            var d = new ImageDocument(1000, 680, "Untitled");
            var l = Layer.Raster("Layer 1", 1000, 680);
            d.Layers.Add(l);
            d.ActiveLayerId = l.Id;
            AddDocument(d);
        }
        session.Changed -= SessionChanged;
        _documents.Remove(session);
        if (Session == session) Switch(_documents[^1]);
        RefreshTabs();
    }
    public void SelectTool(EditorTool tool)
    {
        Surface.Tool = tool;
        foreach (var (k, b) in _toolButtons) b.Selected(k == tool);
        RefreshOptions();
        ShowStatus(ToolHelp(tool));
        StateChanged?.Invoke();
    }
    private static string ToolHelp(EditorTool t) => t switch { EditorTool.Clone => "Alt-click to set a clone source, then paint.", EditorTool.Crop => "Drag a crop rectangle. Enter applies; Escape cancels.", EditorTool.Marquee or EditorTool.EllipseSelect or EditorTool.Lasso or EditorTool.Wand => "Shift adds · Alt subtracts · Shift+Alt intersects", EditorTool.Move => "Drag to move. Handles resize; top handle rotates. Shift constrains.", EditorTool.Text => "Click to add text. Double-click a text layer to edit.", EditorTool.Brush or EditorTool.Eraser => "Paint on an unlocked pixel layer. [ and ] change brush size.", _ => t.ToString() };
    private void RefreshColors()
    {
        var foreground = Surface.Foreground;
        var background = Surface.BackgroundColor;
        if (_shownForeground != foreground && _fgButton is not null)
        {
            _colorHex.Text = foreground.Hex;
            _fgButton.Background = Studio.Brush(foreground.Hex);
            _shownForeground = foreground;
        }
        if (_shownBackground != background && _bgButton is not null)
        {
            _bgButton.Background = Studio.Brush(background.Hex);
            _shownBackground = background;
        }
    }
    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _recoveryTimer.Stop();
        foreach (var s in _documents) s.Changed -= SessionChanged;
        _properties.Dispose();
        _histogramTimer?.Stop();
        _channelPreview.Dispose();
        _documentTabs.Clear();
        Surface.Dispose();
    }
}
