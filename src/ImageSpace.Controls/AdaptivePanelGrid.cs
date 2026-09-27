using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ImageSpace.Controls;

/// <summary>
/// Three-panel inspector layout that preserves useful space for the bottom panel
/// instead of allowing fixed-height inspectors to consume a short window.
/// </summary>
public sealed class AdaptivePanelGrid : Grid
{
    public double PreferredTopHeight { get; set; } = 192;
    public double PreferredMiddleHeight { get; set; } = 224;
    public double MinimumBottomHeight { get; set; } = 220;
    public event Action<double>? TopHeightChanged;

    public AdaptivePanelGrid()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(PreferredTopHeight) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(PreferredMiddleHeight) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        SizeChanged += (_, _) => UpdateLayoutAllocation();
    }

    public void UpdateLayoutAllocation()
    {
        if (ActualHeight <= 0)
        {
            return;
        }

        var bottom = Math.Min(MinimumBottomHeight, ActualHeight * 0.48);
        var budget = Math.Max(0, ActualHeight - bottom);
        var factor = Math.Clamp(budget / Math.Max(1, PreferredTopHeight + PreferredMiddleHeight), 0, 1);
        var top = Math.Floor(PreferredTopHeight * factor);
        var middle = Math.Floor(PreferredMiddleHeight * factor);
        RowDefinitions[0].Height = new GridLength(top);
        RowDefinitions[1].Height = new GridLength(middle);
        TopHeightChanged?.Invoke(top);
    }
}
