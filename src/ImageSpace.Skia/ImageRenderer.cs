using System.Diagnostics;
using System.Runtime.InteropServices;
using ImageSpace.Core;
using SkiaSharp;
namespace ImageSpace.Skia;

public sealed class ImageRenderer : IDisposable
{
    private sealed record CachedTile(SKImage Image, long Revision);
    private readonly Dictionary<object, CachedTile> _tiles = new(ReferenceEqualityComparer.Instance);
    private SKTypeface? _typeface;
    public double LastRenderMilliseconds
    {
        get; private set;
    }
    public long TileUploads
    {
        get; private set;
    }
    public int CachedTiles => _tiles.Count;
    public void SetTypeface(SKTypeface typeface)
    {
        _typeface?.Dispose();
        _typeface = typeface;
    }
    public void Draw(SKCanvas canvas, ImageDocument document)
    {
        var watch = Stopwatch.StartNew();
        canvas.Save();
        canvas.ClipRect(new(0, 0, document.Width, document.Height));
        DrawRange(document.Layers.Count - 1);
        canvas.Restore();
        LastRenderMilliseconds = watch.Elapsed.TotalMilliseconds;
        if (_tiles.Count > 768)
            Prune(document);
        void DrawRange(int last)
        {
            var adjustment = -1;
            for (var i = last; i >= 0; i--)
            if (document.Layers[i].Visible && document.Layers[i].Kind == LayerKind.Adjustment)
            {
                adjustment = i;
                break;
            }
            if (adjustment < 0)
            {
                for (var i = 0; i <= last; i++)
                    DrawLayer(canvas, document.Layers[i]);
                return;
            }
            var layer = document.Layers[adjustment];
            using var paint = AdjustmentPaint(layer);
            canvas.SaveLayer(new SKRect(0, 0, document.Width, document.Height), paint);
            DrawRange(adjustment - 1);
            canvas.Restore();
            for (var i = adjustment + 1; i <= last; i++)
                DrawLayer(canvas, document.Layers[i]);
        }
    }
    public void DrawLayer(SKCanvas canvas, Layer layer, bool ignoreVisibility = false, bool ignoreOpacity = false)
    {
        if ((!ignoreVisibility && !layer.Visible) || layer.Kind == LayerKind.Adjustment)
            return;
        canvas.Save();
        var t = layer.Transform;
        var matrix = new SKMatrix { ScaleX = t.M11, SkewX = t.M21, TransX = t.M31, SkewY = t.M12, ScaleY = t.M22, TransY = t.M32, Persp2 = 1 };
        canvas.Concat(in matrix);
        using var composite = new SKPaint { Color = SKColors.White.WithAlpha(ignoreOpacity ? (byte)255 : Rgba32.Byte(layer.Opacity * 255)), BlendMode = ignoreOpacity ? SKBlendMode.SrcOver : Blend(layer.Blend) };
        canvas.SaveLayer(composite);
        using var paint = new SKPaint { IsAntialias = true, Color = Color(layer.Color) };
        switch (layer.Kind)
        {
            case LayerKind.Raster:
                if (layer.Pixels is not null)
                    DrawTiles(canvas, layer.Pixels);
                break;
            case LayerKind.Text:
                using (var font = new SKFont(_typeface ?? SKTypeface.Default, layer.FontSize))
                {
                    font.Embolden = layer.Bold;
                    var y = layer.FontSize;
                    foreach (var line in layer.Text.Replace("\r", "").Split('\n'))
                    {
                        canvas.DrawText(line, 0, y, SKTextAlign.Left, font, paint);
                        y += layer.FontSize * 1.16f;
                    }
                }
                break;
            case LayerKind.Rectangle:
                canvas.DrawRoundRect(new(0, 0, layer.Width, layer.Height), layer.CornerRadius, layer.CornerRadius, paint);
                break;
            case LayerKind.Ellipse:
                canvas.DrawOval(new SKRect(0, 0, layer.Width, layer.Height), paint);
                break;
        }
        if (layer.StrokeWidth > 0 && layer.Kind is LayerKind.Rectangle or LayerKind.Ellipse)
        {
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = layer.StrokeWidth;
            paint.Color = Color(layer.StrokeColor);
            if (layer.Kind == LayerKind.Ellipse)
                canvas.DrawOval(new SKRect(0, 0, layer.Width, layer.Height), paint);
            else
                canvas.DrawRoundRect(new(0, 0, layer.Width, layer.Height), layer.CornerRadius, layer.CornerRadius, paint);
        }
        if (layer.MaskEnabled && layer.Mask is not null)
        {
            using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
            canvas.SaveLayer(maskPaint);
            DrawTiles(canvas, layer.Mask);
            canvas.Restore();
        }
        canvas.Restore();
        canvas.Restore();
    }
    public void DrawTiles(SKCanvas canvas, PixelSurface surface)
    {
        canvas.Save();
        canvas.ClipRect(new(0, 0, surface.Width, surface.Height));
        foreach (var (x, y, memory, identity) in surface.EnumerateTiles())
        {
            if (canvas.QuickReject(new SKRect(x, y, x + PixelSurface.TileSize, y + PixelSurface.TileSize)))
                continue;
            var revision = surface.GetTileRevision(x, y);
            if (!_tiles.TryGetValue(identity, out var cached) || cached.Revision != revision)
            {
                cached?.Image.Dispose();
                using var bitmap = new SKBitmap(new SKImageInfo(PixelSurface.TileSize, PixelSurface.TileSize, SKColorType.Rgba8888, SKAlphaType.Unpremul));
                Marshal.Copy(memory.ToArray(), 0, bitmap.GetPixels(), memory.Length);
                cached = new(SKImage.FromBitmap(bitmap), revision);
                _tiles[identity] = cached;
                TileUploads++;
            }
            canvas.DrawImage(cached.Image, x, y);
        }
        canvas.Restore();
    }
    public PixelSurface Rasterize(ImageDocument document)
    {
        using var surface = SKSurface.Create(new SKImageInfo(document.Width, document.Height, SKColorType.Rgba8888, SKAlphaType.Premul)) ?? throw new InvalidOperationException("Unable to allocate an export surface.");
        surface.Canvas.Clear(SKColors.Transparent);
        Draw(surface.Canvas, document);
        using var image = surface.Snapshot();
        return ReadPixels(image, document.Width, document.Height);
    }
    public PixelSurface RasterizeLayer(ImageDocument document, Layer layer, bool bakeOpacity = false)
    {
        using var surface = SKSurface.Create(new SKImageInfo(document.Width, document.Height, SKColorType.Rgba8888, SKAlphaType.Premul)) ?? throw new InvalidOperationException("Unable to allocate a layer surface.");
        surface.Canvas.Clear(SKColors.Transparent);
        DrawLayer(surface.Canvas, layer, true, !bakeOpacity);
        using var image = surface.Snapshot();
        return ReadPixels(image, document.Width, document.Height);
    }
    public byte[] Export(ImageDocument document, SKEncodedImageFormat format, int quality = 95)
    {
        using var surface = SKSurface.Create(new SKImageInfo(document.Width, document.Height, SKColorType.Rgba8888, SKAlphaType.Premul)) ?? throw new InvalidOperationException("Unable to allocate an export surface.");
        surface.Canvas.Clear(format == SKEncodedImageFormat.Jpeg ? SKColors.White : SKColors.Transparent);
        Draw(surface.Canvas, document);
        using var image = surface.Snapshot();
        using var data = image.Encode(format, Math.Clamp(quality, 1, 100)) ?? throw new InvalidOperationException("This codec cannot encode the selected format.");
        return data.ToArray();
    }
    public static PixelSurface Decode(byte[] encoded)
    {
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Unsupported or damaged image.");
        PixelSurface.ValidateSize(codec.Info.Width, codec.Info.Height);
        using var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result != SKCodecResult.Success)
            throw new InvalidDataException($"Image decode failed: {result}");
        var bytes = new byte[checked(bitmap.Width * bitmap.Height * 4)];
        Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);
        return PixelSurface.FromRgba(bitmap.Width, bitmap.Height, bytes);
    }
    private static PixelSurface ReadPixels(SKImage image, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (!image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
            throw new InvalidOperationException("Pixel readback failed.");
        var bytes = new byte[checked(width * height * 4)];
        Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);
        return PixelSurface.FromRgba(width, height, bytes);
    }
    private static SKPaint AdjustmentPaint(Layer layer)
    {
        var paint = new SKPaint();
        var opacity = Math.Clamp(layer.Opacity, 0, 1);
        var brightness = layer.Amount / 100;
        var contrast = 1 + Math.Clamp(layer.Secondary, -99, 300) / 100;
        float[] identity = [1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0];
        float[]? matrix = layer.Adjustment switch
        {
            AdjustmentKind.Invert => [-1, 0, 0, 0, 1, 0, -1, 0, 0, 1, 0, 0, -1, 0, 1, 0, 0, 0, 1, 0],
            AdjustmentKind.Grayscale => [.2126f, .7152f, .0722f, 0, 0, .2126f, .7152f, .0722f, 0, 0, .2126f, .7152f, .0722f, 0, 0, 0, 0, 0, 1, 0],
            AdjustmentKind.Sepia => [.393f, .769f, .189f, 0, 0, .349f, .686f, .168f, 0, 0, .272f, .534f, .131f, 0, 0, 0, 0, 0, 1, 0],
            AdjustmentKind.BrightnessContrast => [contrast, 0, 0, 0, .5f * (1 - contrast) + brightness, 0, contrast, 0, 0, .5f * (1 - contrast) + brightness, 0, 0, contrast, 0, .5f * (1 - contrast) + brightness, 0, 0, 0, 1, 0],
            AdjustmentKind.Saturation => Saturation(Math.Max(0, 1 + layer.Amount / 100)),
            _ => null
        };
        if (matrix is not null)
        {
            for (var i = 0; i < 20; i++)
                matrix[i] = identity[i] + (matrix[i] - identity[i]) * opacity;
            paint.ColorFilter = SKColorFilter.CreateColorMatrix(matrix);
        }
        if (layer.Adjustment == AdjustmentKind.GaussianBlur)
            paint.ImageFilter = SKImageFilter.CreateBlur(Math.Clamp(layer.Amount, 0, 32) * opacity, Math.Clamp(layer.Amount, 0, 32) * opacity);
        return paint;
    }
    private static float[] Saturation(float s)
    {
        var r = .2126f * (1 - s);
        var g = .7152f * (1 - s);
        var b = .0722f * (1 - s);
        return [r + s, g, b, 0, 0, r, g + s, b, 0, 0, r, g, b + s, 0, 0, 0, 0, 0, 1, 0];
    }
    public static SKColor Color(Rgba32 c) => new(c.R, c.G, c.B, c.A);
    public static SKBlendMode Blend(LayerBlend b) => b switch { LayerBlend.Multiply => SKBlendMode.Multiply, LayerBlend.Screen => SKBlendMode.Screen, LayerBlend.Overlay => SKBlendMode.Overlay, LayerBlend.Darken => SKBlendMode.Darken, LayerBlend.Lighten => SKBlendMode.Lighten, LayerBlend.ColorDodge => SKBlendMode.ColorDodge, LayerBlend.ColorBurn => SKBlendMode.ColorBurn, LayerBlend.HardLight => SKBlendMode.HardLight, LayerBlend.SoftLight => SKBlendMode.SoftLight, LayerBlend.Difference => SKBlendMode.Difference, LayerBlend.Exclusion => SKBlendMode.Exclusion, LayerBlend.Hue => SKBlendMode.Hue, LayerBlend.Saturation => SKBlendMode.Saturation, LayerBlend.Color => SKBlendMode.Color, LayerBlend.Luminosity => SKBlendMode.Luminosity, _ => SKBlendMode.SrcOver };
    public void Prune(ImageDocument document)
    {
        var used = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var s in document.Layers.SelectMany(l => new[] { l.Pixels, l.Mask }))
        if (s is not null)
        foreach (var t in s.EnumerateTiles())
            used.Add(t.Identity);
        foreach (var key in _tiles.Keys.Where(k => !used.Contains(k)).ToArray())
        {
            _tiles[key].Image.Dispose();
            _tiles.Remove(key);
        }
    }
    public void Dispose()
    {
        foreach (var tile in _tiles.Values)
            tile.Image.Dispose();
        _tiles.Clear();
        _typeface?.Dispose();
    }
}
