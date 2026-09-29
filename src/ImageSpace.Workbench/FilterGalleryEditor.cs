using ImageSpace.Filters;
using Microsoft.UI.Xaml.Automation;

namespace ImageSpace.Workbench;

/// <summary>Ordered filter stack editor with sampled, latest-request-only previews. Never mutates its source document.</summary>
public sealed class FilterGalleryEditor : UserControl, IAsyncDisposable
{
    private readonly List<FilterOperation> _operations;
    private readonly List<StudioButton> _rowButtons = [];
    private readonly StackPanel _rows = new() { Spacing = 3 };
    private readonly NumericField _amount = new("Gallery amount", 0, -100, 100, 220);
    private readonly NumericField _secondary = new("Gallery contrast", 0, -99, 300, 220);
    private readonly TextBlock _status = Studio.Label("Preparing preview…", 11, "#b5b5b5");
    private readonly TextBlock _hint = Studio.Label("", 11, "#aaaaaa");
    private readonly FilterPreviewCanvas _preview = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PixelSurface _source;
    private readonly float _scale;
    private readonly Func<PixelSurface, CancellationToken, Task<IFilterSession>> _createSession;
    private IFilterSession? _session;
    private Task _pending = Task.CompletedTask;
    private long _revision;
    private int _selected;
    private bool _updating, _disposed, _rerun;
    public FilterOperation[] Operations => FilterRecipe.Capture(_operations);

    public FilterGalleryEditor(PixelSurface source, IReadOnlyList<FilterOperation> operations,
        Func<PixelSurface, CancellationToken, Task<IFilterSession>> createSession)
    {
        _operations = FilterRecipe.Capture(operations).ToList();
        _createSession = createSession ?? throw new ArgumentNullException(nameof(createSession));
        _scale = Math.Min(1, 256f / Math.Max(source.Width, source.Height));
        _source = _scale == 1 ? source.Snapshot() : RasterOperations.Resize(source,
            Math.Max(1, (int)Math.Round(source.Width * _scale)), Math.Max(1, (int)Math.Round(source.Height * _scale)));
        _preview.SetBefore(_source);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        var root = new Grid
        {
            ColumnSpacing = 14,
            ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = new GridLength(150) }, new() { Width = new GridLength(232) } },
            RowDefinitions = { new() { Height = new GridLength(1, GridUnitType.Star) }, new() { Height = GridLength.Auto } }
        };
        var preview = new Grid { RowDefinitions = { new() { Height = new GridLength(1, GridUnitType.Star) }, new() { Height = GridLength.Auto } } };
        AutomationProperties.SetName(_preview, "Filter Gallery preview");
        preview.Children.Add(_preview);
        var compare = new Slider { Minimum = 0, Maximum = 100, Value = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(compare, "Filter Gallery comparison");
        compare.ValueChanged += (_, e) => _preview.Split = (float)e.NewValue / 100;
        var comparePanel = new StackPanel { Spacing = 3 };
        comparePanel.Children.Add(Studio.Label("Compare: filtered → original", 11)); comparePanel.Children.Add(compare);
        Grid.SetRow(comparePanel, 1); preview.Children.Add(comparePanel); root.Children.Add(preview);

        var catalogue = new StackPanel { Spacing = 3 };
        catalogue.Children.Add(Studio.Label("FILTERS", 11, "#b5b5b5"));
        foreach (var kind in Enum.GetValues<FilterKind>())
        {
            var filter = kind;
            catalogue.Children.Add(new StudioButton("Add " + kind, () => Add(filter))
            { Height = 29, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left });
        }
        var catalogScroll = new ScrollViewer { Content = catalogue, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(catalogScroll, 1); root.Children.Add(catalogScroll);
        var editor = new StackPanel { Spacing = 9 };
        editor.Children.Add(Studio.Label("EFFECT STACK · TOP TO BOTTOM", 11, "#b5b5b5"));
        editor.Children.Add(new ScrollViewer { Content = _rows, Height = 205, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        editor.Children.Add(Studio.Row(new StudioButton("Move effect up", () => Move(-1), "up"),
            new StudioButton("Move effect down", () => Move(1), "down"), new StudioButton("Remove effect", Remove, "trash")));
        editor.Children.Add(_amount); editor.Children.Add(_secondary); editor.Children.Add(_hint);
        _hint.TextWrapping = TextWrapping.Wrap;
        editor.Children.Add(new StudioButton("Reset filter parameters", Reset));
        var editorScroll = new ScrollViewer { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(editorScroll, 2); root.Children.Add(editorScroll);
        _status.TextWrapping = TextWrapping.Wrap; _status.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(_status, 1); Grid.SetColumnSpan(_status, 3); root.Children.Add(_status);
        Content = root;
        _amount.ValueChanged += value => Change(op => op with { Amount = (float)value });
        _secondary.ValueChanged += value => Change(op => op with { Secondary = (float)value });
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            if (!_pending.IsCompleted) { _rerun = true; return; }
            _pending = PreviewAsync();
        };
        Loaded += (_, _) => QueuePreview();
        RebuildRows();
    }

    private void Add(FilterKind kind)
    {
        if (_operations.Count == FilterRecipe.MaximumOperations) { _status.Text = "The stack is limited to sixteen effects."; return; }
        _operations.Add(FilterOperation.Default(kind)); _selected = _operations.Count - 1; RebuildRows(); QueuePreview();
    }
    private void Move(int direction)
    {
        var next = _selected + direction;
        if ((uint)_selected >= (uint)_operations.Count || (uint)next >= (uint)_operations.Count) return;
        (_operations[next], _operations[_selected]) = (_operations[_selected], _operations[next]);
        _selected = next; RebuildRows(); QueuePreview();
    }
    private void Remove()
    {
        if ((uint)_selected >= (uint)_operations.Count) return;
        _operations.RemoveAt(_selected); _selected = Math.Max(0, Math.Min(_selected, _operations.Count - 1)); RebuildRows(); QueuePreview();
    }
    private void Reset() => Change(op => FilterOperation.Default(op.Kind) with { Enabled = op.Enabled });
    private void Change(Func<FilterOperation, FilterOperation> change)
    {
        if (_updating || _disposed || (uint)_selected >= (uint)_operations.Count) return;
        _operations[_selected] = change(_operations[_selected]); BindParameters(); QueuePreview();
    }
    private void RebuildRows()
    {
        _rows.Children.Clear(); _rowButtons.Clear();
        for (var i = 0; i < _operations.Count; i++)
        {
            var index = i;
            var enabled = new CheckBox { IsChecked = _operations[i].Enabled, MinWidth = 24, MinHeight = 28, Width = 28 };
            AutomationProperties.SetName(enabled, "Enable gallery effect " + (i + 1));
            enabled.Click += (_, _) => { _operations[index] = _operations[index] with { Enabled = enabled.IsChecked == true }; QueuePreview(); };
            var select = new StudioButton($"Effect {i + 1}: {_operations[i].Kind}", () => { _selected = index; BindParameters(); })
            { Height = 28, Width = 185, HorizontalContentAlignment = HorizontalAlignment.Left };
            _rowButtons.Add(select); _rows.Children.Add(Studio.Row(enabled, select));
        }
        BindParameters();
    }
    private void BindParameters()
    {
        _updating = true;
        try
        {
            for (var i = 0; i < _rowButtons.Count; i++) _rowButtons[i].Selected(i == _selected);
            var op = (uint)_selected < (uint)_operations.Count ? _operations[_selected] : null;
            _amount.IsEnabled = op is not null && op.Kind is not (FilterKind.Invert or FilterKind.Grayscale or FilterKind.Sepia or FilterKind.Emboss or FilterKind.Edges);
            _secondary.Visibility = op?.Kind == FilterKind.BrightnessContrast ? Visibility.Visible : Visibility.Collapsed;
            if (op is null) { _hint.Text = "Add an effect from the catalogue."; return; }
            (_amount.Minimum, _amount.Maximum) = op.Kind switch
            {
                FilterKind.GaussianBlur => (0, 32), FilterKind.Gamma => (.1, 10), FilterKind.Sharpen => (.1, 5),
                FilterKind.Threshold => (0, 255), FilterKind.Posterize => (2, 256), FilterKind.Pixelate => (2, 128),
                FilterKind.Noise => (0, 100), _ => (-100, 100)
            };
            _amount.Step = op.Kind is FilterKind.Gamma or FilterKind.GaussianBlur or FilterKind.Sharpen ? .1 : 1;
            _amount.Value = op.Amount; _secondary.Value = op.Secondary;
            _hint.Text = op.Kind switch
            {
                FilterKind.GaussianBlur => "Gaussian sigma in authored pixels. Premultiplied edge coverage.",
                FilterKind.Pixelate => "Block size in authored pixels. Partial edge blocks are averaged independently.",
                FilterKind.Noise => "Seeded monochromatic noise. CPU fallback preserves the established seed sequence.",
                _ => "Stack order affects the result. Disable an effect to compare without deleting it."
            };
        }
        finally { _updating = false; }
    }
    private void QueuePreview()
    {
        if (_disposed) return;
        _revision++; _timer.Stop(); _timer.Start();
    }
    private async Task PreviewAsync()
    {
        var revision = _revision;
        var operations = FilterRecipe.ForPreview(_operations, _scale);
        try
        {
            _session ??= await _createSession(_source, _lifetime.Token);
            var result = await _session.ApplyAsync(operations, _lifetime.Token);
            if (!_disposed && revision == _revision)
            {
                _preview.SetAfter(result);
                _status.Text = $"Preview ready · {_session.Backend} · {_operations.Count} effects · sampled {_source.Width}×{_source.Height}. Selection is applied on final commit.";
                AutomationProperties.SetName(_status, "Filter Gallery preview ready");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_disposed) _status.Text = "Preview failed: " + error.Message; }
        finally
        {
            if (!_disposed && (_rerun || revision != _revision)) { _rerun = false; _timer.Start(); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; _timer.Stop(); _lifetime.Cancel();
        await _pending;
        if (_session is not null) await _session.DisposeAsync();
        _preview.Dispose(); _lifetime.Dispose();
    }
}
