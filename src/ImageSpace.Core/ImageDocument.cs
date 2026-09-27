namespace ImageSpace.Core;

public sealed class ImageDocument
{
    private const int MaximumLayerExtent = 1_000_000;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled";
    public int Width { get; set; }
    public int Height { get; set; }
    public double Dpi { get; set; } = 72;
    /// <summary>Bottom-to-top painter order.</summary>
    public List<Layer> Layers { get; set; } = [];
    public Guid ActiveLayerId { get; set; }
    /// <summary>Null means unrestricted editing; an empty mask means nothing is selected.</summary>
    public PixelSurface? Selection { get; set; }
    public bool EditMask { get; set; }
    public Layer? ActiveLayer => Layers.FirstOrDefault(layer => layer.Id == ActiveLayerId);

    public ImageDocument(int width, int height, string name = "Untitled")
    {
        PixelSurface.ValidateSize(width, height);
        Width = width;
        Height = height;
        Name = name;
    }

    public float Coverage(int x, int y) => (uint)x >= (uint)Width || (uint)y >= (uint)Height
        ? 0 : Selection?.Get(x, y).A / 255f ?? 1f;

    public ImageDocument Snapshot() => new(Width, Height, Name)
    {
        Id = Id, Dpi = Dpi, ActiveLayerId = ActiveLayerId, EditMask = EditMask,
        Selection = Selection?.Snapshot(), Layers = Layers.Select(layer => layer.Snapshot()).ToList()
    };

    public void Validate()
    {
        PixelSurface.ValidateSize(Width, Height);
        if (Name is null || Name.Length > 1024 || Layers is null || Layers.Count > 128 || Layers.Any(layer => layer is null))
        {
            throw new InvalidDataException("Invalid document metadata or layer count.");
        }
        if (Layers.Select(layer => layer.Id).Distinct().Count() != Layers.Count)
        {
            throw new InvalidDataException("Duplicate layer IDs.");
        }
        if (!double.IsFinite(Dpi) || Dpi is < 1 or > 2400)
        {
            throw new InvalidDataException("Invalid DPI.");
        }
        foreach (var layer in Layers)
        {
            if (!Enum.IsDefined(layer.Kind) || !Enum.IsDefined(layer.Blend) || !Enum.IsDefined(layer.Adjustment))
            {
                throw new InvalidDataException("The document uses an unsupported layer or adjustment type.");
            }
            if (!float.IsFinite(layer.X) || !float.IsFinite(layer.Y) || Math.Abs(layer.X) > MaximumLayerExtent ||
                Math.Abs(layer.Y) > MaximumLayerExtent || !float.IsFinite(layer.ScaleX) || !float.IsFinite(layer.ScaleY) ||
                Math.Abs(layer.ScaleX) < 0.001 || Math.Abs(layer.ScaleY) < 0.001 ||
                Math.Abs(layer.ScaleX) > 10000 || Math.Abs(layer.ScaleY) > 10000 || !float.IsFinite(layer.Rotation) ||
                !float.IsFinite(layer.Opacity) || layer.Opacity is < 0 or > 1 ||
                !float.IsFinite(layer.Width) || !float.IsFinite(layer.Height) ||
                layer.Width is < 0 or > MaximumLayerExtent || layer.Height is < 0 or > MaximumLayerExtent ||
                !float.IsFinite(layer.FontSize) || layer.FontSize is < 1 or > 4096 ||
                !float.IsFinite(layer.StrokeWidth) || layer.StrokeWidth is < 0 or > 4096 ||
                !float.IsFinite(layer.CornerRadius) || layer.CornerRadius is < 0 or > MaximumLayerExtent ||
                !float.IsFinite(layer.Amount) || !float.IsFinite(layer.Secondary))
            {
                throw new InvalidDataException("Invalid layer geometry or effect parameters.");
            }
            if (layer.Text is null || layer.Name is null || layer.FontFamily is null ||
                layer.Text.Length > 100000 || layer.Name.Length > 1024 || layer.FontFamily.Length > 1024 ||
                layer.Curves is null || layer.Levels is null)
            {
                throw new InvalidDataException("Invalid layer text or tone settings.");
            }
            layer.Curves.Validate();
            layer.Levels.Validate();
        }
        if (Selection is not null && (Selection.Width != Width || Selection.Height != Height))
        {
            throw new InvalidDataException("Selection dimensions differ from the canvas.");
        }
        if (ActiveLayer is null && Layers.Count > 0)
        {
            ActiveLayerId = Layers[^1].Id;
        }
    }
}
