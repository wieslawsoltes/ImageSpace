using ImageSpace.Core;
using SkiaSharp;

namespace ImageSpace.Skia;

/// <summary>Renderer-owned tone tables. Opacity/mask coverage is applied after the complete effect.</summary>
internal sealed class AdjustmentFilterCache : IDisposable
{
    private static readonly byte[] IdentityAlpha = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
    private sealed record Entry(AdjustmentKind Kind, CurvesAdjustment Curves, LevelsAdjustment Levels, SKColorFilter Filter);
    private readonly Dictionary<Guid, Entry> _entries = [];
    public long ToneFilterBuilds { get; private set; }

    public SKPaint CreatePaint(Layer layer, SKImageFilter? mask, SKRect bounds)
    {
        var paint = new SKPaint();
        using var color = CreateColorFilter(layer);
        using var effect = color is not null ? SKImageFilter.CreateColorFilter(color)
            : layer.Adjustment == AdjustmentKind.GaussianBlur
                ? SKImageFilter.CreateBlur(Math.Clamp(layer.Amount, 0, 32), Math.Clamp(layer.Amount, 0, 32)) : null;
        if (effect is null || layer.Opacity <= 0) return paint;
        if (mask is null)
        {
            // Interpolate the CLAMPED effect result, not parameters/matrix coefficients. This also
            // makes half-opacity blur a crossfade with the full blur rather than half its radius.
            using var faded = layer.Opacity >= 1 ? null
                : SKImageFilter.CreateArithmetic(0, layer.Opacity, 1 - layer.Opacity, 0, true, null, effect, bounds);
            paint.ImageFilter = faded ?? effect;
            return paint;
        }
        using var scale = SKColorFilter.CreateColorMatrix([
            1,0,0,0,0, 0,1,0,0,0, 0,0,1,0,0, 0,0,0,layer.Opacity,0]);
        using var coverage = SKImageFilter.CreateColorFilter(scale, mask, bounds);
        using var selected = SKImageFilter.CreateBlendMode(SKBlendMode.DstIn, effect, coverage, bounds);
        using var retained = SKImageFilter.CreateBlendMode(SKBlendMode.DstOut, null, coverage, bounds);
        // Both branches read the same isolated source. Source-over would increase alpha for
        // translucent input; complementary coverage requires their premultiplied sum instead.
        using var merged = SKImageFilter.CreateBlendMode(SKBlendMode.Plus, retained, selected, bounds);
        paint.ImageFilter = merged;
        return paint;
    }

    private SKColorFilter? CreateColorFilter(Layer layer)
    {
        if (layer.Adjustment is AdjustmentKind.Curves or AdjustmentKind.Levels)
        {
            if (!_entries.TryGetValue(layer.Id, out var entry) || entry.Kind != layer.Adjustment ||
                !ReferenceEquals(entry.Curves, layer.Curves) || !ReferenceEquals(entry.Levels, layer.Levels))
            {
                var tables = layer.Adjustment == AdjustmentKind.Curves
                    ? RgbLookupTables.FromCurves(layer.Curves) : RgbLookupTables.FromLevels(layer.Levels);
                var filter = SKColorFilter.CreateTable(IdentityAlpha, tables.Red.ToArray(), tables.Green.ToArray(), tables.Blue.ToArray());
                entry?.Filter.Dispose();
                entry = new(layer.Adjustment, layer.Curves, layer.Levels, filter);
                _entries[layer.Id] = entry;
                ToneFilterBuilds++;
            }
            // A composed identity gives the caller its own native reference without transferring
            // ownership of the cached table filter.
            using var identity = SKColorFilter.CreateColorMatrix([1,0,0,0,0, 0,1,0,0,0, 0,0,1,0,0, 0,0,0,1,0]);
            return SKColorFilter.CreateCompose(identity, entry.Filter);
        }
        var brightness = layer.Amount / 100;
        var contrast = 1 + Math.Clamp(layer.Secondary, -99, 300) / 100;
        float[]? matrix = layer.Adjustment switch
        {
            AdjustmentKind.Invert => [-1,0,0,0,1, 0,-1,0,0,1, 0,0,-1,0,1, 0,0,0,1,0],
            AdjustmentKind.Grayscale => [.2126f,.7152f,.0722f,0,0, .2126f,.7152f,.0722f,0,0, .2126f,.7152f,.0722f,0,0, 0,0,0,1,0],
            AdjustmentKind.Sepia => [.393f,.769f,.189f,0,0, .349f,.686f,.168f,0,0, .272f,.534f,.131f,0,0, 0,0,0,1,0],
            AdjustmentKind.BrightnessContrast => [contrast,0,0,0,.5f*(1-contrast)+brightness, 0,contrast,0,0,.5f*(1-contrast)+brightness, 0,0,contrast,0,.5f*(1-contrast)+brightness, 0,0,0,1,0],
            AdjustmentKind.Saturation => Saturation(Math.Max(0, 1 + layer.Amount / 100)),
            _ => null
        };
        return matrix is null ? null : SKColorFilter.CreateColorMatrix(matrix);
    }

    private static float[] Saturation(float value)
    {
        var r = .2126f * (1 - value);
        var g = .7152f * (1 - value);
        var b = .0722f * (1 - value);
        return [r+value,g,b,0,0, r,g+value,b,0,0, r,g,b+value,0,0, 0,0,0,1,0];
    }

    public void Prune(ImageDocument document)
    {
        var ids = document.Layers.Select(layer => layer.Id).ToHashSet();
        foreach (var id in _entries.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            _entries[id].Filter.Dispose();
            _entries.Remove(id);
        }
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values) entry.Filter.Dispose();
        _entries.Clear();
    }
}
