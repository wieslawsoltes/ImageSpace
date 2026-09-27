using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    /// <summary>Opt-in, read-only diagnostics. No document mutation or command execution is exposed.</summary>
    public string CaptureDiagnostics()
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            var d = Session.Document;
            var l = d.ActiveLayer;
            json.WriteStartObject();
            json.WriteBoolean("ready", true);
            json.WriteString("name", d.Name);
            json.WriteString("tool", Surface.Tool.ToString());
            json.WriteNumber("documents", _documents.Count);
            json.WriteNumber("layers", d.Layers.Count);
            json.WriteNumber("width", d.Width);
            json.WriteNumber("height", d.Height);
            json.WriteNumber("history", Session.History.Count);
            json.WriteNumber("revision", Session.Revision);
            json.WriteBoolean("dirty", Session.IsDirty);
            json.WriteBoolean("busy", _busy);
            json.WriteBoolean("dialog", _dialogOpen);
            json.WriteBoolean("canUndo", Session.CanUndo);
            json.WriteBoolean("canRedo", Session.CanRedo);
            json.WriteNumber("zoom", Surface.Zoom);
            json.WriteNumber("panX", Surface.Pan.X);
            json.WriteNumber("panY", Surface.Pan.Y);
            json.WriteString("activeLayer", l?.Name);
            json.WriteString("activeKind", l?.Kind.ToString());
            json.WriteNumber("activeX", l?.X ?? 0);
            json.WriteNumber("activeY", l?.Y ?? 0);
            json.WriteNumber("activeOpacity", l?.Opacity ?? 1);
            json.WriteBoolean("activeVisible", l?.Visible ?? false);
            json.WriteBoolean("activeLocked", l?.Locked ?? false);
            json.WriteBoolean("mask", l?.Mask is not null);
            json.WriteBoolean("editMask", d.EditMask);
            json.WriteBoolean("selection", d.Selection is not null);
            json.WriteNumber("tileUploads", Surface.Renderer.TileUploads);
            json.WriteString("backend", ComputeBackend);
            json.WriteString("status", _status.Text);
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    public string CaptureControls()
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartArray();
            if (XamlRoot is { Content: FrameworkElement root })
            {
                var visited = new HashSet<DependencyObject>();
                var viewport = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
                Visit(root, viewport);
                foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot))
                if (popup.Child is { } child)
                    Visit(child, viewport);
                void Visit(DependencyObject element, Rect clip)
                {
                    if (!visited.Add(element) || visited.Count > 15000)
                        return;
                    if (element is FrameworkElement view)
                    {
                        if (view.Visibility != Visibility.Visible || view.Opacity <= 0)
                            return;
                        try
                        {
                            var bounds = view.TransformToVisual(root).TransformBounds(new Rect(0, 0, view.ActualWidth, view.ActualHeight));
                            var visible = bounds;
                            visible.Intersect(clip);
                            if (view is ScrollViewer)
                                clip = visible;
                            var name = AutomationProperties.GetName(view);
                            if (string.IsNullOrEmpty(name))
                                name = view switch
                                {
                                    ButtonBase { Content: string text } => text,
                                    TextBox text => text.PlaceholderText,
                                    _ => ""
                                };
                            if (view == Surface)
                                name = "Image canvas";
                            if (name.Length > 0 && visible.Width > 1 && visible.Height > 1)
                            {
                                json.WriteStartObject();
                                json.WriteString("name", name);
                                json.WriteString("type", view.GetType().Name);
                                json.WriteNumber("x", visible.X);
                                json.WriteNumber("y", visible.Y);
                                json.WriteNumber("width", visible.Width);
                                json.WriteNumber("height", visible.Height);
                                json.WriteBoolean("enabled", view is not Control control || control.IsEnabled);
                                if (view is TextBox input)
                                    json.WriteString("value", input.Text);
                                json.WriteEndObject();
                            }
                        }
                        catch (ArgumentException) { return; }
                    }
                    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                        Visit(VisualTreeHelper.GetChild(element, i), clip);
                }
            }
            json.WriteEndArray();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
