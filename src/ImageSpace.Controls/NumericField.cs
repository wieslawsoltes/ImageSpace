using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace ImageSpace.Controls;

/// <summary>Compact numeric control with validation, inherited disabled state, stepping and wheel scrubbing.</summary>
public sealed class NumericField : UserControl
{
    private readonly TextBox _input;
    private double _value;
    private bool _updating;
    public double Minimum { get; set; } = double.MinValue;
    public double Maximum { get; set; } = double.MaxValue;
    public double Step { get; set; } = 1;
    public string Format { get; set; } = "0.##";
    public event Action<double>? ValueChanged;

    public double Value
    {
        get => _value;
        set
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            _value = Math.Clamp(value, Minimum, Maximum);
            Render();
        }
    }

    public NumericField(string label, double value, double minimum, double maximum, double width = 76)
    {
        Minimum = minimum;
        Maximum = maximum;
        Width = width;
        MinHeight = 27;
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
            }
        };
        var caption = Studio.Label(label, 11, "#a9a9a9");
        caption.Margin = new Thickness(0, 0, 5, 0);
        grid.Children.Add(caption);
        _input = Studio.TextInput("", label);
        _input.MinWidth = 35;
        _input.Padding = new Thickness(4, 3, 3, 3);
        _input.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(_input, 1);
        grid.Children.Add(_input);
        Content = grid;
        Value = value;
        _input.LostFocus += (_, _) => Commit();
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter) { Commit(); e.Handled = true; }
            else if (e.Key is VirtualKey.Up or VirtualKey.Down)
            {
                SetAndNotify(_value + (e.Key == VirtualKey.Up ? Step : -Step));
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape) { Render(); e.Handled = true; }
        };
        caption.PointerWheelChanged += (_, e) =>
        {
            if (!IsEnabled) return;
            SetAndNotify(_value + Math.Sign(e.GetCurrentPoint(caption).Properties.MouseWheelDelta) * Step);
            e.Handled = true;
        };
    }

    private void Commit()
    {
        if (_updating || !IsEnabled) return;
        if (double.TryParse(_input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
            SetAndNotify(value);
        else Render();
    }

    private void SetAndNotify(double value)
    {
        if (!IsEnabled || !double.IsFinite(value)) return;
        value = Math.Clamp(value, Minimum, Maximum);
        if (Math.Abs(value - _value) < .00001) { Render(); return; }
        _value = value;
        Render();
        ValueChanged?.Invoke(value);
    }

    private void Render()
    {
        _updating = true;
        _input.Text = _value.ToString(Format, CultureInfo.InvariantCulture);
        _updating = false;
    }
}
