using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Automation;
using Windows.UI;
using ImageSpace.Core;
namespace ImageSpace.Controls;

public static class Studio
{
    public static FontFamily Font { get; set; } = new("Segoe UI");
    public static SolidColorBrush Brush(string hex)
    {
        var c = Rgba32.Parse(hex);
        return new(Color.FromArgb(c.A, c.R, c.G, c.B));
    }
    public static TextBlock Label(string text, double size = 12, string color = "#d7d7d7") => new() { Text = text, FontSize = size, FontFamily = Font, Foreground = Brush(color), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    public static Border Box(UIElement content, string color = "#292929", Thickness? padding = null) => new() { Child = content, Background = Brush(color), Padding = padding ?? new Thickness(0) };
    public static StackPanel Row(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var c in children)
            p.Children.Add(c);
        return p;
    }
    public static Border Divider() => new() { Height = 1, Background = Brush("#161616"), Margin = new Thickness(0, 5, 0, 5) };
    public static TextBox TextInput(string value, string name, double width = double.NaN)
    {
        var box = new TextBox { Text = value, Width = width, FontFamily = Font, FontSize = 12, MinHeight = 27, Padding = new Thickness(6, 3, 6, 3), Background = Brush("#202020"), Foreground = Brush("#dddddd"), BorderBrush = Brush("#454545"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), SelectionHighlightColor = Brush("#446f99") };
        AutomationProperties.SetName(box, name);
        return box;
    }
    [ThreadStatic] private static ControlTemplate? _buttonTemplate;
    private static long _templateBuilds;
    public static long ButtonTemplateBuilds => Interlocked.Read(ref _templateBuilds);
    public static ControlTemplate ButtonTemplate() => _buttonTemplate ??= CreateButtonTemplate();
    private static ControlTemplate CreateButtonTemplate()
    {
        Interlocked.Increment(ref _templateBuilds);
        return (ControlTemplate)XamlReader.Load("""
    <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
      <Grid>
        <VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="CommonStates">
          <VisualState x:Name="Normal"/><VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="Chrome.Background" Value="#454545"/></VisualState.Setters></VisualState>
          <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="Chrome.Background" Value="#1d1d1d"/></VisualState.Setters></VisualState>
          <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="Chrome.Opacity" Value="0.35"/></VisualState.Setters></VisualState>
        </VisualStateGroup><VisualStateGroup x:Name="FocusStates"><VisualState x:Name="Unfocused"/><VisualState x:Name="Focused"><VisualState.Setters><Setter Target="Chrome.BorderBrush" Value="#83b8f1"/></VisualState.Setters></VisualState><VisualState x:Name="PointerFocused"/></VisualStateGroup></VisualStateManager.VisualStateGroups>
        <Border x:Name="Chrome" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="{TemplateBinding CornerRadius}">
          <ContentPresenter Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}" Padding="{TemplateBinding Padding}" HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}"/>
        </Border>
      </Grid>
    </ControlTemplate>
    """);
    }
    public static void Menu(FrameworkElement anchor, IEnumerable<(string Label, string Shortcut, Action Action, bool Enabled)> entries)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(entries);
        if (anchor.XamlRoot is not { } root) return;
        var panel = new StackPanel { Spacing = 1, MinWidth = 238 };
        var popup = new Popup { XamlRoot = anchor.XamlRoot, IsLightDismissEnabled = true };
        foreach (var entry in entries)
        {
            if (entry.Label == "-")
            {
                panel.Children.Add(Divider());
                continue;
            }
            var row = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } } };
            row.Children.Add(Label(entry.Label));
            var shortcut = Label(entry.Shortcut, 11, "#a0a0a0");
            Grid.SetColumn(shortcut, 1);
            row.Children.Add(shortcut);
            var button = new StudioButton(entry.Label, () => { popup.IsOpen = false; entry.Action(); }) { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Height = 28, IsEnabled = entry.Enabled, Padding = new Thickness(13, 3, 13, 3) };
            panel.Children.Add(button);
        }
        popup.Child = new Border { Background = Brush("#303030"), BorderBrush = Brush("#131313"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(4), Child = new ScrollViewer { Content = panel, MaxHeight = 620, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        var point = anchor.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, anchor.ActualHeight));
        popup.HorizontalOffset = Math.Max(0, Math.Min(point.X, root.Size.Width - 260));
        popup.VerticalOffset = Math.Max(0, Math.Min(point.Y, root.Size.Height - 360));
        popup.IsOpen = true;
    }
}

public sealed class StudioButton : Button
{
    private static long _created;
    private bool? _selected;
    public static long CreatedCount => Interlocked.Read(ref _created);
    public StudioButton(string text, Action action, string? icon = null)
    {
        Interlocked.Increment(ref _created);
        Template = Studio.ButtonTemplate();
        Background = Studio.Brush("#303030");
        Foreground = Studio.Brush("#d7d7d7");
        BorderBrush = Studio.Brush("#303030");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(3);
        Padding = new Thickness(8, 3, 8, 3);
        MinWidth = 0;
        MinHeight = 0;
        Height = 27;
        FontFamily = Studio.Font;
        FontSize = 12;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
        Content = icon is null ? Studio.Label(text) : new IconView(icon);
        AutomationProperties.SetName(this, text);
        ToolTipService.SetToolTip(this, text);
        Click += (_, _) => action();
    }
    public void SetLabel(string text)
    {
        if (Content is TextBlock label) { if (label.Text != text) label.Text = text; }
        else Content = Studio.Label(text);
        SetName(text);
    }
    public void SetName(string text)
    {
        if (AutomationProperties.GetName(this) == text) return;
        AutomationProperties.SetName(this, text);
        ToolTipService.SetToolTip(this, text);
    }
    public void Selected(bool value)
    {
        if (_selected == value) return;
        _selected = value;
        Background = Studio.Brush(value ? "#505050" : "#292929");
        BorderBrush = Studio.Brush(value ? "#727272" : "#292929");
    }
}
