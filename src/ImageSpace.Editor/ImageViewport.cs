using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Windows.Foundation;
using Windows.System;
using ImageSpace.Core;
using ImageSpace.Editing;
using ImageSpace.Imaging;
using ImageSpace.Skia;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
namespace ImageSpace.Editor;

public sealed partial class ImageViewport : UserControl, IDisposable
{
    private sealed class DrawingCanvas : SKCanvasElement
    {
        public Action<SKCanvas, Size>? Draw; protected override void RenderOverride(SKCanvas canvas, Size area) => Draw?.Invoke(canvas, area);
    }
    private readonly DrawingCanvas _imageCanvas = new() { IsHitTestVisible = false };
    private readonly DrawingCanvas _canvas = new(); private readonly BrushEngine _brush = new(); private EditorSession _session;
    private EditorTool _tool; private string _gesture = ""; private string _pendingTransform = ""; private Vector2 _start, _last, _startPan, _cursor; private Layer? _original; private int _handle = -1; private readonly List<Vector2> _lasso = []; private Vector2? _cloneSource; private (Vector2 A, Vector2 B)? _crop; private bool _fitPending = true;
    private readonly DispatcherTimer _ants = new() { Interval = TimeSpan.FromMilliseconds(140) }; private float _dashOffset;
    public ImageRenderer Renderer { get; } = new();
    public EditorSession Session
    {
        get => _session; set
        {
            CancelGesture();
            _session.Changed -= Changed;
            _session = value;
            _maskPreview = MaskPreviewMode.Composite;
            _previewLayerId = Guid.Empty;
            _session.Changed += Changed;
            _fitPending = true;
            Invalidate();
        }
    }
    public EditorTool Tool
    {
        get => _tool; set
        {
            if (_tool == value && _gesture == "") return;
            CancelGesture();
            _tool = value;
            InvalidateOverlay();
        }
    }
    public BrushSettings Brush { get; set; } = new(); public new Rgba32 Foreground { get; set; } = new(54, 145, 230); public Rgba32 BackgroundColor { get; set; } = Rgba32.White;
    public int Tolerance { get; set; } = 32; public bool IsSpaceDown
    {
        get; set;
    }
    public bool ShowRulers { get; set; } = true; public bool ShowGrid
    {
        get; set;
    }
    public bool ShowTransform { get; set; } = true;
    public float Zoom { get; private set; } = 1; public Vector2 Pan
    {
        get; private set;
    }
    public event Action<string>? Status; public event Action<Rgba32>? ColorPicked; public event Action<Layer>? TextEditRequested; public event Action? ViewChanged;
    public ImageViewport(EditorSession session)
    {
        _session = session;
        _session.Changed += Changed;
        IsTabStop = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        var host = new Grid();
        host.Children.Add(_imageCanvas);
        host.Children.Add(_canvas);
        Content = host;
        AutomationProperties.SetName(this, "Image canvas");
        _imageCanvas.Draw = RenderImage;
        _canvas.Draw = RenderOverlay;
        _canvas.PointerPressed += Pressed;
        _canvas.PointerMoved += Moved;
        _canvas.PointerReleased += Released;
        _canvas.PointerWheelChanged += Wheel;
        _canvas.PointerCanceled += (_, _) => CancelGesture();
        _canvas.PointerCaptureLost += (_, _) => { if (_gesture != "") CancelGesture(); };
        _canvas.DoubleTapped += (_, e) => { if (!_session.Document.EditMask && _session.Document.ActiveLayer is { Kind: LayerKind.Text } layer) TextEditRequested?.Invoke(layer); e.Handled = true; };
        SizeChanged += (_, _) => { if (_fitPending && ActualWidth > 50 && ActualHeight > 50) Fit(); Invalidate(); };
        _ants.Tick += (_, _) => { if (_session.Document.Selection is not null || _gesture == "selection") { _dashOffset += 1; InvalidateOverlay(); } };
        Loaded += (_, _) => _ants.Start();
        Unloaded += (_, _) => _ants.Stop();
    }
    private void Changed(object? sender, EventArgs e)
    {
        if (e is EditorChangedEventArgs { Change: EditorChange.SavedState }) return;
        var previousPreview = _maskPreview;
        if (!_session.Document.EditMask || _session.Document.ActiveLayer?.Mask is null ||
            _previewLayerId != _session.Document.ActiveLayerId)
        {
            _maskPreview = MaskPreviewMode.Composite;
            _previewLayerId = _session.Document.ActiveLayerId;
        }
        if (e is EditorChangedEventArgs { Change: EditorChange.ActiveTarget } && previousPreview == MaskPreviewMode.Composite)
            InvalidateOverlay();
        else Invalidate();
    }
    /// <summary>Invalidate scene and interaction drawings for actual content/view changes.</summary>
    public void Invalidate() { _imageCanvas.Invalidate(); _canvas.Invalidate(); }
    /// <summary>Cursor, selection ants and handles do not re-submit the image compositor.</summary>
    public void InvalidateOverlay() => _canvas.Invalidate();
    public Vector2 ToDocument(Vector2 p) => (p - Pan) / Zoom;
    public Vector2 ToScreen(Vector2 p) => p * Zoom + Pan;
    public void Fit()
    {
        if (ActualWidth < 50 || ActualHeight < 50)
            return;
        Zoom = Math.Clamp(Math.Min(((float)ActualWidth - 100) / _session.Document.Width, ((float)ActualHeight - 100) / _session.Document.Height), .02f, 32);
        Pan = new(((float)ActualWidth - _session.Document.Width * Zoom) / 2, ((float)ActualHeight - _session.Document.Height * Zoom) / 2);
        _fitPending = false;
        Invalidate();
        ViewChanged?.Invoke();
    }
    public void SetZoom(float zoom, Vector2? anchor = null)
    {
        var p = anchor ?? new Vector2((float)ActualWidth / 2, (float)ActualHeight / 2);
        var world = ToDocument(p);
        Zoom = Math.Clamp(zoom, .02f, 32);
        Pan = p - world * Zoom;
        Invalidate();
        ViewChanged?.Invoke();
    }
    private static Vector2 Point(Point p) => new((float)p.X, (float)p.Y);
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        Focus(FocusState.Programmatic);
        var point = e.GetCurrentPoint(_canvas);
        _cursor = Point(point.Position);
        _start = ToDocument(_cursor);
        _last = _start;
        _startPan = Pan;
        var doc = _session.Document;
        var layer = doc.ActiveLayer;
        if (point.Properties.IsRightButtonPressed)
            return;
        if (IsSpaceDown || Tool == EditorTool.Hand || point.Properties.IsMiddleButtonPressed)
        {
            _gesture = "pan";
            _start = _cursor;
            _canvas.CapturePointer(e.Pointer);
            e.Handled = true;
            return;
        }
        if (Tool == EditorTool.Zoom)
        {
            SetZoom(Zoom * ((e.KeyModifiers & VirtualKeyModifiers.Menu) != 0 ? .8f : 1.25f), _cursor);
            e.Handled = true;
            return;
        }
        if (Tool == EditorTool.Eyedropper)
        {
            var pixels = Renderer.Rasterize(doc);
            Foreground = pixels.Get((int)_start.X, (int)_start.Y);
            ColorPicked?.Invoke(Foreground);
            e.Handled = true;
            return;
        }
        if (Tool == EditorTool.Clone && (e.KeyModifiers & VirtualKeyModifiers.Menu) != 0)
        {
            _cloneSource = _start;
            Status?.Invoke("Clone source set. Paint to copy pixels.");
            e.Handled = true;
            return;
        }
        try
        {
            if (Tool == EditorTool.Move)
            {
                if (layer is not null && ShowTransform && (layer.Kind != LayerKind.Adjustment || doc.EditMask))
                    _handle = HitHandle(layer, _cursor);
                if (_handle < 0 && !doc.EditMask)
                {
                    layer = HitLayer(_start);
                    if (layer is null)
                        return;
                    _session.SelectLayer(layer.Id);
                }
                if (layer is null || layer.Locked)
                    return;
                // A click selects; only a real drag takes COW history snapshots.
                // This is geometry-only read state, not an editable pixel clone.
                _original = new Layer
                {
                    X = layer.X, Y = layer.Y, ScaleX = layer.ScaleX, ScaleY = layer.ScaleY,
                    Rotation = layer.Rotation, Width = layer.Width, Height = layer.Height,
                    Kind = layer.Kind, Mask = layer.Mask, MaskLinked = layer.MaskLinked, MaskPlacement = layer.MaskPlacement
                };
                var targetName = IsIndependentMask(layer) ? "mask" : "layer";
                _pendingTransform = (_handle == 8 ? "Rotate " : _handle >= 0 ? "Transform " : "Move ") + targetName;
                _gesture = _handle == 8 ? "rotate" : _handle >= 0 ? "resize" : "move";
            }
            else if (Tool is EditorTool.Marquee or EditorTool.EllipseSelect or EditorTool.Lasso)
            {
                _gesture = "selection";
                _lasso.Clear();
                _lasso.Add(_start);
            }
            else if (Tool == EditorTool.Crop)
            {
                _gesture = "crop";
                _crop = (_start, _start);
            }
            else if (Tool is EditorTool.Rectangle or EditorTool.Ellipse or EditorTool.Text)
            {
                _session.Begin(Tool == EditorTool.Text ? "New text layer" : "New shape layer");
                layer = new()
                {
                    Kind = Tool == EditorTool.Text ? LayerKind.Text : Tool == EditorTool.Rectangle ? LayerKind.Rectangle : LayerKind.Ellipse,
                    Name = Tool == EditorTool.Text ? "Text" : Tool.ToString(),
                    Color = Foreground,
                    X = _start.X,
                    Y = _start.Y,
                    Width = Tool == EditorTool.Text ? 360 : 1,
                    Height = Tool == EditorTool.Text ? 90 : 1,
                    FontSize = 56
                };
                doc.Layers.Add(layer);
                doc.ActiveLayerId = layer.Id;
                doc.EditMask = false;
                _gesture = "shape";
            }
            else
            {
                if (Tool == EditorTool.Wand)
                {
                    var composite = Renderer.Rasterize(doc);
                    var selection = Selections.Contiguous(composite, (int)MathF.Floor(_start.X), (int)MathF.Floor(_start.Y), Tolerance);
                    _session.Execute("Magic wand", d => d.Selection = Selections.Combine(d.Selection, selection, Combine(e.KeyModifiers)));
                    e.Handled = true;
                    return;
                }
                if (layer is null)
                    throw new InvalidOperationException("Select a pixel layer or its mask.");
                var editableSurface = PixelTarget.RequireEditable(doc, layer);
                if (Tool == EditorTool.Fill)
                {
                    var local = PixelTarget.ToLocal(doc, layer, _start);
                    var sampled = doc.EditMask ? MaskOperations.ToGrayscale(editableSurface) : editableSurface;
                    var region = Selections.Contiguous(sampled, (int)MathF.Floor(local.X), (int)MathF.Floor(local.Y), Tolerance);
                    _session.Execute("Paint bucket", d => PixelEdits.Fill(d, layer, Foreground, region));
                    e.Handled = true;
                    return;
                }
                _session.Begin(Tool == EditorTool.Gradient ? "Gradient" : Tool.ToString());
                _gesture = Tool == EditorTool.Gradient ? "gradient" : "paint";
                if (_gesture == "paint")
                {
                    if (Tool == EditorTool.Clone && _cloneSource is null)
                        throw new InvalidOperationException("Alt-click to set the clone source first.");
                    var target = PixelTarget.RequireEditable(doc, layer);
                    var offset = _cloneSource is null ? Vector2.Zero : PixelTarget.ToLocal(doc, layer, _cloneSource.Value) - PixelTarget.ToLocal(doc, layer, _start);
                    _brush.Begin(target, offset);
                    Paint(point);
                }
            }
            _canvas.CapturePointer(e.Pointer);
            e.Handled = true;
            if (_session.IsInTransaction) Invalidate(); else InvalidateOverlay();
        }
        catch (Exception ex) { CancelGesture(); Status?.Invoke(ex.Message); }
    }
    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_canvas);
        _cursor = Point(p.Position);
        var world = ToDocument(_cursor);
        _last = world;
        var layer = _session.Document.ActiveLayer;
        if (_pendingTransform.Length != 0)
        {
            if (Vector2.Distance(world, _start) * Zoom < 1) { InvalidateOverlay(); return; }
            try { _session.Begin(_pendingTransform); _pendingTransform = ""; }
            catch (Exception error) { CancelGesture(); Status?.Invoke(error.Message); return; }
        }
        if (_gesture == "pan")
        {
            Pan = _startPan + _cursor - _start;
            ViewChanged?.Invoke();
        }
        else if (layer is not null && TransformIndependentMask(layer, world,
            (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0))
        {
            // The mask frame was updated without changing layer content or resampling pixels.
        }
        else if (_gesture == "move" && layer is not null && _original is not null)
        {
            var delta = world - _start;
            if ((e.KeyModifiers & VirtualKeyModifiers.Shift) != 0)
            {
                if (Math.Abs(delta.X) > Math.Abs(delta.Y))
                    delta.Y = 0;
                else
                    delta.X = 0;
            }
            layer.X = _original.X + delta.X;
            layer.Y = _original.Y + delta.Y;
        }
        else if (_gesture == "resize" && layer is not null && _original is not null)
            Resize(layer, world, (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0);
        else if (_gesture == "rotate" && layer is not null && _original is not null)
        {
            var center = _original.ToDocument(new(_original.Width / 2, _original.Height / 2));
            var delta = (MathF.Atan2(world.Y - center.Y, world.X - center.X) - MathF.Atan2(_start.Y - center.Y, _start.X - center.X)) * 180 / MathF.PI;
            layer.Rotation = _original.Rotation + delta;
            if ((e.KeyModifiers & VirtualKeyModifiers.Shift) != 0)
                layer.Rotation = MathF.Round(layer.Rotation / 15) * 15;
            var newCenter = layer.ToDocument(new(layer.Width / 2, layer.Height / 2));
            layer.X += center.X - newCenter.X;
            layer.Y += center.Y - newCenter.Y;
        }
        else if (_gesture == "paint")
            Paint(p);
        else if (_gesture == "shape" && layer is not null && layer.Kind != LayerKind.Text)
        {
            var size = world - _start;
            if ((e.KeyModifiers & VirtualKeyModifiers.Shift) != 0)
            {
                var extent = Math.Max(Math.Abs(size.X), Math.Abs(size.Y));
                size = new(Math.Sign(size.X) * extent, Math.Sign(size.Y) * extent);
            }
            layer.X = Math.Min(_start.X, _start.X + size.X);
            layer.Y = Math.Min(_start.Y, _start.Y + size.Y);
            layer.Width = Math.Max(1, Math.Abs(size.X));
            layer.Height = Math.Max(1, Math.Abs(size.Y));
        }
        else if (_gesture == "selection" && Tool == EditorTool.Lasso)
        {
            if (_lasso.Count == 0 || Vector2.Distance(_lasso[^1], world) > 1 / Zoom)
                _lasso.Add(world);
        }
        else if (_gesture == "crop")
            _crop = (_start, world);
        if (_gesture is "pan" or "move" or "resize" or "rotate" or "paint" or "shape") Invalidate();
        else InvalidateOverlay();
        if (_gesture != "") e.Handled = true;
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (_gesture == "")
            return;
        var gesture = _gesture;
        var layer = _session.Document.ActiveLayer;
        try
        {
            if (gesture == "selection")
            {
                var d = _session.Document;
                var selection = Tool == EditorTool.Lasso ? Selections.Polygon(d.Width, d.Height, _lasso) : Selections.Rectangle(d.Width, d.Height, _start, _last, Tool == EditorTool.EllipseSelect);
                _session.Execute("Selection", doc => doc.Selection = Selections.Combine(doc.Selection, selection, Combine(e.KeyModifiers)));
            }
            else if (gesture == "gradient" && layer is not null)
            {
                PixelEdits.Gradient(_session.Document, layer, _start, _last, Foreground, BackgroundColor);
            }
            if (_session.IsInTransaction)
                _session.Commit();
            if (gesture == "crop")
                Status?.Invoke("Crop preview. Press Enter to apply, or Escape to cancel.");
        }
        catch (Exception ex) { _session.Cancel(); Status?.Invoke(ex.Message); }
        _gesture = "";
        _pendingTransform = "";
        _original = null;
        _handle = -1;
        _brush.End();
        _canvas.ReleasePointerCapture(e.Pointer);
        InvalidateOverlay();
        e.Handled = true;
        if (gesture == "shape" && layer?.Kind == LayerKind.Text)
            TextEditRequested?.Invoke(layer);
    }
    private void Paint(Microsoft.UI.Input.PointerPoint p)
    {
        var layer = _session.Document.ActiveLayer;
        if (layer is null)
            return;
        var pressure = p.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Mouse ? 1 : p.Properties.Pressure;
        var mode = Tool switch
        {
            EditorTool.Pencil => PaintMode.Pencil,
            EditorTool.Eraser => PaintMode.Eraser,
            EditorTool.Clone => PaintMode.Clone,
            EditorTool.Dodge => PaintMode.Dodge,
            EditorTool.Burn => PaintMode.Burn,
            EditorTool.Smudge => PaintMode.Smudge,
            _ => PaintMode.Brush
        };
        _brush.Paint(_session.Document, layer, ToDocument(Point(p.Position)), pressure, Brush, Foreground, mode);
    }
    private void Wheel(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(_canvas);
        SetZoom(Zoom * MathF.Pow(1.0015f, p.Properties.MouseWheelDelta), Point(p.Position));
        e.Handled = true;
    }
    public void ApplyCrop()
    {
        if (_crop is not { } crop)
            return;
        var x = Math.Clamp((int)Math.Min(crop.A.X, crop.B.X), 0, _session.Document.Width - 1);
        var y = Math.Clamp((int)Math.Min(crop.A.Y, crop.B.Y), 0, _session.Document.Height - 1);
        var right = Math.Clamp((int)Math.Max(crop.A.X, crop.B.X), x + 1, _session.Document.Width);
        var bottom = Math.Clamp((int)Math.Max(crop.A.Y, crop.B.Y), y + 1, _session.Document.Height);
        _session.Crop(x, y, right - x, bottom - y);
        _crop = null;
        Fit();
    }
    public void CancelGesture()
    {
        _gesture = "";
        _pendingTransform = "";
        _original = null;
        _handle = -1;
        _crop = null;
        _brush.End();
        _session.Cancel();
        _canvas.ReleasePointerCaptures();
        InvalidateOverlay();
    }
    private static SelectionCombine Combine(VirtualKeyModifiers keys) => (keys & VirtualKeyModifiers.Shift) != 0 ? ((keys & VirtualKeyModifiers.Menu) != 0 ? SelectionCombine.Intersect : SelectionCombine.Add) : (keys & VirtualKeyModifiers.Menu) != 0 ? SelectionCombine.Subtract : SelectionCombine.Replace;
    private Layer? HitLayer(Vector2 world)
    {
        foreach (var layer in _session.Document.Layers.AsEnumerable().Reverse())
        {
            if (!layer.Visible || layer.Kind == LayerKind.Adjustment)
                continue;
            var local = layer.ToLocal(world);
            if (local.X < 0 || local.Y < 0 || local.X > layer.Width || local.Y > layer.Height)
                continue;
            if (layer.Pixels is not null && layer.Pixels.Get((int)local.X, (int)local.Y).A < 8)
                continue;
            return layer;
        }
        return null;
    }
    private static readonly Vector2[] Handles = [new(0, 0), new(.5f, 0), new(1, 0), new(1, .5f), new(1, 1), new(.5f, 1), new(0, 1), new(0, .5f)];
    private int HitHandle(Layer layer, Vector2 screen)
    {
        for (var i = 0; i < 8; i++)
            if (Vector2.Distance(ToScreen(FrameToDocument(layer, Handles[i] * FrameDimensions(layer))), screen) < 8)
                return i;
        var rotation = ToScreen(FrameToDocument(layer, new(FrameDimensions(layer).X / 2, 0))) + new Vector2(0, -28);
        return Vector2.Distance(rotation, screen) < 8 ? 8 : -1;
    }
    private void Resize(Layer layer, Vector2 world, bool uniform)
    {
        var o = _original!;
        var local = o.ToLocal(world);
        var direction = Handles[_handle];
        var opposite = Vector2.One - direction;
        var fixedLocal = opposite * new Vector2(o.Width, o.Height);
        var fixedWorld = o.ToDocument(fixedLocal);
        var sx = 1f;
        var sy = 1f;
        if (direction.X != .5f)
            sx = (local.X - fixedLocal.X) / ((direction.X - opposite.X) * Math.Max(1, o.Width));
        if (direction.Y != .5f)
            sy = (local.Y - fixedLocal.Y) / ((direction.Y - opposite.Y) * Math.Max(1, o.Height));
        sx = Math.Clamp(sx, .01f, 100);
        sy = Math.Clamp(sy, .01f, 100);
        if (uniform)
        {
            var scale = Math.Max(sx, sy);
            sx = scale;
            sy = scale;
        }
        layer.ScaleX = o.ScaleX * sx;
        layer.ScaleY = o.ScaleY * sy;
        var transformed = layer.ToDocument(fixedLocal);
        layer.X += fixedWorld.X - transformed.X;
        layer.Y += fixedWorld.Y - transformed.Y;
    }
    public new void Dispose()
    {
        _ants.Stop();
        _session.Changed -= Changed;
        _maskPreviewRenderer.Dispose();
        Renderer.Dispose();
        _selectionPath?.Dispose();
    }
}
