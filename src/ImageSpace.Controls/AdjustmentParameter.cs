using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
using Windows.System;

namespace ImageSpace.Controls;

/// <summary>Retained numeric field and original Skia slider, with explicit gesture ownership.</summary>
public sealed class AdjustmentParameter : UserControl
{
    private sealed class Track : SKCanvasElement
    {
        public Action<SKCanvas, Size>? Draw;
        protected override void RenderOverride(SKCanvas canvas, Size size) => Draw?.Invoke(canvas, size);
    }
    private readonly NumericField _number;
    private readonly Track _track = new() { Height = 20 };
    private double _value;
    private double? _before;
    private uint? _pointer;
    private bool _updating;
    public double Minimum
    {
        get;
    }
    public double Maximum
    {
        get;
    }
    public double Step
    {
        get;
    }
    public Func<bool>? TryBeginEdit
    {
        get; set;
    }
    public event Action<double>? ValueChanged;
    public event Action? EditCompleted;
    public event Action? EditCanceled;

    public double Value
    {
        get => _value;
        set
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            var next = Math.Clamp(value, Minimum, Maximum);
            if (_value != next)
            {
                _value = next;
                _track.Invalidate();
            }
            _updating = true;
            try
            {
                _number.Value = next;
            }
            finally { _updating = false; }
        }
    }

    public AdjustmentParameter(string name, double minimum, double maximum, double step, string format = "0.##")
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum <= minimum || !double.IsFinite(step) || step <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimum));
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        IsTabStop = true;
        _number = new NumericField(name, Math.Clamp(0, minimum, maximum), minimum, maximum, 252) { Step = step, Format = format };
        _number.HorizontalAlignment = HorizontalAlignment.Stretch;
        _number.Width = double.NaN;
        _track.Draw = Render;
        AutomationProperties.SetName(_track, name + " slider");
        AutomationProperties.SetName(this, name + " control");
        var body = new StackPanel { Spacing = 1 };
        body.Children.Add(_number);
        body.Children.Add(_track);
        Content = body;
        Value = Math.Clamp(0, minimum, maximum);
        _number.ValueChanged += value =>
        {
            if (_updating)
                return;
            if (!Start())
            {
                Value = _value;
                _number.ResetPendingEdit();
                return;
            }
            Change(value);
            Finish();
        };
        _track.PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(_track);
            if (!IsEnabled || point.Properties.IsRightButtonPressed || _before is not null)
                return;
            Focus(FocusState.Programmatic);
            if (!Start())
                return;
            _pointer = e.Pointer.PointerId;
            if (!_track.CapturePointer(e.Pointer))
            {
                CancelEdit();
                return;
            }
            Move(point.Position.X);
            e.Handled = true;
        };
        _track.PointerMoved += (_, e) =>
        {
            if (_before is null || _pointer != e.Pointer.PointerId)
                return;
            Move(e.GetCurrentPoint(_track).Position.X);
            e.Handled = true;
        };
        _track.PointerReleased += (_, e) =>
        {
            if (_before is null || _pointer != e.Pointer.PointerId)
                return;
            Move(e.GetCurrentPoint(_track).Position.X);
            Finish();
            _track.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        };
        _track.PointerCanceled += (_, _) => CancelEdit();
        _track.PointerCaptureLost += (_, _) => CancelEdit();
        Unloaded += (_, _) => CancelEdit();
        KeyDown += OnKeyDown;
    }

    public void ResetPendingEdit() => _number.ResetPendingEdit();
    private bool Start()
    {
        if (!IsEnabled || _before is not null || TryBeginEdit?.Invoke() == false)
            return false;
        _before = _value;
        return true;
    }
    private void Change(double value)
    {
        var next = Math.Clamp(value, Minimum, Maximum);
        if (next == _value)
            return;
        Value = next;
        ValueChanged?.Invoke(next);
    }
    private void Move(double x)
    {
        var normalized = Math.Clamp((x - 7) / Math.Max(1, _track.ActualWidth - 14), 0, 1);
        var value = Minimum + normalized * (Maximum - Minimum);
        Change(Minimum + Math.Round((value - Minimum) / Step) * Step);
    }
    private void Finish()
    {
        if (_before is null)
            return;
        _before = null;
        _pointer = null;
        EditCompleted?.Invoke();
    }
    public void CancelEdit()
    {
        if (_before is not { } value)
            return;
        _before = null;
        _pointer = null;
        Value = value;
        _track.ReleasePointerCaptures();
        EditCanceled?.Invoke();
    }
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox)
            return;
        if (e.Key == VirtualKey.Escape)
        {
            CancelEdit();
            e.Handled = true;
            return;
        }
        if (e.Key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or VirtualKey.Home or VirtualKey.End))
            return;
        e.Handled = true; // Never nudge the canvas layer while this control owns arrow input.
        if (!Start())
            return;
        var coarse = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        var next = e.Key switch
        {
            VirtualKey.Home => Minimum,
            VirtualKey.End => Maximum,
            VirtualKey.Left or VirtualKey.Down => _value - Step * (coarse ? 10 : 1),
            _ => _value + Step * (coarse ? 10 : 1)
        };
        Change(next);
        Finish();
    }
    private void Render(SKCanvas canvas, Size size)
    {
        canvas.Clear(SKColors.Transparent);
        var left = 7f;
        var right = Math.Max(left + 1, (float)size.Width - 7);
        var y = (float)size.Height / 2;
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(105, 105, 105), StrokeWidth = 2 };
        canvas.DrawLine(left, y, right, y, paint);
        if (Minimum < 0 && Maximum > 0)
        {
            var zero = left + (float)(-Minimum / (Maximum - Minimum)) * (right - left);
            canvas.DrawLine(zero, y - 4, zero, y + 4, paint);
        }
        var thumb = left + (float)((_value - Minimum) / (Maximum - Minimum)) * (right - left);
        paint.Color = IsEnabled ? new SKColor(206, 206, 206) : new SKColor(95, 95, 95);
        canvas.DrawCircle(thumb, y, 5, paint);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1;
        paint.Color = new SKColor(32, 32, 32);
        canvas.DrawCircle(thumb, y, 5, paint);
    }
}
