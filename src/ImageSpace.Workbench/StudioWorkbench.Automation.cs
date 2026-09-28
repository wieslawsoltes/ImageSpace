using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private ToneCurve? _diagnosticCurve;
    private int _diagnosticCurveMidpoint;
    /// <summary>Opt-in, read-only diagnostics. No commands or document mutations are exposed.</summary>
    public string CaptureDiagnostics()
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            var document = Session.Document;
            var layer = document.ActiveLayer;
            json.WriteStartObject();
            json.WriteBoolean("ready", true);
            json.WriteNumber("contentRevision", Session.ContentRevision);
            json.WriteNumber("uiRefreshes", UiRefreshes);
            json.WriteNumber("selectionRefreshes", SelectionRefreshes);
            json.WriteNumber("lastUiRefreshMs", LastUiRefreshMilliseconds);
            json.WriteNumber("lastSelectionRefreshMs", LastSelectionRefreshMilliseconds);
            json.WriteNumber("maxSelectionRefreshMs", MaxSelectionRefreshMilliseconds);
            json.WriteNumber("buttonsCreated", StudioButton.CreatedCount);
            json.WriteNumber("buttonTemplateBuilds", Studio.ButtonTemplateBuilds);
            json.WriteNumber("layerRowsCreated", _layers.RowsCreated);
            json.WriteNumber("inspectorBuilds", _properties.InspectorBuilds);
            json.WriteNumber("historyButtonsCreated", HistoryButtonsCreated);
            json.WriteNumber("tabsCreated", TabsCreated);
            json.WriteNumber("optionsBuilds", OptionsBuilds);
            json.WriteNumber("toneHistogramBuilds", _properties.HistogramBuilds);
            json.WriteNumber("channelHistogramBuilds", ChannelHistogramBuilds);
            json.WriteNumber("thumbnailRenders", LayerThumbnail.RenderCount);
            json.WriteNumber("sceneRenders", Surface.SceneRenders);
            json.WriteNumber("overlayRenders", Surface.OverlayRenders);
            json.WriteString("version", ProductVersion);
            json.WriteString("name", document.Name);
            json.WriteString("tool", Surface.Tool.ToString());
            json.WriteNumber("documents", _documents.Count);
            json.WriteNumber("layers", document.Layers.Count);
            json.WriteNumber("width", document.Width);
            json.WriteNumber("height", document.Height);
            json.WriteNumber("history", Session.History.Count);
            json.WriteNumber("revision", Session.Revision);
            json.WriteBoolean("dirty", Session.IsDirty);
            json.WriteBoolean("transaction", Session.IsInTransaction);
            json.WriteBoolean("busy", _busy);
            json.WriteBoolean("dialog", _dialogOpen);
            json.WriteBoolean("canUndo", Session.CanUndo);
            json.WriteBoolean("canRedo", Session.CanRedo);
            json.WriteNumber("zoom", Surface.Zoom);
            json.WriteNumber("panX", Surface.Pan.X);
            json.WriteNumber("panY", Surface.Pan.Y);
            json.WriteString("activeLayer", layer?.Name);
            json.WriteString("activeKind", layer?.Kind.ToString());
            json.WriteString("activeAdjustment", layer?.Adjustment.ToString());
            json.WriteNumber("activeX", layer?.X ?? 0);
            json.WriteNumber("activeY", layer?.Y ?? 0);
            json.WriteNumber("activeOpacity", layer?.Opacity ?? 1);
            json.WriteBoolean("activeVisible", layer?.Visible ?? false);
            json.WriteBoolean("activeLocked", layer?.Locked ?? false);
            json.WriteBoolean("mask", layer?.Mask is not null);
            json.WriteBoolean("editMask", document.EditMask);
            json.WriteNumber("maskDensity", layer?.MaskDensity ?? 1);
            json.WriteNumber("maskFeather", layer?.MaskFeather ?? 0);
            json.WriteBoolean("maskEnabled", layer?.MaskEnabled ?? false);
            json.WriteNumber("maskRevision", layer?.Mask?.Revision ?? 0);
            json.WriteNumber("maskFilterBuilds", Surface.Renderer.MaskFilterBuilds);
            json.WriteNumber("maskSourceBuilds", Surface.Renderer.MaskSourceBuilds);
            json.WriteBoolean("maskLinked", layer?.MaskLinked ?? true);
            var maskMatrix = layer?.MaskDocumentTransform ?? System.Numerics.Matrix3x2.Identity;
            json.WriteNumber("maskX", maskMatrix.M31);
            json.WriteNumber("maskY", maskMatrix.M32);
            json.WriteNumber("maskWidth", layer?.Mask?.Width ?? 0);
            json.WriteNumber("maskHeight", layer?.Mask?.Height ?? 0);
            json.WriteNumber("maskM11", maskMatrix.M11);
            json.WriteNumber("maskM12", maskMatrix.M12);
            json.WriteNumber("maskM21", maskMatrix.M21);
            json.WriteNumber("maskM22", maskMatrix.M22);
            json.WriteNumber("selectionOutlineSegments", Surface.SelectionOutlineSegments);
            json.WriteNumber("selectionOutlineScratchBytes", Surface.SelectionOutlineScratchBytes);
            json.WriteString("maskPreview", Surface.MaskPreview.ToString());
            json.WriteBoolean("selection", document.Selection is not null);
            json.WriteNumber("curvePoints", layer?.Curves.Rgb.Points.Length ?? 0);
            if (_diagnosticCurve != layer?.Curves.Rgb)
            {
                _diagnosticCurve = layer?.Curves.Rgb;
                _diagnosticCurveMidpoint = _diagnosticCurve?.CreateLookup()[128] ?? 0;
            }
            json.WriteNumber("curveMidpoint", _diagnosticCurveMidpoint);
            json.WriteNumber("rgbGamma", layer?.Levels.Rgb.Gamma ?? 1);
            json.WriteNumber("redGamma", layer?.Levels.Red.Gamma ?? 1);
            json.WriteNumber("tileUploads", Surface.Renderer.TileUploads);
            json.WriteNumber("toneFilterBuilds", Surface.Renderer.ToneFilterBuilds);
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
