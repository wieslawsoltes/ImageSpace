using ImageSpace.Core;
using SkiaSharp;

namespace ImageSpace.Skia;

public sealed partial class ImageRenderer
{
    private void DrawContentRange(SKCanvas canvas, ImageDocument document, int first, int last)
    {
        for (var i = first; i <= last;)
        {
            // Invalid orphan clips are never shown as unrestricted content by a renderer.
            // Document validation and the native/PSD readers reject such input explicitly.
            if (document.Layers[i].IsClipped)
            {
                i++;
                continue;
            }
            var end = i;
            while (end < last && document.Layers[end + 1].IsClipped)
                end++;
            if (end == i)
                DrawLayer(canvas, document.Layers[i]);
            else
                DrawClippingGroup(canvas, document, i, end);
            i = end + 1;
        }
    }

    private void DrawClippingGroup(SKCanvas canvas, ImageDocument document, int first, int last)
    {
        var basis = document.Layers[first];
        if (!basis.Visible || basis.Opacity <= 0 || basis.Kind == LayerKind.Adjustment)
            return;
        ClippingGroupDraws++;
        using var composite = new SKPaint
        {
            Color = SKColors.White.WithAlpha(Rgba32.Byte(basis.Opacity * 255)),
            BlendMode = Blend(basis.Blend)
        };
        var saved = canvas.SaveLayer(composite);
        try
        {
            DrawClippedPrefix(last);
        }
        finally { canvas.RestoreToCount(saved); }

        void DrawClippedPrefix(int end)
        {
            var adjustment = -1;
            for (var i = end; i > first; i--)
                if (document.Layers[i] is { Visible: true, Kind: LayerKind.Adjustment, Opacity: > 0 })
                {
                    adjustment = i;
                    break;
                }
            if (adjustment < 0)
            {
                // Base opacity and blend belong to the entire group, not this first draw.
                DrawLayer(canvas, basis, ignoreOpacity: true);
                DrawClippedContent(first + 1, end);
                return;
            }
            var layer = document.Layers[adjustment];
            var bounds = new SKRect(0, 0, document.Width, document.Height);
            var mask = layer.MaskEnabled && layer.Mask is not null && layer.MaskDensity > 0
                ? _masks.Get(layer, bounds, true, DrawTiles) : null;
            using var paint = _adjustments.CreatePaint(layer, mask, bounds);
            // Adjustment effects (including spatial blur) must never expand a clipping
            // group's alpha. Restore the original prefix alpha after evaluating its color.
            using var opaque = SKColorFilter.CreateColorMatrix([
                1,0,0,0,0, 0,1,0,0,0, 0,0,1,0,0, 0,0,0,0,1]);
            using var color = SKImageFilter.CreateColorFilter(opaque, paint.ImageFilter, bounds);
            using var locked = SKImageFilter.CreateBlendMode(SKBlendMode.DstIn, color, null, bounds);
            paint.ImageFilter = locked;
            var count = canvas.SaveLayer(bounds, paint);
            try
            {
                DrawClippedPrefix(adjustment - 1);
            }
            finally { canvas.RestoreToCount(count); }
            DrawClippedContent(adjustment + 1, end);
        }

        void DrawClippedContent(int start, int end)
        {
            for (var i = start; i <= end; i++)
            {
                var layer = document.Layers[i];
                if (!layer.Visible || layer.Opacity <= 0 || layer.Kind == LayerKind.Adjustment)
                    continue;
                DrawLayerCore(canvas, layer, false, false, false, _clipping.Get(layer.Blend));
            }
        }
    }
}
