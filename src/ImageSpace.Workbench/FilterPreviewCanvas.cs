using System.Runtime.InteropServices;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace ImageSpace.Workbench;

internal sealed class FilterPreviewCanvas : SKCanvasElement, IDisposable
{
    private SKImage? _before, _after;
    private float _split;
    public float Split { get => _split; set { _split = Math.Clamp(value, 0, 1); Invalidate(); } }
    public void SetBefore(PixelSurface pixels) { _before?.Dispose(); _before = Image(pixels); Invalidate(); }
    public void SetAfter(PixelSurface pixels) { _after?.Dispose(); _after = Image(pixels); Invalidate(); }
    private static SKImage Image(PixelSurface pixels)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(pixels.Width, pixels.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var bytes = pixels.ToRgba();
        Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
        return SKImage.FromBitmap(bitmap);
    }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        canvas.Clear(new SKColor(35, 35, 35));
        if (_before is null) return;
        var scale = Math.Min((float)area.Width / _before.Width, (float)area.Height / _before.Height);
        var width = _before.Width * scale; var height = _before.Height * scale;
        var bounds = SKRect.Create(((float)area.Width - width) / 2, ((float)area.Height - height) / 2, width, height);
        var save = canvas.Save();
        try
        {
            canvas.ClipRect(bounds);
            using var paint = new SKPaint { Color = new SKColor(145, 145, 145) };
            canvas.DrawRect(bounds, paint); paint.Color = new SKColor(190, 190, 190);
            for (var y = bounds.Top; y < bounds.Bottom; y += 12)
                for (var x = bounds.Left; x < bounds.Right; x += 12)
                    if (((int)((x - bounds.Left) / 12) + (int)((y - bounds.Top) / 12)) % 2 == 0)
                        canvas.DrawRect(x, y, 12, 12, paint);
            canvas.DrawImage(_after ?? _before, bounds);
            if (_split > 0)
            {
                var edge = bounds.Left + bounds.Width * _split;
                canvas.Save(); canvas.ClipRect(new SKRect(bounds.Left, bounds.Top, edge, bounds.Bottom));
                canvas.DrawImage(_before, bounds); canvas.Restore();
                paint.Color = SKColors.White; paint.StrokeWidth = 1;
                canvas.DrawLine(edge, bounds.Top, edge, bounds.Bottom, paint);
            }
        }
        finally { canvas.RestoreToCount(save); }
    }
    public new void Dispose() { _before?.Dispose(); _after?.Dispose(); _before = _after = null; }
}
