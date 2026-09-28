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
        if (layer.Locked) throw new InvalidOperationException("Unlock the layer before editing it.");
        return Get(document, layer) ?? throw new InvalidOperationException(document.EditMask
            ? "Add a layer mask before editing it." : "Select a pixel layer or its mask before using pixel tools.");
    }

    /// <summary>Sample selection at the transformed pixel CENTER; truncation is incorrect for negative coordinates.</summary>
    public static float Coverage(ImageDocument document, Layer layer, int x, int y)
    {
        var point = layer.ToDocument(new Vector2(x + 0.5f, y + 0.5f));
        // Compare before conversion to avoid integer overflow for extreme transformed coordinates.
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || point.X < 0 || point.Y < 0 ||
            point.X >= document.Width || point.Y >= document.Height) return 0;
        return document.Coverage((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y));
    }

    public static void Replace(ImageDocument document, Layer layer, PixelSurface surface)
    {
        var current = RequireEditable(document, layer);
        if (current.Width != surface.Width || current.Height != surface.Height)
            throw new ArgumentException("The replacement must retain the authored surface dimensions.", nameof(surface));
        if (document.EditMask) layer.Mask = surface;
        else layer.Pixels = surface;
    }
}
