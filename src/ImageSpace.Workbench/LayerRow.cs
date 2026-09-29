using ImageSpace.Skia;

namespace ImageSpace.Workbench;

/// <summary>Retained layer row. Delegates resolve current state by identity after undo/redo.</summary>
internal sealed class LayerRow : Grid
{
    private readonly EditorSession _session;
    private readonly Action<string> _error;
    private readonly StudioButton _eye, _label;
    private readonly IconView _eyeIcon = new("eye");
    private readonly LayerThumbnail _thumbnail = new();
    private readonly Border _thumbnailHost;
    private readonly Border _thumbnailSelection;
    private readonly IconView _clipIcon = new("clipping")
    {
        Width = 13,
        Height = 17,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        Opacity = 0
    };
    private readonly SolidColorBrush _selectedBackground = Studio.Brush("#484848");
    private readonly SolidColorBrush _normalBackground = Studio.Brush("#303030");
    private readonly SolidColorBrush _selectedBorder = Studio.Brush("#b6b6b6");
    private readonly SolidColorBrush _normalBorder = Studio.Brush("#171717");
    private readonly StackPanel _suffix = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly IconView _lock = new("lock") { Width = 14, Height = 14, Margin = new Thickness(4), Visibility = Visibility.Collapsed };
    private readonly TextBlock _type = Studio.Label("", 12, "#a9a9a9");
    private StudioButton? _link, _mask;
    private IconView? _linkIcon;
    private LayerThumbnail? _maskThumbnail;
    private bool? _selected, _maskSelected;
    public Guid LayerId
    {
        get;
    }
    private Layer? Current => _session.Document.Layers.FirstOrDefault(layer => layer.Id == LayerId);

    public LayerRow(EditorSession session, Guid layerId, Action<string> error, Action rename)
    {
        _session = session;
        _error = error;
        LayerId = layerId;
        Height = 43;
        ColumnDefinitions.Add(new()
        {
            Width = new GridLength(28)
        });
        ColumnDefinitions.Add(new()
        {
            Width = new GridLength(54)
        });
        ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        _eye = new StudioButton("", () => Edit("Layer visibility", layer => layer.Visible = !layer.Visible))
        {
            Width = 27,
            Height = 32,
            Padding = new Thickness(4),
            Content = _eyeIcon
        };
        Children.Add(_eye);

        // Changing BorderThickness on selection used to resize the SKCanvasElement,
        // remeasure both rows and redraw both thumbnails. Keep geometry invariant:
        // the second border pixel is an independent retained decoration.
        var thumbnailContent = new Grid();
        thumbnailContent.Children.Add(_thumbnail);
        _thumbnailSelection = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = _selectedBorder,
            IsHitTestVisible = false,
            Opacity = 0
        };
        thumbnailContent.Children.Add(_thumbnailSelection);
        _thumbnailHost = new Border
        {
            BorderThickness = new Thickness(1),
            Child = thumbnailContent,
            BorderBrush = _normalBorder,
            Margin = new Thickness(3, 5, 4, 5)
        };
        _thumbnailHost.PointerPressed += (_, e) => { _session.SelectLayer(LayerId); e.Handled = true; };
        SetColumn(_thumbnailHost, 1);
        Children.Add(_thumbnailHost);
        SetColumn(_clipIcon, 1);
        Children.Add(_clipIcon);
        // Alt/Option-click the boundary below this row, matching the contiguous
        // chain direction of a top-to-bottom Layers panel.
        PointerPressed += (_, e) =>
        {
            if ((e.KeyModifiers & Windows.System.VirtualKeyModifiers.Menu) == 0 ||
                e.GetCurrentPoint(this).Position.Y < ActualHeight - 4 || _session.IsInTransaction)
                return;
            try
            {
                _session.SelectLayer(LayerId);
                _session.ToggleClippingMask();
            }
            catch (Exception error) { _error(error.Message); }
            e.Handled = true;
        };
        _label = new StudioButton("", () => _session.SelectLayer(LayerId))
        {
            Height = 41,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(5, 0, 2, 0)
        };
        _label.DoubleTapped += (_, e) => { _session.SelectLayer(LayerId); rename(); e.Handled = true; };
        SetColumn(_label, 2);
        Children.Add(_label);
        _suffix.Children.Add(_lock);
        _suffix.Children.Add(_type);
        SetColumn(_suffix, 3);
        Children.Add(_suffix);
    }

    private void Edit(string name, Action<Layer> edit)
    {
        if (_session.IsInTransaction || Current is not { } layer)
            return;
        try
        {
            _session.Execute(name, _ => edit(layer));
        }
        catch (Exception error) { _error(error.Message); }
    }

    public void Bind(Layer layer, ImageRenderer renderer, bool selected, bool editMask, bool clippingBase = false)
    {
        _label.SetLabel(layer.Name);
        if (_label.Content is TextBlock text)
            text.TextDecorations = clippingBase ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None;
        _clipIcon.Opacity = layer.IsClipped ? 1 : 0;
        _thumbnailHost.Margin = new Thickness(layer.IsClipped ? 15 : 3, 5, 4, 5);
        _eye.SetName((layer.Visible ? "Hide " : "Show ") + layer.Name);
        _eyeIcon.Icon = layer.Visible ? "eye" : "hidden";
        _thumbnail.Bind(layer, renderer);
        _lock.Visibility = layer.Locked ? Visibility.Visible : Visibility.Collapsed;
        _type.Text = layer.Kind == LayerKind.Text ? "T" : layer.Kind == LayerKind.Adjustment ? "◐" : "";
        if (layer.Mask is not null && _mask is null)
        {
            _linkIcon = new IconView("link");
            _link = new StudioButton("", () =>
            {
                if (_session.IsInTransaction || Current is not { Locked: false } current)
                    return;
                try
                {
                    _session.SelectMask(LayerId);
                    _session.SetMaskLinked(!current.MaskLinked);
                }
                catch (Exception error) { _error(error.Message); }
            })
            {
                Width = 24,
                Height = 28,
                Padding = new Thickness(3),
                Content = _linkIcon
            };
            _maskThumbnail = new LayerThumbnail { ShowMask = true, Width = 28, Height = 20 };
            _mask = new StudioButton("", () => _session.SelectMask(LayerId))
            {
                Width = 34,
                Height = 28,
                Padding = new Thickness(2),
                Content = _maskThumbnail
            };
            _suffix.Children.Insert(0, _link);
            _suffix.Children.Insert(1, _mask);
            _maskSelected = null;
        }
        if (_mask is not null)
        {
            _mask.Visibility = _link!.Visibility = layer.Mask is null ? Visibility.Collapsed : Visibility.Visible;
            _mask.SetName("Edit mask for " + layer.Name);
            _link.SetName((layer.MaskLinked ? "Unlink mask for " : "Link mask for ") + layer.Name);
            _link.IsEnabled = !layer.Locked;
            _linkIcon!.Icon = layer.MaskLinked ? "link" : "unlink";
            _maskThumbnail!.Bind(layer, renderer);
        }
        RefreshSelection(selected, selected && editMask);
    }

    public void RefreshSelection(bool selected, bool editMask)
    {
        if (_selected == selected && _maskSelected == editMask)
            return;
        if (_selected != selected)
        {
            Background = _label.Background = selected ? _selectedBackground : _normalBackground;
            _thumbnailHost.BorderBrush = selected ? _selectedBorder : _normalBorder;
        }
        _selected = selected;
        _maskSelected = editMask;
        _thumbnailSelection.Opacity = selected && !editMask ? 1 : 0;
        _mask?.Selected(selected && editMask);
    }
}
