using System.Numerics;
using ImageSpace.Imaging;
using SkiaSharp;

namespace ImageSpace.Editor;

public sealed partial class ImageViewport
{
    private void DrawBrushCursor(SKCanvas canvas)
    {
        var layer = Session.Document.ActiveLayer;
        if (layer is null || PixelTarget.Get(Session.Document, layer) is null)
            return;
        var mapping = PixelTarget.Prepare(Session.Document, layer);
        var local = mapping.ToLocal(ToDocument(_cursor));
        var matrix = mapping.ToDocumentMatrix * Matrix3x2.CreateScale(Zoom) * Matrix3x2.CreateTranslation(Pan);
        var skia = new SKMatrix
        {
            ScaleX = matrix.M11,
            SkewX = matrix.M21,
            TransX = matrix.M31,
            SkewY = matrix.M12,
            ScaleY = matrix.M22,
            TransY = matrix.M32,
            Persp2 = 1
        };
        using var path = new SKPath();
        path.AddCircle(local.X, local.Y, Math.Max(.5f, Brush.Size / 2));
        path.Transform(skia);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3, Color = new SKColor(0, 0, 0, 170) };
        canvas.DrawPath(path, paint);
        paint.StrokeWidth = 1;
        paint.Color = SKColors.White;
        canvas.DrawPath(path, paint);
    }
}
