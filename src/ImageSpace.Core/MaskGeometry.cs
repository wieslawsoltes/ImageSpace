using System.Numerics;

namespace ImageSpace.Core;

/// <summary>Geometry-only mask operations. All placements use row-vector affine composition.</summary>
public static class MaskGeometry
{
    public static void SetLinked(Layer layer, bool linked)
    {
        ArgumentNullException.ThrowIfNull(layer);
        if (layer.Mask is null)
            throw new InvalidOperationException("This layer has no mask.");
        if (layer.MaskLinked == linked)
            return;
        var world = layer.MaskDocumentTransform;
        var placement = world;
        if (linked)
        {
            if (!Matrix3x2.Invert(layer.Transform, out var inverse))
                throw new InvalidOperationException("The layer transform cannot be inverted.");
            placement *= inverse;
        }
        var validated = AffinePlacement.FromMatrix(placement);
        layer.MaskPlacement = validated;
        layer.MaskLinked = linked;
    }

    /// <summary>Used by document-wide canvas operations; unlinked masks must follow the document too.</summary>
    public static void TransformUnlinked(Layer layer, Matrix3x2 documentDelta)
    {
        if (layer.Mask is not null && !layer.MaskLinked)
            layer.MaskPlacement = AffinePlacement.FromMatrix(layer.MaskPlacement.Matrix * documentDelta);
    }

    /// <summary>Resize an arbitrary affine frame around the opposite handle, retaining shear/reflections.</summary>
    public static Matrix3x2 Resize(Matrix3x2 original, Vector2 dimensions, Vector2 handle,
        Vector2 documentPoint, bool uniform)
    {
        if (dimensions.X <= 0 || dimensions.Y <= 0 || !Matrix3x2.Invert(original, out var inverse))
            throw new ArgumentException("The resize frame must be invertible and nonempty.");
        var local = Vector2.Transform(documentPoint, inverse);
        var opposite = Vector2.One - handle;
        var fixedLocal = opposite * dimensions;
        var fixedWorld = Vector2.Transform(fixedLocal, original);
        var sx = handle.X == .5f ? 1 : (local.X - fixedLocal.X) / ((handle.X - opposite.X) * dimensions.X);
        var sy = handle.Y == .5f ? 1 : (local.Y - fixedLocal.Y) / ((handle.Y - opposite.Y) * dimensions.Y);
        sx = Math.Clamp(sx, .01f, 100);
        sy = Math.Clamp(sy, .01f, 100);
        if (uniform)
        {
            var factor = handle.X == .5f ? sy : handle.Y == .5f ? sx : Math.Max(sx, sy);
            sx = sy = factor;
        }
        var next = Matrix3x2.CreateScale(sx, sy) * original;
        next *= Matrix3x2.CreateTranslation(fixedWorld - Vector2.Transform(fixedLocal, next));
        return AffinePlacement.FromMatrix(next).Matrix;
    }

    public static Matrix3x2 Rotate(Matrix3x2 original, Vector2 dimensions, Vector2 start,
        Vector2 documentPoint, bool snap)
    {
        var center = Vector2.Transform(dimensions / 2, original);
        if (Vector2.DistanceSquared(start, center) < 1e-10f ||
            Vector2.DistanceSquared(documentPoint, center) < 1e-10f)
            return original;
        var delta = MathF.Atan2(documentPoint.Y - center.Y, documentPoint.X - center.X) -
            MathF.Atan2(start.Y - center.Y, start.X - center.X);
        if (snap)
        {
            var angle = MathF.Atan2(original.M12, original.M11);
            const float step = MathF.PI / 12;
            delta = MathF.Round((angle + delta) / step) * step - angle;
        }
        return AffinePlacement.FromMatrix(original * Matrix3x2.CreateRotation(delta, center)).Matrix;
    }
}
