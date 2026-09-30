namespace ImageSpace.Core;

/// <summary>Render dependencies without COW snapshots. Selection/names/locks are excluded.</summary>
public readonly record struct LayerRenderStamp(
    Guid Id, LayerKind Kind, bool Visible, float Opacity, LayerBlend Blend,
    float X, float Y, float ScaleX, float ScaleY, float Rotation, float Width, float Height,
    Rgba32 Color, Rgba32 StrokeColor, float StrokeWidth, float CornerRadius,
    float FontSize, string Text, string FontFamily, bool Bold,
    PixelSurface? Pixels, long PixelRevision, PixelSurface? Mask, long MaskRevision,
    bool MaskEnabled, bool MaskLinked, float MaskDensity, float MaskFeather,
    AffinePlacement MaskPlacement, AdjustmentKind Adjustment, float Amount, float Secondary,
    CurvesAdjustment Curves, LevelsAdjustment Levels, bool IsClipped = false,
    ChannelMixerAdjustment? ChannelMixer = null, ExposureAdjustment? Exposure = null)
{
    public static LayerRenderStamp Capture(Layer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        return new(layer.Id, layer.Kind, layer.Visible, layer.Opacity, layer.Blend,
            layer.X, layer.Y, layer.ScaleX, layer.ScaleY, layer.Rotation, layer.Width, layer.Height,
            layer.Color, layer.StrokeColor, layer.StrokeWidth, layer.CornerRadius,
            layer.FontSize, layer.Text, layer.FontFamily, layer.Bold,
            layer.Pixels, layer.Pixels?.Revision ?? -1, layer.Mask, layer.Mask?.Revision ?? -1,
            layer.MaskEnabled, layer.MaskLinked, layer.MaskDensity, layer.MaskFeather,
            layer.MaskPlacement, layer.Adjustment, layer.Amount, layer.Secondary,
            layer.Curves, layer.Levels, layer.IsClipped,
            layer.Adjustment == AdjustmentKind.ChannelMixer ? layer.ChannelMixer : null,
            layer.Adjustment == AdjustmentKind.Exposure ? layer.Exposure : null);
    }
}
