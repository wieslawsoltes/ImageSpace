using System.Numerics;
using ImageSpace.Core;

namespace ImageSpace.Imaging;

/// <summary>Allocation-free geometric/pixel hit testing. Type uses its editable bounds;
/// feathered mask edges are conservatively retained rather than evaluated through a render readback.</summary>
public static class LayerHitTesting
{
    public static Layer? Hit(ImageDocument document, Vector2 point, byte minimumAlpha = 8)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            return null;
        var layers = document.Layers;
        for (var i = layers.Count - 1; i >= 0; i--)
        {
            var layer = layers[i];
            if (!layer.Visible || layer.Opacity <= 0 || layer.Kind == LayerKind.Adjustment || Coverage(layer, point) * layer.Opacity * 255 < minimumAlpha)
                continue;
            if (layer.IsClipped)
            {
                var index = LayerClipping.FindBaseIndex(layers, i);
                if (index < 0)
                    continue;
                var basis = layers[index];
                if (!basis.Visible || Coverage(basis, point) * basis.Opacity * 255 < minimumAlpha)
                    continue;
            }
            return layer;
        }
        return null;
    }

    private static float Coverage(Layer layer, Vector2 point)
    {
        var local = layer.ToLocal(point);
        var width = layer.Pixels?.Width ?? layer.Width;
        var height = layer.Pixels?.Height ?? layer.Height;
        if (local.X < 0 || local.Y < 0 || local.X >= width || local.Y >= height)
            return 0;
        if (layer.Kind == LayerKind.Ellipse)
        {
            var x = 2 * local.X / width - 1;
            var y = 2 * local.Y / height - 1;
            if (x * x + y * y > 1)
                return 0;
        }
        if (layer.Kind == LayerKind.Rectangle && layer.CornerRadius > 0)
        {
            var radius = Math.Min(layer.CornerRadius, Math.Min(width, height) / 2);
            var nearest = Vector2.Clamp(local, new(radius), new(width - radius, height - radius));
            if (Vector2.DistanceSquared(local, nearest) > radius * radius)
                return 0;
        }
        var alpha = layer.Pixels is not null ? layer.Pixels.Get((int)local.X, (int)local.Y).A / 255f : layer.Color.A / 255f;
        if (layer.Mask is not null && layer.MaskEnabled && layer.MaskDensity > 0 && layer.MaskFeather == 0)
        {
            if (!Matrix3x2.Invert(layer.MaskDocumentTransform, out var inverse))
                return 0;
            var mask = Vector2.Transform(point, inverse);
            var coverage = mask.X < 0 || mask.Y < 0 || mask.X >= layer.Mask.Width || mask.Y >= layer.Mask.Height
                ? 0 : layer.Mask.Get((int)mask.X, (int)mask.Y).A / 255f;
            alpha *= 1 - layer.MaskDensity + layer.MaskDensity * coverage;
        }
        return alpha;
    }
}
