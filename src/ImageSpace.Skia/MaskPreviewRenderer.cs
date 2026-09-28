using ImageSpace.Core;
using SkiaSharp;

namespace ImageSpace.Skia;

public enum MaskPreviewMode { Composite, Grayscale, Overlay }

/// <summary>View-only effective-coverage inspection; never participates in image/native export.</summary>
public sealed class MaskPreviewRenderer : IDisposable
{
    // Separate graph cache avoids alternating raster-local/document-space cache keys each frame.
    private readonly MaskFilterCache _filters = new();
    public long FilterBuilds => _filters.Builds;

    public void Draw(SKCanvas canvas, ImageRenderer renderer, ImageDocument document, Layer layer, MaskPreviewMode mode)
    {
        if (mode == MaskPreviewMode.Composite || layer.Mask is null) return;
        var bounds = new SKRect(0, 0, document.Width, document.Height);
        var coverage = _filters.Get(layer, bounds, true, renderer.DrawTiles);
        using var color = SKColorFilter.CreateColorMatrix(mode == MaskPreviewMode.Grayscale
            ? [0,0,0,1,0, 0,0,0,1,0, 0,0,0,1,0, 0,0,0,0,1]
            : [0,0,0,0,1, 0,0,0,0,.12f, 0,0,0,0,.12f, 0,0,0,-.55f,.55f]);
        using var display = SKImageFilter.CreateColorFilter(color, coverage, bounds);
        using var paint = new SKPaint { ImageFilter = display, BlendMode = SKBlendMode.SrcOver };
        var saved = canvas.Save();
        try { canvas.ClipRect(bounds); canvas.DrawPaint(paint); }
        finally { canvas.RestoreToCount(saved); }
        _filters.Prune(document);
    }

    public void Dispose() => _filters.Dispose();
}
