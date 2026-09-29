using System.Diagnostics;
using ImageSpace.Core;
using SkiaSharp;

namespace ImageSpace.Skia;

public sealed partial class ImageRenderer : IDisposable
{
    private sealed record CachedTile(SKImage Image, long Revision);
    private readonly Dictionary<object, CachedTile> _tiles = new(ReferenceEqualityComparer.Instance);
    private readonly AdjustmentFilterCache _adjustments = new();
    private readonly MaskFilterCache _masks = new();
    private readonly ClippingBlenderCache _clipping = new();
    public long ClippingGroupDraws
    {
        get; private set;
    }
    public long ClippingBlenderBuilds => _clipping.Builds;
    private SKTypeface? _typeface;
    public long TypefaceRevision
    {
        get; private set;
    }
    /// <summary>Copy a font into a separate cache-owning renderer on its owning thread.</summary>
    public void CopyTypefaceFrom(ImageRenderer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source._typeface is null)
        {
            _typeface?.Dispose();
            _typeface = null;
            TypefaceRevision++;
            return;
        }
        using var stream = source._typeface.OpenStream();
        SetTypeface(SKTypeface.FromStream(stream)
            ?? throw new InvalidOperationException("Unable to copy the preview typeface."));
    }
    public double LastRenderMilliseconds
    {
        get; private set;
    }
    public long TileUploads
    {
        get; private set;
    }
    public long DocumentDraws
    {
        get; private set;
    }
    public long ToneFilterBuilds => _adjustments.ToneFilterBuilds;
    public long MaskFilterBuilds => _masks.Builds;
    public long MaskSourceBuilds => _masks.SourceBuilds;
    public int CachedTiles => _tiles.Count;

    /// <summary>
    /// Allows opaque, normal-blend, effectively unmasked layers to draw directly.
    /// Disable for differential validation against the always-isolated reference path.
    /// Document isolation is retained regardless of this setting.
    /// </summary>
    public bool EnableDirectLayerDrawing { get; set; } = true;
    public long DirectLayerDraws
    {
        get; private set;
    }
    public long IsolatedLayerDraws
    {
        get; private set;
    }

    public void SetTypeface(SKTypeface typeface)
    {
        _typeface?.Dispose();
        _typeface = typeface;
        TypefaceRevision++;
    }

    public void Draw(SKCanvas canvas, ImageDocument document)
    {
        DocumentDraws++;
        var started = Stopwatch.GetTimestamp();
        var save = canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(0, 0, document.Width, document.Height));
            // Never blend document effects against workspace chrome or the JPEG background.
            canvas.SaveLayer();
            DrawRange(document.Layers.Count - 1);
            canvas.Restore();
        }
        finally { canvas.RestoreToCount(save); }
        _masks.Prune(document);
        _adjustments.Prune(document);
        LastRenderMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (_tiles.Count > 768)
            Prune(document);

        void DrawRange(int last)
        {
            var adjustment = -1;
            for (var i = last; i >= 0; i--)
            {
                var item = document.Layers[i];
                if (item.Visible && !item.IsClipped && item.Kind == LayerKind.Adjustment)
                {
                    adjustment = i;
                    break;
                }
            }
            if (adjustment < 0)
            {
                DrawContentRange(canvas, document, 0, last);
                return;
            }
            var layer = document.Layers[adjustment];
            var bounds = new SKRect(0, 0, document.Width, document.Height);
            var mask = layer.MaskEnabled && layer.Mask is not null && layer.MaskDensity > 0
                ? _masks.Get(layer, bounds, true, DrawTiles) : null;
            using var paint = _adjustments.CreatePaint(layer, mask, bounds);
            var saved = canvas.SaveLayer(bounds, paint);
            try
            {
                DrawRange(adjustment - 1);
            }
            finally { canvas.RestoreToCount(saved); }
            DrawContentRange(canvas, document, adjustment + 1, last);
        }
    }

    /// <summary>Draw standalone authored content, independent of its document clipping relationships.</summary>
    public void DrawLayer(SKCanvas canvas, Layer layer, bool ignoreVisibility = false, bool ignoreOpacity = false, bool ignoreTransform = false)
        => DrawLayerCore(canvas, layer, ignoreVisibility, ignoreOpacity, ignoreTransform, null);

    private void DrawLayerCore(SKCanvas canvas, Layer layer, bool ignoreVisibility, bool ignoreOpacity,
        bool ignoreTransform, SKBlender? clippedBlender)
    {
        if ((!ignoreVisibility && !layer.Visible) || layer.Kind == LayerKind.Adjustment)
            return;
        var save = canvas.Save();
        try
        {
            var transform = layer.Transform;
            var matrix = new SKMatrix
            {
                ScaleX = transform.M11,
                SkewX = transform.M21,
                TransX = transform.M31,
                SkewY = transform.M12,
                ScaleY = transform.M22,
                TransY = transform.M32,
                Persp2 = 1
            };
            if (!ignoreTransform)
                canvas.Concat(in matrix);
            var masked = layer.MaskEnabled && layer.Mask is not null && layer.MaskDensity > 0;
            var direct = clippedBlender is null && EnableDirectLayerDrawing && !masked &&
                (ignoreOpacity || (layer.Opacity == 1 && layer.Blend == LayerBlend.Normal));
            if (direct)
            {
                // SrcOver with unit group opacity needs no per-layer offscreen surface.
                // This also avoids constructing paints for ordinary raster layers.
                DirectLayerDraws++;
                DrawContent(canvas, layer);
                return;
            }

            IsolatedLayerDraws++;
            using var composite = new SKPaint
            {
                Color = SKColors.White.WithAlpha(ignoreOpacity ? (byte)255 : Rgba32.Byte(layer.Opacity * 255)),
                BlendMode = ignoreOpacity ? SKBlendMode.SrcOver : Blend(layer.Blend)
            };
            if (clippedBlender is not null)
                composite.Blender = clippedBlender;
            canvas.SaveLayer(composite);
            DrawContent(canvas, layer);
            if (masked)
            {
                var coverage = _masks.Get(layer, canvas.LocalClipBounds, false, DrawTiles);
                ApplyCoverage(canvas, coverage);
            }
            canvas.Restore();
        }
        finally { canvas.RestoreToCount(save); }
    }

    private void DrawContent(SKCanvas canvas, Layer layer)
    {
        if (layer.Kind == LayerKind.Raster)
        {
            if (layer.Pixels is not null)
                DrawTiles(canvas, layer.Pixels);
            return;
        }
        using var paint = new SKPaint { IsAntialias = true, Color = Color(layer.Color) };
        switch (layer.Kind)
        {
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
            if (layer.Kind == LayerKind.Ellipse)
                canvas.DrawOval(new SKRect(0, 0, layer.Width, layer.Height), paint);
            else
                canvas.DrawRoundRect(new SKRect(0, 0, layer.Width, layer.Height), layer.CornerRadius, layer.CornerRadius, paint);
        }
    }

    private static void ApplyCoverage(SKCanvas canvas, SKImageFilter coverage)
    {
        // The transparent exterior must participate in DstIn. A filtered DrawPaint
        // alone can be culled outside its output and leave source pixels unmasked.
        using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
        var save = canvas.SaveLayer(maskPaint);
        try
        {
            using var coveragePaint = new SKPaint { ImageFilter = coverage };
            canvas.DrawPaint(coveragePaint);
        }
        finally { canvas.RestoreToCount(save); }
    }

    public unsafe void DrawTiles(SKCanvas canvas, PixelSurface surface)
    {
        var save = canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(0, 0, surface.Width, surface.Height));
            foreach (var (x, y, memory, identity) in surface.EnumerateTiles())
            {
                if (canvas.QuickReject(new SKRect(x, y, x + PixelSurface.TileSize, y + PixelSurface.TileSize)))
                    continue;
                var revision = surface.GetTileRevision(x, y);
                if (!_tiles.TryGetValue(identity, out var cached) || cached.Revision != revision)
                {
                    cached?.Image.Dispose();
                    using var bitmap = new SKBitmap(new SKImageInfo(PixelSurface.TileSize, PixelSurface.TileSize, SKColorType.Rgba8888, SKAlphaType.Unpremul));
                    memory.Span.CopyTo(new Span<byte>((void*)bitmap.GetPixels(), memory.Length));
                    cached = new CachedTile(SKImage.FromBitmap(bitmap), revision);
                    _tiles[identity] = cached;
                    TileUploads++;
                }
                canvas.DrawImage(cached.Image, x, y);
            }
        }
        finally { canvas.RestoreToCount(save); }
    }

    public PixelSurface Rasterize(ImageDocument document) => RasterizePreview(document, Math.Max(document.Width, document.Height));

    /// <summary>Bounded readback for sampled histograms and previews, never a substitute for full-resolution export.</summary>
    public PixelSurface RasterizePreview(ImageDocument document, int maximumEdge = 256)
    {
        if (maximumEdge < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumEdge));
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

    public static unsafe PixelSurface Decode(byte[] encoded)
    {
        using var data = SKData.CreateCopy(encoded);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Unsupported or damaged image.");
        PixelSurface.ValidateSize(codec.Info.Width, codec.Info.Height);
        using var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result != SKCodecResult.Success)
            throw new InvalidDataException($"Image decode failed: {result}");
        return PixelSurface.FromRgba(bitmap.Width, bitmap.Height,
            new ReadOnlySpan<byte>((void*)bitmap.GetPixels(), checked(bitmap.RowBytes * bitmap.Height)), bitmap.RowBytes);
    }

    private static SKSurface CreateSurface(int width, int height)
    {
        PixelSurface.ValidateSize(width, height);
        return SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Unable to allocate the image surface.");
    }

    private static unsafe PixelSurface ReadPixels(SKImage image, int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (!image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
            throw new InvalidOperationException("Pixel readback failed.");
        return PixelSurface.FromRgba(width, height,
            new ReadOnlySpan<byte>((void*)bitmap.GetPixels(), checked(bitmap.RowBytes * height)), bitmap.RowBytes);
    }

    public static SKColor Color(Rgba32 color) => new(color.R, color.G, color.B, color.A);
    public static SKBlendMode Blend(LayerBlend blend) => blend switch
    {
        LayerBlend.Multiply => SKBlendMode.Multiply,
        LayerBlend.Screen => SKBlendMode.Screen,
        LayerBlend.Overlay => SKBlendMode.Overlay,
        LayerBlend.Darken => SKBlendMode.Darken,
        LayerBlend.Lighten => SKBlendMode.Lighten,
        LayerBlend.ColorDodge => SKBlendMode.ColorDodge,
        LayerBlend.ColorBurn => SKBlendMode.ColorBurn,
        LayerBlend.HardLight => SKBlendMode.HardLight,
        LayerBlend.SoftLight => SKBlendMode.SoftLight,
        LayerBlend.Difference => SKBlendMode.Difference,
        LayerBlend.Exclusion => SKBlendMode.Exclusion,
        LayerBlend.Hue => SKBlendMode.Hue,
        LayerBlend.Saturation => SKBlendMode.Saturation,
        LayerBlend.Color => SKBlendMode.Color,
        LayerBlend.Luminosity => SKBlendMode.Luminosity,
        _ => SKBlendMode.SrcOver
    };

    public void Prune(ImageDocument document)
    {
        var used = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var surface in document.Layers.SelectMany(layer => new[] { layer.Pixels, layer.Mask }))
        {
            if (surface is null)
                continue;
            foreach (var tile in surface.EnumerateTiles())
                used.Add(tile.Identity);
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
        foreach (var tile in _tiles.Values)
            tile.Image.Dispose();
        _tiles.Clear();
        _clipping.Dispose();
        _adjustments.Dispose();
        _masks.Dispose();
        _typeface?.Dispose();
    }
}
