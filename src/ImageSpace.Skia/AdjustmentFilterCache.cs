using ImageSpace.Core;
using SkiaSharp;

namespace ImageSpace.Skia;

/// <summary>Renderer-owned tone filters; immutable settings are the cache revision.</summary>
internal sealed class AdjustmentFilterCache : IDisposable
{
    private sealed record Entry(AdjustmentKind Kind, CurvesAdjustment Curves, LevelsAdjustment Levels,
        float Opacity, SKColorFilter Filter);
    private readonly Dictionary<Guid, Entry> _entries = [];
    public long ToneFilterBuilds { get; private set; }

    public SKPaint CreatePaint(Layer layer)
    {
        var paint = new SKPaint();
        if (layer.Adjustment is AdjustmentKind.Curves or AdjustmentKind.Levels)
        {
            if (!_entries.TryGetValue(layer.Id, out var entry) || entry.Kind != layer.Adjustment ||
                !ReferenceEquals(entry.Curves, layer.Curves) || !ReferenceEquals(entry.Levels, layer.Levels) ||
                entry.Opacity != layer.Opacity)
            {
                var tables = layer.Adjustment == AdjustmentKind.Curves
                    ? RgbLookupTables.FromCurves(layer.Curves, layer.Opacity)
                    : RgbLookupTables.FromLevels(layer.Levels, layer.Opacity);
                var filter = SKColorFilter.CreateTable(null, tables.Red.ToArray(), tables.Green.ToArray(), tables.Blue.ToArray());
                entry?.Filter.Dispose();
                entry = new Entry(layer.Adjustment, layer.Curves, layer.Levels, layer.Opacity, filter);
                _entries[layer.Id] = entry;
                ToneFilterBuilds++;
            }
            paint.ColorFilter = entry.Filter;
            return paint;
        }
        var opacity = Math.Clamp(layer.Opacity, 0, 1);
        var brightness = layer.Amount / 100;
        var contrast = 1 + Math.Clamp(layer.Secondary, -99, 300) / 100;
        float[] identity = [1,0,0,0,0, 0,1,0,0,0, 0,0,1,0,0, 0,0,0,1,0];
        float[]? matrix = layer.Adjustment switch
        {
            AdjustmentKind.Invert => [-1,0,0,0,1, 0,-1,0,0,1, 0,0,-1,0,1, 0,0,0,1,0],
            AdjustmentKind.Grayscale => [.2126f,.7152f,.0722f,0,0, .2126f,.7152f,.0722f,0,0, .2126f,.7152f,.0722f,0,0, 0,0,0,1,0],
            AdjustmentKind.Sepia => [.393f,.769f,.189f,0,0, .349f,.686f,.168f,0,0, .272f,.534f,.131f,0,0, 0,0,0,1,0],
            AdjustmentKind.BrightnessContrast => [contrast,0,0,0,.5f*(1-contrast)+brightness, 0,contrast,0,0,.5f*(1-contrast)+brightness, 0,0,contrast,0,.5f*(1-contrast)+brightness, 0,0,0,1,0],
            AdjustmentKind.Saturation => Saturation(Math.Max(0, 1 + layer.Amount / 100)),
            _ => null
        };
        if (matrix is not null)
        {
            for (var i = 0; i < matrix.Length; i++)
            {
                matrix[i] = identity[i] + (matrix[i] - identity[i]) * opacity;
            }
            using var filter = SKColorFilter.CreateColorMatrix(matrix);
            paint.ColorFilter = filter;
        }
        if (layer.Adjustment == AdjustmentKind.GaussianBlur)
        {
            var radius = Math.Clamp(layer.Amount, 0, 32) * opacity;
            using var filter = SKImageFilter.CreateBlur(radius, radius);
            paint.ImageFilter = filter;
        }
        return paint;
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
        foreach (var entry in _entries.Values)
        {
            entry.Filter.Dispose();
        }
        _entries.Clear();
    }
}
