using ImageSpace.Core;
using System.Numerics;
using ImageSpace.Imaging;
using ImageSpace.Filters;
namespace ImageSpace.Editing;

public sealed record HistoryEntry(string Name, ImageDocument Before, ImageDocument After, long BeforeVersion, long AfterVersion);
public sealed partial class EditorSession
{
    private readonly List<HistoryEntry> _undo = []; private readonly List<HistoryEntry> _redo = [];
    private ImageDocument? _before; private string _transaction = ""; private long _version; private long _nextVersion; private long _savedVersion;
    public ImageDocument Document
    {
        get; private set;
    }
    public long Revision
    {
        get; private set;
    }
    public bool IsDirty => _version != _savedVersion || _before is not null;
    public bool IsInTransaction => _before is not null;
    public bool CanUndo => _undo.Count > 0; public bool CanRedo => _redo.Count > 0;
    public IReadOnlyList<HistoryEntry> History => _undo;
    public long HistoryBudget { get; set; } = 192L * 1024 * 1024;
    public event EventHandler? Changed;
    public EditorSession(ImageDocument document)
    {
        document.Validate();
        Document = document;
    }
    /// <summary>Changes requiring recovery; active-target selection and saving do not advance it.</summary>
    public long ContentRevision
    {
        get; private set;
    }
    public void Notify(EditorChange change = EditorChange.Document)
    {
        if (!Enum.IsDefined(change))
            throw new ArgumentOutOfRangeException(nameof(change));
        Revision++;
        if (change == EditorChange.Document)
            ContentRevision++;
        Changed?.Invoke(this, EditorChangedEventArgs.For(change));
    }
    public void Begin(string name)
    {
        if (_before is not null)
            throw new InvalidOperationException("An edit is already in progress.");
        _transaction = name;
        _before = Document.Snapshot();
    }
    public void Commit()
    {
        if (_before is null)
            return;
        try
        {
            Document.Validate();
        }
        catch { Cancel(); throw; }
        var next = ++_nextVersion;
        _undo.Add(new(_transaction, _before, Document.Snapshot(), _version, next));
        _version = next;
        _before = null;
        _redo.Clear();
        while (_undo.Count > 1 && (_undo.Count > 64 || EstimateHistoryBytes() > HistoryBudget))
            _undo.RemoveAt(0);
        Notify();
    }
    public void Cancel()
    {
        if (_before is null)
            return;
        Document = _before;
        _before = null;
        Notify();
    }
    public void Execute(string name, Action<ImageDocument> action)
    {
        Begin(name);
        try
        {
            action(Document);
            Commit();
        }
        catch { Cancel(); throw; }
    }
    public void Undo()
    {
        if (IsInTransaction)
        {
            Cancel();
            return;
        }
        if (_undo.Count == 0)
            return;
        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(entry);
        Document = entry.Before.Snapshot();
        _version = entry.BeforeVersion;
        Notify();
    }
    public void Redo()
    {
        if (IsInTransaction || _redo.Count == 0)
            return;
        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(entry);
        Document = entry.After.Snapshot();
        _version = entry.AfterVersion;
        Notify();
    }
    public void MarkSaved()
    {
        if (_savedVersion == _version)
            return;
        _savedVersion = _version;
        Notify(EditorChange.SavedState);
    }
    public void SelectLayer(Guid id)
    {
        if (IsInTransaction)
            return;
        if (Document.ActiveLayerId == id && !Document.EditMask)
            return;
        if (Document.Layers.Any(l => l.Id == id))
        {
            Document.ActiveLayerId = id;
            Document.EditMask = false;
            Notify(EditorChange.ActiveTarget);
        }
    }
    public Layer AddLayer(string name = "Layer")
    {
        Layer? layer = null;
        Execute("New layer", d => { layer = Layer.Raster(name, d.Width, d.Height); d.Layers.Add(layer); d.ActiveLayerId = layer.Id; d.EditMask = false; });
        return layer!;
    }
    public void DuplicateLayer()
    {
        var layer = Document.ActiveLayer;
        if (layer is null)
            return;
        if (DuplicateClippingBase(layer))
            return;
        Execute("Duplicate layer", d => { var copy = layer.Snapshot(); copy.Id = Guid.NewGuid(); copy.Name += " copy"; d.Layers.Insert(d.Layers.IndexOf(layer) + 1, copy); d.ActiveLayerId = copy.Id; });
    }
    public void DeleteLayer()
    {
        var layer = Document.ActiveLayer;
        if (layer is null || layer.Locked)
            return;
        Execute("Delete layer", d => { var i = d.Layers.IndexOf(layer); ReleaseDeletedBase(i); d.Layers.Remove(layer); d.ActiveLayerId = d.Layers.Count == 0 ? Guid.Empty : d.Layers[Math.Clamp(i - 1, 0, d.Layers.Count - 1)].Id; d.EditMask = false; });
    }
    public void MoveLayer(int delta) => MoveLayerStack(delta);
    public void AddMask()
    {
        if (Document.ActiveLayer is not { Locked: false } layer || layer.Mask is not null)
            return;
        // Allocate/validate first. A failed allocation cannot leave a half-created history entry.
        var mask = MaskOperations.FromSelection(Document, layer);
        Execute("Add layer mask", document =>
        {
            layer.Mask = mask;
            layer.MaskLinked = true;
            layer.MaskPlacement = AffinePlacement.Identity;
            layer.MaskDensity = 1;
            layer.MaskFeather = 0;
            layer.MaskEnabled = true;
            document.EditMask = true;
        });
    }
    public void ApplyFilter(FilterKind kind, float amount = 0, float secondary = 0)
    {
        var layer = Document.ActiveLayer ?? throw new InvalidOperationException("Select a layer first.");
        var source = PixelTarget.RequireEditable(Document, layer).Snapshot();
        var output = PixelFilterPipeline.Apply(source, Document.EditMask, kind, amount, secondary);
        Execute(kind.ToString(), document => PixelTarget.Replace(document, layer,
            PixelEdits.RestrictToSelection(document, layer, source, output)));
    }
    public void ClearPixels()
    {
        if (Document.ActiveLayer is not { Locked: false } layer || PixelTarget.Get(Document, layer) is null)
            return;
        Execute(Document.EditMask ? "Clear mask coverage" : "Clear pixels", document => PixelEdits.Clear(document, layer));
    }
    public void Crop(int x, int y, int width, int height)
    {
        PixelSurface.ValidateSize(width, height);
        Execute("Crop canvas", d => { d.Width = width; d.Height = height; foreach (var l in d.Layers) { l.X -= x; l.Y -= y; MaskGeometry.TransformUnlinked(l, Matrix3x2.CreateTranslation(-x, -y)); } d.Selection = null; });
    }
    public void ResizeImage(int width, int height)
    {
        PixelSurface.ValidateSize(width, height);
        Execute("Image size", d => { var sx = (float)width / d.Width; var sy = (float)height / d.Height; foreach (var l in d.Layers) { l.X *= sx; l.Y *= sy; l.ScaleX *= sx; l.ScaleY *= sy; MaskGeometry.TransformUnlinked(l, Matrix3x2.CreateScale(sx, sy)); } d.Width = width; d.Height = height; d.Selection = null; });
    }
    public void RotateCanvas(bool clockwise)
    {
        Execute("Rotate canvas", d =>
        {
            var w = d.Width;
            var h = d.Height;
            foreach (var l in d.Layers)
            {
                var x = l.X;
                var y = l.Y;
                l.X = clockwise ? h - y : y;
                l.Y = clockwise ? x : w - x;
                l.Rotation += clockwise ? 90 : -90;
                MaskGeometry.TransformUnlinked(l, clockwise ? new Matrix3x2(0, 1, -1, 0, h, 0) : new Matrix3x2(0, -1, 1, 0, 0, w));
            }
            d.Width = h;
            d.Height = w;
            d.Selection = null;
        });
    }
    private long EstimateHistoryBytes()
    {
        var tiles = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var state in _undo.SelectMany(e => new[] { e.Before, e.After }).Append(Document))
        {
            foreach (var surface in state.Layers.SelectMany(l => new[] { l.Pixels, l.Mask }).Append(state.Selection))
                if (surface is not null)
                    foreach (var t in surface.EnumerateTiles())
                        tiles.Add(t.Identity);
        }
        return (long)tiles.Count * PixelSurface.TileSize * PixelSurface.TileSize * 4;
    }
}
