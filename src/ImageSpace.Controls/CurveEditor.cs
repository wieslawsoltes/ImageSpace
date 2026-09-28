using System.Collections.Immutable;
using ImageSpace.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
using Windows.System;

namespace ImageSpace.Controls;

/// <summary>Reusable editable tone graph with pointer capture, numeric editing and keyboard control.</summary>
public sealed class CurveEditor : UserControl
{
    private sealed class Graph : SKCanvasElement
    {
        public Action<SKCanvas, Size>? Draw;
        protected override void RenderOverride(SKCanvas canvas, Size area) => Draw?.Invoke(canvas, area);
    }
    private readonly Graph _graph = new();
    private ToneCurve _curve = ToneCurve.Identity;
    private ToneCurve? _before;
    private int _selected;
    private int[] _histogram = new int[256];
    private byte[] _lookup = ToneCurve.Identity.CreateLookup();
    public Func<bool>? TryBeginEdit { get; set; }
    public event Action<ToneCurve>? CurveChanged;
    public event Action? EditCompleted;
    public event Action? EditCanceled;
    public event Action<CurvePoint>? SelectedPointChanged;
    public SKColor CurveColor { get; set; } = new(221, 221, 221);
    public CurvePoint SelectedPoint => _curve.Points[_selected];
    public bool CanDeletePoint => _selected > 0 && _selected < _curve.Points.Length - 1;

    public ToneCurve Curve
    {
        get => _curve;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_curve == value) return;
            value.Validate();
            _curve = value;
            _lookup = value.CreateLookup();
            _selected = Math.Clamp(_selected, 0, value.Points.Length - 1);
            _graph.Invalidate();
            SelectedPointChanged?.Invoke(SelectedPoint);
        }
    }

    public CurveEditor()
    {
        Height = 176;
        MinWidth = 140;
        IsTabStop = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, "Tone curve graph");
        Content = _graph;
        _graph.Draw = Render;
        _graph.PointerPressed += Pressed;
        _graph.PointerMoved += Moved;
        _graph.PointerReleased += (_, e) =>
        {
            if (_before is null) return;
            _before = null;
            _graph.ReleasePointerCapture(e.Pointer);
            EditCompleted?.Invoke();
            e.Handled = true;
        };
        _graph.PointerCanceled += (_, _) => CancelEdit();
        _graph.PointerCaptureLost += (_, _) => CancelEdit();
        Unloaded += (_, _) => CancelEdit();
        KeyDown += KeyPressed;
    }

    public void SetHistogram(int[] histogram)
    {
        _histogram = histogram.Length == 256 ? (int[])histogram.Clone() : new int[256];
        _graph.Invalidate();
    }

    public void SetSelectedPoint(int input, int output)
    {
        var points = _curve.Points;
        input = _selected == 0 ? 0 : _selected == points.Length - 1 ? 255 :
            Math.Clamp(input, points[_selected - 1].Input + 1, points[_selected + 1].Input - 1);
        var point = new CurvePoint(input, Math.Clamp(output, 0, 255));
        if (point == SelectedPoint) return;
        Atomic(() => Publish(_curve with { Points = points.SetItem(_selected, point) }));
    }

    public void DeleteSelectedPoint()
    {
        if (!CanDeletePoint) return;
        Atomic(() =>
        {
            var points = _curve.Points.RemoveAt(_selected);
            _selected = Math.Min(_selected, points.Length - 1);
            Publish(_curve with { Points = points });
        });
    }

    public void CancelEdit()
    {
        if (_before is not { } original) return;
        _before = null;
        Curve = original;
        _graph.ReleasePointerCaptures();
        EditCanceled?.Invoke();
    }

    private void Atomic(Action edit)
    {
        if (!IsEnabled || _before is not null || TryBeginEdit?.Invoke() == false) return;
        _before = _curve;
        edit();
        _before = null;
        EditCompleted?.Invoke();
    }

    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(_graph);
        if (!IsEnabled || pointer.Properties.IsRightButtonPressed || _before is not null) return;
        Focus(FocusState.Programmatic);
        var rect = Plot(new Size(_graph.ActualWidth, _graph.ActualHeight));
        var p = new SKPoint((float)pointer.Position.X, (float)pointer.Position.Y);
        if (!rect.Contains(p)) return;
        var best = -1;
        var distance = 11f;
        for (var i = 0; i < _curve.Points.Length; i++)
        {
            var candidate = Position(_curve.Points[i], rect);
            var d = MathF.Sqrt(MathF.Pow(candidate.X - p.X, 2) + MathF.Pow(candidate.Y - p.Y, 2));
            if (d < distance) { best = i; distance = d; }
        }
        var input = Math.Clamp((int)Math.Round((p.X - rect.Left) / rect.Width * 255), 1, 254);
        if (best < 0 && (_curve.Points.Length >= ToneCurve.MaximumPoints || _curve.Points.Any(point => point.Input == input))) return;
        if (TryBeginEdit?.Invoke() == false) return;
        _before = _curve;
        if (best >= 0)
        {
            _selected = best;
        }
        else
        {
            var point = new CurvePoint(input, Math.Clamp((int)Math.Round((rect.Bottom - p.Y) / rect.Height * 255), 0, 255));
            var points = _curve.Points.Add(point).OrderBy(value => value.Input).ToImmutableArray();
            _selected = points.IndexOf(point);
            Publish(_curve with { Points = points });
        }
        SelectedPointChanged?.Invoke(SelectedPoint);
        _graph.CapturePointer(e.Pointer);
        _graph.Invalidate();
        e.Handled = true;
    }

    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_before is null) return;
        var p = e.GetCurrentPoint(_graph).Position;
        var rect = Plot(new Size(_graph.ActualWidth, _graph.ActualHeight));
        var input = (int)Math.Round((p.X - rect.Left) / rect.Width * 255);
        var output = Math.Clamp((int)Math.Round((rect.Bottom - p.Y) / rect.Height * 255), 0, 255);
        var points = _curve.Points;
        input = _selected == 0 ? 0 : _selected == points.Length - 1 ? 255 :
            Math.Clamp(input, points[_selected - 1].Input + 1, points[_selected + 1].Input - 1);
        var point = new CurvePoint(input, output);
        if (point != SelectedPoint) Publish(_curve with { Points = points.SetItem(_selected, point) });
        e.Handled = true;
    }

    private void KeyPressed(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { CancelEdit(); e.Handled = true; return; }
        if (e.Key is VirtualKey.Delete or VirtualKey.Back) { DeleteSelectedPoint(); e.Handled = true; return; }
        if (e.Key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down)) return;
        var shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        var step = shift ? 10 : 1;
        var p = SelectedPoint;
        SetSelectedPoint(p.Input + (e.Key == VirtualKey.Left ? -step : e.Key == VirtualKey.Right ? step : 0),
            p.Output + (e.Key == VirtualKey.Down ? -step : e.Key == VirtualKey.Up ? step : 0));
        e.Handled = true;
    }

    private void Publish(ToneCurve curve)
    {
        Curve = curve;
        CurveChanged?.Invoke(curve);
    }

    private static SKRect Plot(Size size) => new(12, 12, Math.Max(13, (float)size.Width - 12), Math.Max(13, (float)size.Height - 12));
    private static SKPoint Position(CurvePoint p, SKRect rect) => new(rect.Left + p.Input / 255f * rect.Width, rect.Bottom - p.Output / 255f * rect.Height);

    private void Render(SKCanvas canvas, Size size)
    {
        canvas.Clear(new SKColor(31, 31, 31));
        var rect = Plot(size);
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(72, 72, 72) };
        var maximum = Math.Max(1, _histogram.Max());
        using (var histogram = new SKPath())
        {
            histogram.MoveTo(rect.Left, rect.Bottom);
            for (var i = 0; i < 256; i++) histogram.LineTo(rect.Left + i / 255f * rect.Width,
                rect.Bottom - (float)Math.Sqrt((double)_histogram[i] / maximum) * rect.Height);
            histogram.LineTo(rect.Right, rect.Bottom);
            histogram.Close();
            canvas.DrawPath(histogram, paint);
        }
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1;
        paint.Color = new SKColor(91, 91, 91);
        canvas.DrawRect(rect, paint);
        for (var i = 1; i < 4; i++)
        {
            canvas.DrawLine(rect.Left + rect.Width * i / 4, rect.Top, rect.Left + rect.Width * i / 4, rect.Bottom, paint);
            canvas.DrawLine(rect.Left, rect.Top + rect.Height * i / 4, rect.Right, rect.Top + rect.Height * i / 4, paint);
        }
        using (var dash = SKPathEffect.CreateDash([3, 4], 0))
        {
            paint.PathEffect = dash;
            canvas.DrawLine(rect.Left, rect.Bottom, rect.Right, rect.Top, paint);
            paint.PathEffect = null;
        }
        using var curve = new SKPath();
        for (var i = 0; i < 256; i++)
        {
            var point = Position(new CurvePoint(i, _lookup[i]), rect);
            if (i == 0) curve.MoveTo(point); else curve.LineTo(point);
        }
        paint.Color = CurveColor;
        paint.StrokeWidth = 1.7f;
        canvas.DrawPath(curve, paint);
        for (var i = 0; i < _curve.Points.Length; i++)
        {
            var point = Position(_curve.Points[i], rect);
            paint.Style = SKPaintStyle.Fill;
            paint.Color = i == _selected ? new SKColor(109, 184, 255) : new SKColor(30, 30, 30);
            canvas.DrawCircle(point, 4, paint);
            paint.Style = SKPaintStyle.Stroke;
            paint.Color = CurveColor;
            paint.StrokeWidth = 1;
            canvas.DrawCircle(point, 4, paint);
        }
    }
}
