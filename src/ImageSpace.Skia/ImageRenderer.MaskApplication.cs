using ImageSpace.Core;
using SkiaSharp;

namespace ImageSpace.Skia;

public sealed partial class ImageRenderer
{
    /// <summary>
    /// Applies effective mask coverage to a raster layer's authored alpha, in source-pixel
    /// coordinates. Preserves RGB, source extent, transforms, blend mode and opacity.
    /// The caller owns the returned copy-on-write surface. No document is mutated.
    /// </summary>
    /// <remarks>
    /// This is an explicit destructive operation with a coverage readback, not a render
    /// fast path. Selection and canvas clipping are deliberately absent. A disabled mask
    /// must be enabled before applying; deleting it is the separate non-applying action.
    /// </remarks>
    public PixelSurface BakeLayerMask(Layer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (layer.Kind != LayerKind.Raster || layer.Pixels is not { } source || layer.Mask is null)
            throw new InvalidOperationException("Applying a mask requires a raster layer with source pixels and a mask.");
        if (!layer.MaskEnabled)
            throw new InvalidOperationException("Enable the mask before applying it, or delete it to discard its effect.");
        if (!float.IsFinite(layer.MaskDensity) || layer.MaskDensity is < 0 or > 1 ||
            !float.IsFinite(layer.MaskFeather) || layer.MaskFeather is < 0 or > 32)
            throw new InvalidDataException("Invalid mask density or feather.");
        layer.MaskPlacement.Validate();
        var result = source.Snapshot();
        if (layer.MaskDensity == 0)
            return result;

        // Use the authored surface, NOT document dimensions. Cropped/off-canvas pixels
        // remain editable and retain the original layer coordinate system.
        using var surface = CreateSurface(source.Width, source.Height);
        surface.Canvas.Clear(SKColors.Transparent);
        var bounds = new SKRect(0, 0, source.Width, source.Height);
        var filter = _masks.Get(layer, bounds, false, DrawTiles);
        using (var paint = new SKPaint { ImageFilter = filter })
            surface.Canvas.DrawPaint(paint);
        using var image = surface.Snapshot();
        var coverage = ReadPixels(image, source.Width, source.Height);

        // Only alpha is baked. Passing source RGB through premultiplication/readback
        // would destroy hidden colors and introduce avoidable RGB rounding errors.
        foreach (var (left, top, _, _) in source.EnumerateTiles())
        {
            var right = Math.Min(left + PixelSurface.TileSize, source.Width);
            var bottom = Math.Min(top + PixelSurface.TileSize, source.Height);
            for (var y = top; y < bottom; y++)
            for (var x = left; x < right; x++)
            {
                var pixel = source.Get(x, y);
                if (pixel.A == 0)
                    continue;
                var alpha = (byte)((pixel.A * coverage.Get(x, y).A + 127) / 255);
                if (alpha != pixel.A)
                    result.Set(x, y, new Rgba32(pixel.R, pixel.G, pixel.B, alpha));
            }
        }
        return result;
    }
}
