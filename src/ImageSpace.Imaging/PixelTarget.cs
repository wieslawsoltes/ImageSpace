using System.Numerics;
using ImageSpace.Core;

namespace ImageSpace.Imaging;

/// <summary>Resolves the authored surface independently of the layer's content type.</summary>
public static class PixelTarget
{
    public static PixelSurface? Get(ImageDocument document, Layer? layer) =>
        document.EditMask ? layer?.Mask : layer?.Kind == LayerKind.Raster ? layer.Pixels : null;

    public static PixelSurface RequireEditable(ImageDocument document, Layer layer)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);
        if (layer.Locked)
            throw new InvalidOperationException("Unlock the layer before editing it.");
        return Get(document, layer) ?? throw new InvalidOperationException(document.EditMask
            ? "Add a layer mask before editing it." : "Select a pixel layer or its mask before using pixel tools.");
    }

    public static PixelMapping Prepare(ImageDocument document, Layer layer) =>
        new(document, document.EditMask ? layer.MaskDocumentTransform : layer.Transform);

    public static Vector2 ToLocal(ImageDocument document, Layer layer, Vector2 point) =>
        Prepare(document, layer).ToLocal(point);

    /// <summary>Convenience sampler. Bulk operations should prepare one PixelMapping outside their loop.</summary>
    public static float Coverage(ImageDocument document, Layer layer, int x, int y) =>
        Prepare(document, layer).Coverage(x, y);

    public static void Replace(ImageDocument document, Layer layer, PixelSurface surface)
    {
        var current = RequireEditable(document, layer);
        if (current.Width != surface.Width || current.Height != surface.Height)
            throw new ArgumentException("The replacement must retain the authored surface dimensions.", nameof(surface));
        if (document.EditMask)
            layer.Mask = surface;
        else
            layer.Pixels = surface;
    }
}
