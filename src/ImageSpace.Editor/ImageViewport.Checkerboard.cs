using SkiaSharp;
using Windows.Foundation;
namespace ImageSpace.Editor;

public sealed partial class ImageViewport
{
    private static void DrawCheckerboard(SKCanvas canvas, SKRect document, Size area, SKPaint paint)
    {
        const float step = 12;
        var startColumn = Math.Max(0, (int)Math.Floor(-document.Left / step));
        var startRow = Math.Max(0, (int)Math.Floor(-document.Top / step));
        var endColumn = (int)Math.Ceiling((Math.Min(document.Right, (float)area.Width) - document.Left) / step);
        var endRow = (int)Math.Ceiling((Math.Min(document.Bottom, (float)area.Height) - document.Top) / step);
        // Work is bounded by the viewport, not by a heavily zoomed document's off-screen extent.
        for (var row = startRow; row < endRow; row++)
        for (var column = startColumn; column < endColumn; column++)
        if ((row + column) % 2 == 0)
            canvas.DrawRect(document.Left + column * step, document.Top + row * step, step, step, paint);
    }
}
