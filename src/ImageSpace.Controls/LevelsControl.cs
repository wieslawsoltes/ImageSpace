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

/// <summary>Histogram with draggable input black/gamma/white and output endpoint handles.</summary>
public sealed class LevelsControl : UserControl
{
    private sealed class Graph : SKCanvasElement
    {
        public Action<SKCanvas, Size>? Draw;
        protected override void RenderOverride(SKCanvas canvas, Size size) => Draw?.Invoke(canvas, size);
    }
    private readonly Graph _graph = new();
    private LevelsChannel _value = new();
    private LevelsChannel? _before;
    private int _handle;
    private int[] _histogram = new int[256];
    public Func<bool>? TryBeginEdit { get; set; }
    public event Action<LevelsChannel>? ValueChanged;
    public event Action? EditCompleted;
    public event Action? EditCanceled;

    public LevelsChannel Value
    {
        get => _value;
        set { value.Validate(); _value = value; _graph.Invalidate(); }
    }

    public LevelsControl()
    {
        Height = 164;
        MinWidth = 140;
        IsTabStop = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(this, "Levels histogram controls");
        Content = _graph;
        _graph.Draw = Render;
        _graph.PointerPressed += Pressed;
        _graph.PointerMoved += (_, e) =>
        {
            if (_before is null) return;
            UpdateFromPointer(e.GetCurrentPoint(_graph).Position.X);
            e.Handled = true;
        };
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

    public void CancelEdit()
    {
        if (_before is not { } original) return;
        _before = null;
        Value = original;
        _graph.ReleasePointerCaptures();
        EditCanceled?.Invoke();
    }

    private static double Midpoint(LevelsChannel value) => value.InputBlack +
        (value.InputWhite - value.InputBlack) * Math.Pow(0.5, value.Gamma);
    private static SKRect Plot(Size size) => new(12, 9, Math.Max(13, (float)size.Width - 12), Math.Max(10, (float)size.Height - 51));

    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(_graph);
        if (!IsEnabled || pointer.Properties.IsRightButtonPressed || _before is not null) return;
        var p = pointer.Position;
        var rect = Plot(new Size(_graph.ActualWidth, _graph.ActualHeight));
        if (p.X < rect.Left - 6 || p.X > rect.Right + 6 || p.Y < rect.Bottom - 4) return;
        var output = p.Y > rect.Bottom + 22;
        double[] positions = output ? [_value.OutputBlack, _value.OutputWhite] : [_value.InputBlack, Midpoint(_value), _value.InputWhite];
        var domain = (p.X - rect.Left) / rect.Width * 255;
        var nearest = 0;
        for (var i = 1; i < positions.Length; i++)
            if (Math.Abs(positions[i] - domain) < Math.Abs(positions[nearest] - domain)) nearest = i;
        if (TryBeginEdit?.Invoke() == false) return;
        Focus(FocusState.Programmatic);
        _handle = output ? nearest + 3 : nearest;
        _before = _value;
        _graph.CapturePointer(e.Pointer);
        UpdateFromPointer(p.X);
        e.Handled = true;
    }

    private void UpdateFromPointer(double x)
    {
        var rect = Plot(new Size(_graph.ActualWidth, _graph.ActualHeight));
        var value = Math.Clamp((x - rect.Left) / rect.Width * 255, 0, 255);
        var next = _handle switch
        {
            0 => _value with { InputBlack = Math.Min(Math.Round(value), _value.InputWhite - 1) },
            1 => _value with { Gamma = Math.Clamp(Math.Log(Math.Clamp((value - _value.InputBlack) / (_value.InputWhite - _value.InputBlack), 0.000001, 0.999999)) / Math.Log(0.5), 0.1, 10) },
            2 => _value with { InputWhite = Math.Max(Math.Round(value), _value.InputBlack + 1) },
            3 => _value with { OutputBlack = Math.Round(value) },
            _ => _value with { OutputWhite = Math.Round(value) }
        };
        if (next == _value) return;
        Value = next;
        ValueChanged?.Invoke(next);
    }

    private void KeyPressed(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { CancelEdit(); e.Handled = true; return; }
        if (e.Key is VirtualKey.Up or VirtualKey.Down)
        {
            _handle = (_handle + (e.Key == VirtualKey.Down ? 1 : 4)) % 5;
            _graph.Invalidate();
            e.Handled = true;
            return;
        }
        if (e.Key is not (VirtualKey.Left or VirtualKey.Right) || _before is not null || TryBeginEdit?.Invoke() == false) return;
        _before = _value;
        var rect = Plot(new Size(_graph.ActualWidth, _graph.ActualHeight));
        var position = _handle switch { 0 => _value.InputBlack, 1 => Midpoint(_value), 2 => _value.InputWhite, 3 => _value.OutputBlack, _ => _value.OutputWhite };
        UpdateFromPointer(rect.Left + (position + (e.Key == VirtualKey.Left ? -1 : 1)) / 255 * rect.Width);
        _before = null;
        EditCompleted?.Invoke();
        e.Handled = true;
    }

    private void Render(SKCanvas canvas, Size size)
    {
        canvas.Clear(new SKColor(31, 31, 31));
        var rect = Plot(size);
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(151, 163, 177) };
        var maximum = Math.Max(1, _histogram.Max());
        for (var i = 0; i < 256; i++)
        {
            var height = (float)Math.Sqrt((double)_histogram[i] / maximum) * rect.Height;
            canvas.DrawRect(rect.Left + i / 256f * rect.Width, rect.Bottom - height, Math.Max(1, rect.Width / 256), height, paint);
        }
        paint.Style = SKPaintStyle.Stroke;
        paint.Color = new SKColor(90, 90, 90);
        canvas.DrawRect(rect, paint);
        for (var i = 0; i < 256; i++)
        {
            paint.Style = SKPaintStyle.Fill;
            paint.Color = new SKColor((byte)i, (byte)i, (byte)i);
            canvas.DrawRect(rect.Left + i / 256f * rect.Width, rect.Bottom + 26, Math.Max(1, rect.Width / 256), 8, paint);
        }
        double[] positions = [_value.InputBlack, Midpoint(_value), _value.InputWhite, _value.OutputBlack, _value.OutputWhite];
        for (var i = 0; i < positions.Length; i++)
        {
            var x = rect.Left + (float)positions[i] / 255 * rect.Width;
            var y = rect.Bottom + (i < 3 ? 1 : 35);
            using var triangle = new SKPath();
            triangle.MoveTo(x, y);
            triangle.LineTo(x - 5, y + 8);
            triangle.LineTo(x + 5, y + 8);
            triangle.Close();
            paint.Style = SKPaintStyle.Fill;
            paint.Color = i is 0 or 3 ? new SKColor(20, 20, 20) : i == 1 ? new SKColor(130, 130, 130) : SKColors.White;
            canvas.DrawPath(triangle, paint);
            paint.Style = SKPaintStyle.Stroke;
            paint.Color = i == _handle ? new SKColor(109, 184, 255) : new SKColor(183, 183, 183);
            canvas.DrawPath(triangle, paint);
        }
    }
}
