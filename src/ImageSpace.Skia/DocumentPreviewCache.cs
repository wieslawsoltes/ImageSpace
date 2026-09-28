using ImageSpace.Core;

namespace ImageSpace.Skia;

/// <summary>Single-entry, bounded preview cache for a document or adjustment input prefix.
/// Use only on the owning UI/render thread. Never shares mutable native caches with the viewport.</summary>
public sealed class DocumentPreviewCache : IDisposable
{
    private readonly ImageRenderer _renderer = new();
    private readonly List<LayerRenderStamp> _stamps = [];
    private ImageRenderer? _fontSource;
    private long _fontRevision = -1;
    private Guid _documentId;
    private int _width, _height, _maximumEdge;
    private PixelSurface? _pixels;
    private bool _disposed;
    public long Builds
    {
        get; private set;
    }

    public PixelSurface Get(ImageDocument document, int layerCount, int maximumEdge, ImageRenderer fontSource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fontSource);
        if ((uint)layerCount > (uint)document.Layers.Count)
            throw new ArgumentOutOfRangeException(nameof(layerCount));
        if (maximumEdge is < 1 or > 512)
            throw new ArgumentOutOfRangeException(nameof(maximumEdge));
        var fontChanged = !ReferenceEquals(_fontSource, fontSource) || _fontRevision != fontSource.TypefaceRevision;
        var changed = fontChanged || _pixels is null || _documentId != document.Id || _width != document.Width ||
            _height != document.Height || _maximumEdge != maximumEdge || _stamps.Count != layerCount;
        if (!changed)
            for (var i = 0; i < layerCount; i++)
                if (_stamps[i] != LayerRenderStamp.Capture(document.Layers[i]))
                {
                    changed = true;
                    break;
                }
        if (!changed)
            return _pixels!;
        if (fontChanged)
        {
            _renderer.CopyTypefaceFrom(fontSource);
            _fontSource = fontSource;
            _fontRevision = fontSource.TypefaceRevision;
        }
        // Borrow read-only model content on the single writer's thread. No COW snapshots.
        var prefix = new ImageDocument(document.Width, document.Height) { Layers = document.Layers.GetRange(0, layerCount) };
        var pixels = _renderer.RasterizePreview(prefix, maximumEdge);
        _stamps.Clear();
        for (var i = 0; i < layerCount; i++)
            _stamps.Add(LayerRenderStamp.Capture(document.Layers[i]));
        _documentId = document.Id;
        _width = document.Width;
        _height = document.Height;
        _maximumEdge = maximumEdge;
        _pixels = pixels;
        Builds++;
        return pixels;
    }

    /// <summary>Release borrowed model references and cached native graphs on document switches.</summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _pixels = null;
        _stamps.Clear();
        _renderer.Prune(new ImageDocument(1, 1));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pixels = null;
        _stamps.Clear();
        _fontSource = null;
        _renderer.Dispose();
    }
}
