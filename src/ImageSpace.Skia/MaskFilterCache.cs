using System.Numerics;
using ImageSpace.Core;
using SkiaSharp;

namespace ImageSpace.Skia;

/// <summary>Two-stage cache: authored mask/feather source, then placement/density output graph. No readback.</summary>
internal sealed class MaskFilterCache : IDisposable
{
    private sealed record Source(PixelSurface Surface, long Revision, float Feather, SKImageFilter Filter);
    private sealed record Entry(Source Source, float Density, Matrix3x2 Transform, SKRect Bounds, SKImageFilter Filter);
    private readonly Dictionary<Guid, Source> _sources = [];
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly List<Guid> _stale = [];
    public long Builds
    {
        get; private set;
    }
    public long SourceBuilds
    {
        get; private set;
    }

    // Returned native graphs are borrowed. SKPaint/filter composition retains their native handles.
    public SKImageFilter Get(Layer layer, SKRect bounds, bool documentSpace,
        Action<SKCanvas, PixelSurface> drawTiles)
    {
        var mask = layer.Mask ?? throw new InvalidOperationException("This layer has no mask.");
        if (!_sources.TryGetValue(layer.Id, out var source) || !ReferenceEquals(source.Surface, mask) ||
            source.Revision != mask.Revision || source.Feather != layer.MaskFeather)
        {
            var extent = Math.Max(1, layer.MaskFeather * 4);
            using var recorder = new SKPictureRecorder();
            var canvas = recorder.BeginRecording(new SKRect(-extent, -extent, mask.Width + extent, mask.Height + extent));
            drawTiles(canvas, mask);
            using var picture = recorder.EndRecording();
            var raw = SKImageFilter.CreatePicture(picture)
                ?? throw new InvalidOperationException("Unable to create the authored mask graph.");
            SKImageFilter next = raw;
            try
            {
                if (layer.MaskFeather > 0)
                    next = SKImageFilter.CreateBlur(layer.MaskFeather, layer.MaskFeather, SKShaderTileMode.Decal, raw)
                        ?? throw new InvalidOperationException("Unable to create the feather graph.");
            }
            catch { raw.Dispose(); throw; }
            if (!ReferenceEquals(next, raw))
                raw.Dispose();
            source?.Filter.Dispose();
            source = new Source(mask, mask.Revision, layer.MaskFeather, next);
            _sources[layer.Id] = source;
            SourceBuilds++;
        }

        var transform = documentSpace ? layer.MaskDocumentTransform : layer.MaskLocalTransform;
        if (_entries.TryGetValue(layer.Id, out var cached) && ReferenceEquals(cached.Source, source) &&
            cached.Density == layer.MaskDensity && cached.Transform == transform && cached.Bounds == bounds)
            return cached.Filter;

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
        using var placed = !transform.IsIdentity
            ? SKImageFilter.CreateMatrix(in matrix, new SKSamplingOptions(SKFilterMode.Linear), source.Filter)
            : null;
        using var density = SKColorFilter.CreateColorMatrix([
            0,0,0,0,1, 0,0,0,0,1, 0,0,0,0,1,
            0,0,0,layer.MaskDensity,1-layer.MaskDensity]);
        var filter = SKImageFilter.CreateColorFilter(density, placed ?? source.Filter, bounds)
            ?? throw new InvalidOperationException("Unable to create the placed mask coverage graph.");
        cached?.Filter.Dispose();
        _entries[layer.Id] = new Entry(source, layer.MaskDensity, transform, bounds, filter);
        Builds++;
        return filter;
    }

    public void Prune(ImageDocument document)
    {
        _stale.Clear();
        foreach (var id in _sources.Keys)
        {
            var keep = false;
            foreach (var layer in document.Layers)
                if (layer.Id == id && layer.Mask is not null)
                {
                    keep = true;
                    break;
                }
            if (!keep)
                _stale.Add(id);
        }
        foreach (var id in _stale)
        {
            if (_entries.Remove(id, out var entry))
                entry.Filter.Dispose();
            if (_sources.Remove(id, out var source))
                source.Filter.Dispose();
        }
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
            entry.Filter.Dispose();
        foreach (var source in _sources.Values)
            source.Filter.Dispose();
        _entries.Clear();
        _sources.Clear();
        _stale.Clear();
    }
}
