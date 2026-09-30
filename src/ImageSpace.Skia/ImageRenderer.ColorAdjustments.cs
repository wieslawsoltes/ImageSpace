namespace ImageSpace.Skia;

public sealed partial class ImageRenderer
{
    /// <summary>Cumulative matrix/LUT construction count, not GPU time or pixel uploads.</summary>
    public long ColorAdjustmentFilterBuilds => _adjustments.ColorAdjustmentFilterBuilds;
    public int CachedColorAdjustments => _adjustments.CachedColorAdjustments;
}
