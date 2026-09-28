using System.Numerics;
using ImageSpace.Core;

namespace ImageSpace.Imaging;

/// <summary>Selection-aware operations shared by mouse tools and editor commands.</summary>
public static class PixelEdits
{
    public static void Fill(ImageDocument document, Layer layer, Rgba32 color, PixelSurface? region = null)
    {
        var target = PixelTarget.RequireEditable(document, layer);
        if (region is not null && (region.Width != target.Width || region.Height != target.Height))
            throw new ArgumentException("Flood-fill coverage must match the editable surface.", nameof(region));
        var luminance = MaskOperations.Luminance(color);
        for (var y = 0; y < target.Height; y++)
            for (var x = 0; x < target.Width; x++)
            {
                var coverage = PixelTarget.Coverage(document, layer, x, y) * (region?.Get(x, y).A / 255f ?? 1);
                if (coverage <= 0) continue;
                var old = target.Get(x, y);
                target.Set(x, y, document.EditMask
                    ? MaskOperations.CoverageColor(Rgba32.Byte(old.A + (luminance - old.A) * coverage * color.A / 255f))
                    : Rgba32.Over(old, color, coverage));
            }
    }

    public static void Gradient(ImageDocument document, Layer layer, Vector2 from, Vector2 to,
        Rgba32 foreground, Rgba32 background)
    {
        var target = PixelTarget.RequireEditable(document, layer);
        var delta = to - from;
        var lengthSquared = delta.LengthSquared();
        if (!float.IsFinite(from.X) || !float.IsFinite(from.Y) || !float.IsFinite(lengthSquared))
            throw new ArgumentException("Gradient points must be finite.");
        if (lengthSquared < .0001f) return;
        for (var y = 0; y < target.Height; y++)
            for (var x = 0; x < target.Width; x++)
            {
                var coverage = PixelTarget.Coverage(document, layer, x, y);
                if (coverage <= 0) continue;
                var point = layer.ToDocument(new Vector2(x + .5f, y + .5f));
                var fraction = Math.Clamp(Vector2.Dot(point - from, delta) / lengthSquared, 0, 1);
                var ink = Rgba32.Lerp(foreground, background, fraction);
                var old = target.Get(x, y);
                target.Set(x, y, document.EditMask
                    ? MaskOperations.CoverageColor(Rgba32.Byte(old.A +
                        (MaskOperations.Luminance(ink) - old.A) * coverage * ink.A / 255f))
                    : Rgba32.Over(old, ink, coverage));
            }
    }

    public static void Clear(ImageDocument document, Layer layer)
    {
        var target = PixelTarget.RequireEditable(document, layer);
        for (var y = 0; y < target.Height; y++)
            for (var x = 0; x < target.Width; x++)
            {
                var coverage = PixelTarget.Coverage(document, layer, x, y);
                if (coverage <= 0) continue;
                var old = target.Get(x, y);
                var alpha = Rgba32.Byte(old.A * (1 - coverage));
                target.Set(x, y, document.EditMask ? MaskOperations.CoverageColor(alpha) : old.WithAlpha(alpha));
            }
    }

    /// <summary>Merge a complete filter result into its captured source using document-space selection coverage.</summary>
    public static PixelSurface RestrictToSelection(ImageDocument document, Layer layer,
        PixelSurface original, PixelSurface filtered)
    {
        if (filtered.Width != original.Width || filtered.Height != original.Height)
            throw new ArgumentException("Filter output dimensions differ from its source.", nameof(filtered));
        // Outside-canvas content is also protected, even when there is no active selection.
        for (var y = 0; y < original.Height; y++)
            for (var x = 0; x < original.Width; x++)
            {
                var coverage = PixelTarget.Coverage(document, layer, x, y);
                if (coverage >= 1) continue;
                var before = original.Get(x, y);
                var after = filtered.Get(x, y);
                filtered.Set(x, y, document.EditMask
                    ? MaskOperations.CoverageColor(Rgba32.Byte(before.A + (after.A - before.A) * coverage))
                    : Rgba32.Lerp(before, after, coverage));
            }
        return filtered;
    }
}
