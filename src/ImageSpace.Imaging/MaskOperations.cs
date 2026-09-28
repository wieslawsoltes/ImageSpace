using ImageSpace.Core;

namespace ImageSpace.Imaging;

/// <summary>Authored mask coverage lives in alpha, never in the mask's RGB components.</summary>
public static class MaskOperations
{
    public static byte Luminance(Rgba32 color) => Rgba32.Byte(color.R * .2126 + color.G * .7152 + color.B * .0722);
    public static Rgba32 CoverageColor(byte coverage) => new(255, 255, 255, coverage);

    public static PixelSurface FromSelection(ImageDocument document, Layer layer)
    {
        var width = layer.Pixels?.Width ?? Dimension(layer.Width);
        var height = layer.Pixels?.Height ?? Dimension(layer.Height);
        var result = new PixelSurface(width, height);
        var mapping = new PixelMapping(document, layer.Transform);
        // Reveal All means the whole layer, including currently off-canvas content.
        if (document.Selection is null)
        {
            result.Fill(Rgba32.White);
            return result;
        }
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                result.Set(x, y, CoverageColor(Rgba32.Byte(mapping.Coverage(x, y) * 255)));
        return result;
    }

    private static int Dimension(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > PixelSurface.MaximumDimension)
            throw new InvalidOperationException("Layer dimensions exceed the editable mask limit.");
        return Math.Max(1, (int)Math.Ceiling(value));
    }

    /// <summary>Opaque grayscale lets RGB filters process mask alpha without dropping fully hidden pixels.</summary>
    public static PixelSurface ToGrayscale(PixelSurface mask)
    {
        var result = new PixelSurface(mask.Width, mask.Height);
        for (var y = 0; y < mask.Height; y++)
            for (var x = 0; x < mask.Width; x++)
            {
                var value = mask.Get(x, y).A;
                result.Set(x, y, new Rgba32(value, value, value));
            }
        return result;
    }

    public static PixelSurface FromGrayscale(PixelSurface image)
    {
        var result = new PixelSurface(image.Width, image.Height);
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
                result.Set(x, y, CoverageColor(Luminance(image.Get(x, y))));
        return result;
    }

    public static PixelSurface Invert(PixelSurface mask)
    {
        var result = new PixelSurface(mask.Width, mask.Height);
        for (var y = 0; y < mask.Height; y++)
            for (var x = 0; x < mask.Width; x++)
                result.Set(x, y, CoverageColor((byte)(255 - mask.Get(x, y).A)));
        return result;
    }
    public static PixelSurface ToDocumentSelection(ImageDocument document, Layer layer)
    {
        var mask = layer.Mask ?? throw new InvalidOperationException("This layer has no mask.");
        if (!System.Numerics.Matrix3x2.Invert(layer.MaskDocumentTransform, out var inverse))
            throw new InvalidOperationException("The layer transform cannot be inverted.");
        var selection = new PixelSurface(document.Width, document.Height);
        for (var y = 0; y < document.Height; y++)
            for (var x = 0; x < document.Width; x++)
            {
                var point = System.Numerics.Vector2.Transform(new(x + .5f, y + .5f), inverse);
                if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || point.X < 0 || point.Y < 0 ||
                    point.X >= mask.Width || point.Y >= mask.Height)
                    continue;
                selection.Set(x, y, CoverageColor(mask.Get((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y)).A));
            }
        return selection;
    }

}
