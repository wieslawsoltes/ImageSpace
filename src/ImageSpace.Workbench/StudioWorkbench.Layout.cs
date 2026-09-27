namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private UIElement BuildMenuBar()
    {
        var grid = new Grid
        {
            Background = Studio.Brush("#242424"),
            ColumnDefinitions =
            {
                new() { Width = GridLength.Auto },
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = GridLength.Auto }
            }
        };
        var logo = Studio.Label("Is", 16, "#80baff");
        logo.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        logo.Margin = new Thickness(11, 0, 12, 0);
        grid.Children.Add(logo);

        var menus = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var name in new[] { "File", "Edit", "Image", "Layer", "Type", "Select", "Filter", "View", "Window", "Help" })
        {
            StudioButton? button = null;
            button = new StudioButton(name, () => ShowMenu(name, button!))
            {
                Height = 27,
                Padding = new Thickness(7, 0, 7, 0),
                Background = Studio.Brush("#242424"),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0)
            };
            menus.Children.Add(button);
        }
        Grid.SetColumn(menus, 1);
        grid.Children.Add(menus);
        var title = Studio.Label("ImageSpace", 11, "#a6a6a6");
        title.Margin = new Thickness(10, 0, 15, 0);
        Grid.SetColumn(title, 2);
        grid.Children.Add(title);
        return grid;
    }

    private UIElement BuildToolPalette()
    {
        _toolPalette.Children.Add(Studio.Label("··", 11, "#777777"));
        (EditorTool Tool, string Icon, string Key)[] tools =
        [
            (EditorTool.Move, "move", "V"),
            (EditorTool.Marquee, "marquee", "M"),
            (EditorTool.EllipseSelect, "ellipse-select", "Shift+M"),
            (EditorTool.Lasso, "lasso", "L"),
            (EditorTool.Wand, "wand", "W"),
            (EditorTool.Crop, "crop", "C"),
            (EditorTool.Eyedropper, "eyedropper", "I"),
            (EditorTool.Brush, "brush", "B"),
            (EditorTool.Pencil, "pencil", "Shift+B"),
            (EditorTool.Clone, "clone", "S"),
            (EditorTool.Smudge, "smudge", "R"),
            (EditorTool.Eraser, "eraser", "E"),
            (EditorTool.Gradient, "gradient", "G"),
            (EditorTool.Fill, "fill", "Shift+G"),
            (EditorTool.Dodge, "dodge", "O"),
            (EditorTool.Burn, "burn", "Shift+O"),
            (EditorTool.Text, "text", "T"),
            (EditorTool.Rectangle, "rectangle", "U"),
            (EditorTool.Ellipse, "ellipse", "Shift+U"),
            (EditorTool.Hand, "hand", "H"),
            (EditorTool.Zoom, "zoom", "Z")
        ];
        foreach (var (tool, icon, key) in tools)
        {
            var button = new StudioButton($"{tool} tool ({key})", () => SelectTool(tool), icon)
            {
                Width = 34,
                Height = 28,
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(2)
            };
            button.Selected(tool == EditorTool.Move);
            _toolButtons[tool] = button;
            _toolPalette.Children.Add(button);
        }

        var swatches = new Grid { Width = 34, Height = 38, Margin = new Thickness(0, 8, 0, 0) };
        _fgButton = new StudioButton("Foreground color", () => _ = ColorAsync(true))
        {
            Width = 24, Height = 24, Padding = new Thickness(0), Content = null,
            BorderBrush = Studio.Brush("#dddddd"), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };
        _bgButton = new StudioButton("Background color", () => _ = ColorAsync(false))
        {
            Width = 24, Height = 24, Padding = new Thickness(0), Content = null,
            BorderBrush = Studio.Brush("#dddddd"), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10, 10, 0, 0)
        };
        swatches.Children.Add(_bgButton);
        swatches.Children.Add(_fgButton);
        _toolPalette.Children.Add(swatches);
        _toolPalette.Margin = new Thickness(4, 0, 4, 8);
        return new ScrollViewer
        {
            Content = _toolPalette,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Studio.Brush("#303030")
        };
    }

    private UIElement BuildPanelRail()
    {
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(1, 13, 1, 0) };
        foreach (var (name, icon) in new[] { ("History", "undo"), ("Properties", "rectangle"), ("Layers", "folder"), ("Color", "adjust") })
        {
            stack.Children.Add(new StudioButton(name, () =>
            {
                if (name is "History" or "Layers")
                {
                    SetBottomMode(name);
                }
                else if (name == "Color")
                {
                    _ = ColorAsync(true);
                }
                else
                {
                    _properties.Focus(FocusState.Programmatic);
                }
            }, icon) { Width = 27, Height = 30, Padding = new Thickness(5) });
        }
        return Studio.Box(stack, "#292929");
    }

    private static UIElement PanelHeader(string active, string secondary, Action action)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        var label = Studio.Box(Studio.Label(active), "#383838", new Thickness(12, 0, 12, 0));
        label.Height = 29;
        row.Children.Add(label);
        row.Children.Add(new StudioButton(secondary, action)
        {
            Height = 29,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(0),
            Background = Studio.Brush("#282828"),
            BorderThickness = new Thickness(0)
        });
        return Studio.Box(row, "#272727");
    }
}
