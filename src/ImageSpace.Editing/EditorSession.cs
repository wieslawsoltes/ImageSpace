using ImageSpace.Core;
using ImageSpace.Imaging;
using ImageSpace.Filters;
namespace ImageSpace.Editing;

public sealed record HistoryEntry(string Name,ImageDocument Before,ImageDocument After,long BeforeVersion,long AfterVersion);
public sealed class EditorSession
{
    private readonly List<HistoryEntry> _undo=[];private readonly List<HistoryEntry> _redo=[];
    private ImageDocument? _before;private string _transaction="";private long _version;private long _nextVersion;private long _savedVersion;
    public ImageDocument Document {get;private set;}
    public long Revision {get;private set;}
    public bool IsDirty=>_version!=_savedVersion || _before is not null;
    public bool IsInTransaction=>_before is not null;
    public bool CanUndo=>_undo.Count>0;public bool CanRedo=>_redo.Count>0;
    public IReadOnlyList<HistoryEntry> History=>_undo;
    public long HistoryBudget {get;set;}=192L*1024*1024;
    public event EventHandler? Changed;
    public EditorSession(ImageDocument document){document.Validate();Document=document;}
    public void Notify(){Revision++;Changed?.Invoke(this,EventArgs.Empty);}
    public void Begin(string name){if(_before is not null)throw new InvalidOperationException("An edit is already in progress.");_transaction=name;_before=Document.Snapshot();}
    public void Commit()
    {
        if(_before is null)return;
        try{Document.Validate();}catch{Cancel();throw;}
        var next=++_nextVersion;_undo.Add(new(_transaction,_before,Document.Snapshot(),_version,next));_version=next;_before=null;_redo.Clear();
        while(_undo.Count>1 && (_undo.Count>64 || EstimateHistoryBytes()>HistoryBudget))_undo.RemoveAt(0);
        Notify();
    }
    public void Cancel(){if(_before is null)return;Document=_before;_before=null;Notify();}
    public void Execute(string name,Action<ImageDocument> action)
    {
        Begin(name);try{action(Document);Commit();}catch{Cancel();throw;}
    }
    public void Undo(){if(IsInTransaction){Cancel();return;}if(_undo.Count==0)return;var entry=_undo[^1];_undo.RemoveAt(_undo.Count-1);_redo.Add(entry);Document=entry.Before.Snapshot();_version=entry.BeforeVersion;Notify();}
    public void Redo(){if(IsInTransaction||_redo.Count==0)return;var entry=_redo[^1];_redo.RemoveAt(_redo.Count-1);_undo.Add(entry);Document=entry.After.Snapshot();_version=entry.AfterVersion;Notify();}
    public void MarkSaved(){_savedVersion=_version;Notify();}
    public void SelectLayer(Guid id){if(IsInTransaction)return;if(Document.Layers.Any(l=>l.Id==id)){Document.ActiveLayerId=id;Document.EditMask=false;Notify();}}
    public Layer AddLayer(string name="Layer")
    {
        Layer? layer=null;Execute("New layer",d=>{layer=Layer.Raster(name,d.Width,d.Height);d.Layers.Add(layer);d.ActiveLayerId=layer.Id;d.EditMask=false;});return layer!;
    }
    public void DuplicateLayer(){var layer=Document.ActiveLayer;if(layer is null)return;Execute("Duplicate layer",d=>{var copy=layer.Snapshot();copy.Id=Guid.NewGuid();copy.Name+=" copy";d.Layers.Insert(d.Layers.IndexOf(layer)+1,copy);d.ActiveLayerId=copy.Id;});}
    public void DeleteLayer(){var layer=Document.ActiveLayer;if(layer is null||layer.Locked)return;Execute("Delete layer",d=>{var i=d.Layers.IndexOf(layer);d.Layers.Remove(layer);d.ActiveLayerId=d.Layers.Count==0?Guid.Empty:d.Layers[Math.Clamp(i-1,0,d.Layers.Count-1)].Id;d.EditMask=false;});}
    public void MoveLayer(int delta){var layer=Document.ActiveLayer;if(layer is null)return;var index=Document.Layers.IndexOf(layer);var target=Math.Clamp(index+delta,0,Document.Layers.Count-1);if(target==index)return;Execute("Reorder layer",d=>{d.Layers.RemoveAt(index);d.Layers.Insert(target,layer);});}
    public void AddMask(){var layer=Document.ActiveLayer;if(layer is null||layer.Locked)return;Execute("Add layer mask",d=>{var w=layer.Pixels?.Width??(int)Math.Max(1,layer.Width);var h=layer.Pixels?.Height??(int)Math.Max(1,layer.Height);var mask=new PixelSurface(w,h);for(var y=0;y<h;y++)for(var x=0;x<w;x++){var p=layer.ToDocument(new(x,y));mask.Set(x,y,new(255,255,255,Rgba32.Byte(d.Coverage((int)p.X,(int)p.Y)*255)));}layer.Mask=mask;d.EditMask=true;});}
    public void ApplyFilter(FilterKind kind,float amount=0,float secondary=0)
    {
        var layer=Document.ActiveLayer;if(layer?.Pixels is null||layer.Locked)throw new InvalidOperationException("Select an unlocked pixel layer. Rasterize text or shapes first.");
        Execute(kind.ToString(),d=>{var source=d.EditMask?layer.Mask:layer.Pixels;if(source is null)return;var filtered=FilterEngine.Apply(source,kind,amount,secondary);if(d.Selection is not null){for(var y=0;y<source.Height;y++)for(var x=0;x<source.Width;x++){var p=layer.ToDocument(new(x,y));var coverage=d.Coverage((int)p.X,(int)p.Y);filtered.Set(x,y,Rgba32.Lerp(source.Get(x,y),filtered.Get(x,y),coverage));}}if(d.EditMask)layer.Mask=filtered;else layer.Pixels=filtered;});
    }
    public void ClearPixels()
    {
        var layer=Document.ActiveLayer;if(layer?.Pixels is null||layer.Locked)return;
        Execute("Clear pixels",d=>{var target=d.EditMask?layer.Mask:layer.Pixels;if(target is null)return;for(var y=0;y<target.Height;y++)for(var x=0;x<target.Width;x++){var p=layer.ToDocument(new(x,y));var old=target.Get(x,y);target.Set(x,y,old.WithAlpha(Rgba32.Byte(old.A*(1-d.Coverage((int)p.X,(int)p.Y)))));}});
    }
    public void Crop(int x,int y,int width,int height)
    {
        PixelSurface.ValidateSize(width,height);Execute("Crop canvas",d=>{d.Width=width;d.Height=height;foreach(var l in d.Layers){l.X-=x;l.Y-=y;}d.Selection=null;});
    }
    public void ResizeImage(int width,int height)
    {
        PixelSurface.ValidateSize(width,height);Execute("Image size",d=>{var sx=(float)width/d.Width;var sy=(float)height/d.Height;foreach(var l in d.Layers){l.X*=sx;l.Y*=sy;l.ScaleX*=sx;l.ScaleY*=sy;}d.Width=width;d.Height=height;d.Selection=null;});
    }
    public void RotateCanvas(bool clockwise)
    {
        Execute("Rotate canvas",d=>{var w=d.Width;var h=d.Height;foreach(var l in d.Layers){var x=l.X;var y=l.Y;l.X=clockwise?h-y:y;l.Y=clockwise?x:w-x;l.Rotation+=clockwise?90:-90;}d.Width=h;d.Height=w;d.Selection=null;});
    }
    private long EstimateHistoryBytes()
    {
        var tiles=new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach(var state in _undo.SelectMany(e=>new[]{e.Before,e.After}).Append(Document))
        {
            foreach(var surface in state.Layers.SelectMany(l=>new[]{l.Pixels,l.Mask}).Append(state.Selection))
                if(surface is not null)foreach(var t in surface.EnumerateTiles())tiles.Add(t.Identity);
        }
        return (long)tiles.Count*PixelSurface.TileSize*PixelSurface.TileSize*4;
    }
}
