namespace ImageSpace.Core;

public sealed class ImageDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled";
    public int Width { get; set; }
    public int Height { get; set; }
    public double Dpi { get; set; } = 72;
    /// <summary>Bottom-to-top painter order.</summary>
    public List<Layer> Layers { get; set; } = [];
    public Guid ActiveLayerId { get; set; }
    /// <summary>Null means no selection (all editable). Non-null may deliberately be empty.</summary>
    public PixelSurface? Selection { get; set; }
    public bool EditMask { get; set; }
    public Layer? ActiveLayer => Layers.FirstOrDefault(x => x.Id == ActiveLayerId);
    public ImageDocument(int width,int height,string name = "Untitled") { PixelSurface.ValidateSize(width,height); Width=width;Height=height;Name=name; }
    public float Coverage(int x,int y) => (uint)x >= (uint)Width || (uint)y >= (uint)Height ? 0 : Selection?.Get(x,y).A / 255f ?? 1f;
    public ImageDocument Snapshot() => new(Width,Height,Name) { Id=Id,Dpi=Dpi,ActiveLayerId=ActiveLayerId,EditMask=EditMask,Selection=Selection?.Snapshot(),Layers=Layers.Select(x=>x.Snapshot()).ToList() };
    public void Validate()
    {
        PixelSurface.ValidateSize(Width,Height);
        if(Layers.Count>128) throw new InvalidDataException("At most 128 layers are supported.");
        if(Layers.Select(x=>x.Id).Distinct().Count()!=Layers.Count) throw new InvalidDataException("Duplicate layer IDs.");
        if(!double.IsFinite(Dpi)||Dpi<1||Dpi>2400) throw new InvalidDataException("Invalid DPI.");
        foreach(var l in Layers)
        {
            if(!float.IsFinite(l.X)||!float.IsFinite(l.Y)||!float.IsFinite(l.ScaleX)||!float.IsFinite(l.ScaleY)||Math.Abs(l.ScaleX)<0.001||Math.Abs(l.ScaleY)<0.001||!float.IsFinite(l.Rotation)||!float.IsFinite(l.Opacity)||l.Opacity<0||l.Opacity>1||!float.IsFinite(l.Width)||!float.IsFinite(l.Height)||l.Width<0||l.Height<0||l.Width>MaximumLayerExtent||l.Height>MaximumLayerExtent||!float.IsFinite(l.FontSize)||l.FontSize<1||l.FontSize>4096||!float.IsFinite(l.Amount)||!float.IsFinite(l.Secondary)) throw new InvalidDataException("Invalid layer geometry or effect parameters.");
            if(l.Text.Length>100000 || l.Name.Length>1024) throw new InvalidDataException("Layer text exceeds limits.");
        }
        if(Selection is not null && (Selection.Width!=Width || Selection.Height!=Height)) throw new InvalidDataException("Selection dimensions differ from the canvas.");
        if(ActiveLayer is null && Layers.Count>0) ActiveLayerId=Layers[^1].Id;
    }
    private const int MaximumLayerExtent = 1_000_000;
}
