using System.Diagnostics;
using System.Runtime.InteropServices;
using ImageSpace.Core;
using SkiaSharp;

namespace ImageSpace.Skia;

public sealed partial class ImageRenderer : IDisposable
{
    private sealed record CachedTile(SKImage Image, long Revision);
    private readonly Dictionary<object, CachedTile> _tiles = new(ReferenceEqualityComparer.Instance);
    private readonly AdjustmentFilterCache _adjustments = new();
    private readonly MaskFilterCache _masks = new();
    private SKTypeface? _typeface;
    public double LastRenderMilliseconds { get; private set; }
    public long TileUploads { get; private set; }
    public long ToneFilterBuilds => _adjustments.ToneFilterBuilds;
    public long MaskFilterBuilds => _masks.Builds;
    public int CachedTiles => _tiles.Count;

    public void SetTypeface(SKTypeface typeface)
    {
        _typeface?.Dispose();
        _typeface = typeface;
    }

    public void Draw(SKCanvas canvas, ImageDocument document)
    {
        var watch = Stopwatch.StartNew();
        var save = canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(0, 0, document.Width, document.Height));
            // Isolate document blend modes and effects from presentation chrome/JPEG backgrounds.
            canvas.SaveLayer();
            DrawRange(document.Layers.Count - 1);
            canvas.Restore();
        }
        finally { canvas.RestoreToCount(save); }
        _masks.Prune(document);
        _adjustments.Prune(document);
        LastRenderMilliseconds = watch.Elapsed.TotalMilliseconds;
        if (_tiles.Count > 768) Prune(document);
        void DrawRange(int last)
        {
            var adjustment = -1;
            for (var i = last; i >= 0; i--)
            {
                if (document.Layers[i].Visible && document.Layers[i].Kind == LayerKind.Adjustment)
                {
                    adjustment = i;
                    break;
                }
            }
            if (adjustment < 0)
            {
                for (var i = 0; i <= last; i++) DrawLayer(canvas, document.Layers[i]);
                return;
            }
            var layer = document.Layers[adjustment];
            var bounds = new SKRect(0, 0, document.Width, document.Height);
            var mask = layer.MaskEnabled && layer.Mask is not null && layer.MaskDensity > 0
                ? _masks.Get(layer, bounds, true, DrawTiles) : null;
            using var paint = _adjustments.CreatePaint(layer, mask, bounds);
            canvas.SaveLayer(bounds, paint);
            DrawRange(adjustment - 1);
            canvas.Restore();
            for (var i = adjustment + 1; i <= last; i++) DrawLayer(canvas, document.Layers[i]);
        }
    }

    public void DrawLayer(SKCanvas canvas, Layer layer, bool ignoreVisibility = false, bool ignoreOpacity = false)
    {
        if ((!ignoreVisibility && !layer.Visible) || layer.Kind == LayerKind.Adjustment) return;
        var save = canvas.Save();
        try
        {
            var transform = layer.Transform;
            var matrix = new SKMatrix
            {
                ScaleX = transform.M11, SkewX = transform.M21, TransX = transform.M31,
                SkewY = transform.M12, ScaleY = transform.M22, TransY = transform.M32, Persp2 = 1
            };
            canvas.Concat(in matrix);
            using var composite = new SKPaint
            {
                Color = SKColors.White.WithAlpha(ignoreOpacity ? (byte)255 : Rgba32.Byte(layer.Opacity * 255)),
                BlendMode = ignoreOpacity ? SKBlendMode.SrcOver : Blend(layer.Blend)
            };
            canvas.SaveLayer(composite);
            using var paint = new SKPaint { IsAntialias = true, Color = Color(layer.Color) };
            switch (layer.Kind)
            {
                case LayerKind.Raster:
                    if (layer.Pixels is not null) DrawTiles(canvas, layer.Pixels);
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
                    canvas.DrawRoundRect(new SKRect(0, 0, layer.Width, layer.Height), layer.CornerRadius, layer.CornerRadius, paint);
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
                if (layer.Kind == LayerKind.Ellipse) canvas.DrawOval(new SKRect(0, 0, layer.Width, layer.Height), paint);
                else canvas.DrawRoundRect(new SKRect(0, 0, layer.Width, layer.Height), layer.CornerRadius, layer.CornerRadius, paint);
            }
            if (layer.MaskEnabled && layer.Mask is not null && layer.MaskDensity > 0)
            {
                var coverage = _masks.Get(layer, canvas.LocalClipBounds, false, DrawTiles);
                using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn, ImageFilter = coverage };
                canvas.DrawPaint(maskPaint);
            }
            canvas.Restore();
        }
        finally { canvas.RestoreToCount(save); }
    }

    public void DrawTiles(SKCanvas canvas, PixelSurface surface)
    {
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, surface.Width, surface.Height));
        foreach (var (x, y, memory, identity) in surface.EnumerateTiles())
        {
            if (canvas.QuickReject(new SKRect(x, y, x + PixelSurface.TileSize, y + PixelSurface.TileSize))) continue;
            var revision = surface.GetTileRevision(x, y);
            if (!_tiles.TryGetValue(identity, out var cached) || cached.Revision != revision)
            {
                cached?.Image.Dispose();
                using var bitmap = new SKBitmap(new SKImageInfo(PixelSurface.TileSize, PixelSurface.TileSize, SKColorType.Rgba8888, SKAlphaType.Unpremul));
                Marshal.Copy(memory.ToArray(), 0, bitmap.GetPixels(), memory.Length);
                cached = new CachedTile(SKImage.FromBitmap(bitmap), revision);
                _tiles[identity] = cached;
                TileUploads++;
            }
            canvas.DrawImage(cached.Image, x, y);
        }
        canvas.Restore();
    }

    public PixelSurface Rasterize(ImageDocument document) => RasterizePreview(document, Math.Max(document.Width, document.Height));

    /// <summary>Bounded readback for sampled histograms and previews, never a substitute for full-resolution export.</summary>
    public PixelSurface RasterizePreview(ImageDocument document, int maximumEdge = 256)
    {
        if (maximumEdge < 1) throw new ArgumentOutOfRangeException(nameof(maximumEdge));
        var scale = Math.Min(1, (double)maximumEdge / Math.Max(document.Width, document.Height));
        var width = Math.Max(1, (int)Math.Round(document.Width * scale));
        var height = Math.Max(1, (int)Math.Round(document.Height * scale));
        using var surface = CreateSurface(width, height);
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.Scale((float)width / document.Width, (float)height / document.Height);
        Draw(surface.Canvas, document);
        using var image = surface.Snapshot();
        return ReadPixels(image, width, height);
    }

    public PixelSurface RasterizeLayer(ImageDocument document, Layer layer, bool bakeOpacity = false)
    {
        using var surface = CreateSurface(document.Width, document.Height);
        surface.Canvas.Clear(SKColors.Transparent);
        DrawLayer(surface.Canvas, layer, true, !bakeOpacity);
        using var image = surface.Snapshot();
        return ReadPixels(image, document.Width, document.Height);
    }

    public byte[] Export(ImageDocument document, SKEncodedImageFormat format, int quality = 95)
    {
        using var surface = CreateSurface(document.Width, document.Height);
        surface.Canvas.Clear(format == SKEncodedImageFormat.Jpeg ? SKColors.White : SKColors.Transparent);
        Draw(surface.Canvas, document);
        using var image = surface.Snapshot();
        using var data = image.Encode(format, Math.Clamp(quality, 1, 100))
            ?? throw new InvalidOperationException("This codec cannot encode the selected format.");
        return data.ToArray();
    }

    public static PixelSurface Decode(byte[] encoded)
    {
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Unsupported or damaged image.");
        PixelSurface.ValidateSize(codec.Info.Width, codec.Info.Height);
        using var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result != SKCodecResult.Success) throw new InvalidDataException($"Image decode failed: {result}");
        var bytes = new byte[checked(bitmap.Width * bitmap.Height * 4)];
        Marshal.Copy(bitmap.GetPixels(), bytes, 0, bytes.Length);
        return PixelSurface.FromRgba(bitmap.Width, bitmap.Height, bytes);
    }

    private static SKSurface CreateSurface(int width, int height)
    {
        PixelSurface.ValidateSize(width, height);
        return SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Unable to allocate the image surface.");
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

    public static SKColor Color(Rgba32 color) => new(color.R, color.G, color.B, color.A);
    public static SKBlendMode Blend(LayerBlend blend) => blend switch
    {
        LayerBlend.Multiply => SKBlendMode.Multiply, LayerBlend.Screen => SKBlendMode.Screen,
        LayerBlend.Overlay => SKBlendMode.Overlay, LayerBlend.Darken => SKBlendMode.Darken,
        LayerBlend.Lighten => SKBlendMode.Lighten, LayerBlend.ColorDodge => SKBlendMode.ColorDodge,
        LayerBlend.ColorBurn => SKBlendMode.ColorBurn, LayerBlend.HardLight => SKBlendMode.HardLight,
        LayerBlend.SoftLight => SKBlendMode.SoftLight, LayerBlend.Difference => SKBlendMode.Difference,
        LayerBlend.Exclusion => SKBlendMode.Exclusion, LayerBlend.Hue => SKBlendMode.Hue,
        LayerBlend.Saturation => SKBlendMode.Saturation, LayerBlend.Color => SKBlendMode.Color,
        LayerBlend.Luminosity => SKBlendMode.Luminosity, _ => SKBlendMode.SrcOver
    };

    public void Prune(ImageDocument document)
    {
        var used = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var surface in document.Layers.SelectMany(layer => new[] { layer.Pixels, layer.Mask }))
        {
            if (surface is null) continue;
            foreach (var tile in surface.EnumerateTiles()) used.Add(tile.Identity);
        }
        foreach (var key in _tiles.Keys.Where(key => !used.Contains(key)).ToArray())
        {
            _tiles[key].Image.Dispose();
            _tiles.Remove(key);
        }
        _adjustments.Prune(document);
        _masks.Prune(document);
    }

    public void Dispose()
    {
        foreach (var tile in _tiles.Values) tile.Image.Dispose();
        _tiles.Clear();
        _adjustments.Dispose();
        _masks.Dispose();
        _typeface?.Dispose();
    }
}
