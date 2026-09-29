using System.Runtime.InteropServices;
using ImageSpace.Skia;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace ImageSpace.Workbench;

internal sealed class FilterPreviewCanvas : SKCanvasElement, IDisposable
{
    private SKImage? _before, _after;
    private float _split;
    private bool _disposed;
    public float Split
    {
        get => _split;
        set
        {
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            var next = Math.Clamp(value, 0, 1);
            if (_split == next) return;
            _split = next;
            Invalidate();
        }
    }

    public void SetBefore(PixelSurface pixels) => Replace(ref _before, pixels);
    public void SetAfter(PixelSurface pixels) => Replace(ref _after, pixels);

    private void Replace(ref SKImage? target, PixelSurface pixels)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(pixels);
        // Preserve the last good image if allocation or pixel conversion fails.
        var image = Image(pixels);
        var previous = target;
        target = image;
        previous?.Dispose();
        Invalidate();
    }

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
        if (_disposed || _before is null || area.Width <= 0 || area.Height <= 0) return;
        var scale = Math.Min((float)area.Width / _before.Width, (float)area.Height / _before.Height);
        var width = _before.Width * scale;
        var height = _before.Height * scale;
        var bounds = SKRect.Create(((float)area.Width - width) / 2, ((float)area.Height - height) / 2, width, height);
        var save = canvas.Save();
        try
        {
            canvas.ClipRect(bounds);
            using var paint = new SKPaint { Color = new SKColor(145, 145, 145) };
            canvas.DrawRect(bounds, paint);
            paint.Color = new SKColor(190, 190, 190);
            for (var y = bounds.Top; y < bounds.Bottom; y += 12)
                for (var x = bounds.Left; x < bounds.Right; x += 12)
                    if (((int)((x - bounds.Left) / 12) + (int)((y - bounds.Top) / 12)) % 2 == 0)
                        canvas.DrawRect(x, y, 12, 12, paint);
            ImageComparisonRenderer.Draw(canvas, _before, _after ?? _before, bounds, _split);
            if (_split is > 0 and < 1)
            {
                var edge = bounds.Left + bounds.Width * _split;
                paint.Color = SKColors.White;
                paint.StrokeWidth = 1;
                canvas.DrawLine(edge, bounds.Top, edge, bounds.Bottom, paint);
            }
        }
        finally { canvas.RestoreToCount(save); }
    }

    public new void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _before?.Dispose();
        _after?.Dispose();
        _before = _after = null;
    }
}
