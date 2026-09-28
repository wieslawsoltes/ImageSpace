using System.Numerics;
using ImageSpace.Core;
using SkiaSharp;

namespace ImageSpace.Skia;

/// <summary>Cached, resolution-independent mask pictures and native filter graphs. No raster readback.</summary>
internal sealed class MaskFilterCache : IDisposable
{
    private sealed record Entry(PixelSurface Surface, long Revision, float Density, float Feather,
        Matrix3x2 Transform, SKRect Bounds, SKImageFilter Filter);
    private readonly Dictionary<Guid, Entry> _entries = [];
    public long Builds { get; private set; }

    // The result is borrowed; callers retain it through their native paint/filter object, not Dispose().
    public SKImageFilter Get(Layer layer, SKRect bounds, bool documentSpace,
        Action<SKCanvas, PixelSurface> drawTiles)
    {
        var mask = layer.Mask ?? throw new InvalidOperationException("This layer has no mask.");
        var transform = documentSpace ? layer.Transform : Matrix3x2.Identity;
        if (_entries.TryGetValue(layer.Id, out var cached) && ReferenceEquals(cached.Surface, mask) &&
            cached.Revision == mask.Revision && cached.Density == layer.MaskDensity &&
            cached.Feather == layer.MaskFeather && cached.Transform == transform && cached.Bounds == bounds)
            return cached.Filter;

        // Feather in mask-local pixels before applying the layer transform. Expand the picture cull
        // bounds so blur samples are not clipped at the viewport edge before the kernel runs.
        var extent = Math.Max(1, layer.MaskFeather * 4);
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(new SKRect(-extent, -extent, mask.Width + extent, mask.Height + extent));
        drawTiles(canvas, mask);
        using var picture = recorder.EndRecording();
        using var raw = SKImageFilter.CreatePicture(picture);
        using var blurred = layer.MaskFeather > 0
            ? SKImageFilter.CreateBlur(layer.MaskFeather, layer.MaskFeather, SKShaderTileMode.Decal, raw)
            : null;
        var matrix = new SKMatrix
        {
            ScaleX = transform.M11, SkewX = transform.M21, TransX = transform.M31,
            SkewY = transform.M12, ScaleY = transform.M22, TransY = transform.M32, Persp2 = 1
        };
        using var placed = documentSpace
            ? SKImageFilter.CreateMatrix(in matrix, new SKSamplingOptions(SKFilterMode.Linear), blurred ?? raw)
            : null;
        // Density zero reveals everything, including transparent/unallocated parts of the mask.
        // Supplying a crop rect makes the alpha offset cover the entire output domain.
        using var density = SKColorFilter.CreateColorMatrix([
            0,0,0,0,1, 0,0,0,0,1, 0,0,0,0,1,
            0,0,0,layer.MaskDensity,1-layer.MaskDensity]);
        var filter = SKImageFilter.CreateColorFilter(density, placed ?? blurred ?? raw, bounds)
            ?? throw new InvalidOperationException("Unable to create the mask coverage filter.");
        cached?.Filter.Dispose();
        _entries[layer.Id] = new(mask, mask.Revision, layer.MaskDensity, layer.MaskFeather, transform, bounds, filter);
        Builds++;
        return filter;
    }

    public void Prune(ImageDocument document)
    {
        var ids = document.Layers.Where(layer => layer.Mask is not null).Select(layer => layer.Id).ToHashSet();
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
