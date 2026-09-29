using System.Numerics;
using System.Text.Json.Serialization;

namespace ImageSpace.Core;

public enum LayerKind
{
    Raster, Text, Rectangle, Ellipse, Adjustment
}
public enum LayerBlend
{
    Normal, Multiply, Screen, Overlay, Darken, Lighten, ColorDodge, ColorBurn,
    HardLight, SoftLight, Difference, Exclusion, Hue, Saturation, Color, Luminosity
}
public enum AdjustmentKind
{
    None, BrightnessContrast, Saturation, Invert, Grayscale, Sepia, GaussianBlur, Sharpen,
    Curves, Levels
}

public sealed class Layer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Layer";
    public LayerKind Kind
    {
        get; set;
    }
    public bool Visible { get; set; } = true;
    public bool Locked
    {
        get; set;
    }
    /// <summary>Clip this layer to the nearest preceding non-clipped content layer.
    /// Consecutive clipped layers form a single alpha-preserving compositing group.</summary>
    public bool IsClipped
    {
        get; set;
    }
    public float Opacity { get; set; } = 1;
    public LayerBlend Blend
    {
        get; set;
    }
    public float X
    {
        get; set;
    }
    public float Y
    {
        get; set;
    }
    public float ScaleX { get; set; } = 1;
    public float ScaleY { get; set; } = 1;
    public float Rotation
    {
        get; set;
    }
    public float Width { get; set; } = 200;
    public float Height { get; set; } = 140;
    public Rgba32 Color { get; set; } = new(54, 145, 230);
    public Rgba32 StrokeColor { get; set; } = Rgba32.White;
    public float StrokeWidth
    {
        get; set;
    }
    public float CornerRadius
    {
        get; set;
    }
    public string Text { get; set; } = "Your text";
    public string FontFamily { get; set; } = "Inter";
    public float FontSize { get; set; } = 64;
    public bool Bold
    {
        get; set;
    }
    public PixelSurface? Pixels
    {
        get; set;
    }
    public PixelSurface? Mask
    {
        get; set;
    }
    /// <summary>Linked placements are layer-local; unlinked placements are document-local.</summary>
    public bool MaskLinked { get; set; } = true;
    public AffinePlacement MaskPlacement { get; set; } = AffinePlacement.Identity;
    [JsonIgnore] public Matrix3x2 MaskDocumentTransform => MaskLinked ? MaskPlacement.Matrix * Transform : MaskPlacement.Matrix;
    [JsonIgnore]
    public Matrix3x2 MaskLocalTransform
    {
        get
        {
            if (MaskLinked)
                return MaskPlacement.Matrix;
            if (!Matrix3x2.Invert(Transform, out var inverse))
                throw new InvalidOperationException("The layer transform cannot be inverted.");
            return MaskPlacement.Matrix * inverse;
        }
    }

    public bool MaskEnabled { get; set; } = true;
    /// <summary>One applies the authored mask; zero reveals the entire layer/effect.</summary>
    public float MaskDensity { get; set; } = 1;
    /// <summary>Non-destructive Gaussian sigma in mask-local pixels, applied before its transform.</summary>
    public float MaskFeather
    {
        get; set;
    }
    public AdjustmentKind Adjustment
    {
        get; set;
    }
    public float Amount
    {
        get; set;
    }
    public float Secondary
    {
        get; set;
    }
    public CurvesAdjustment Curves { get; set; } = new();
    public LevelsAdjustment Levels { get; set; } = new();

    public Matrix3x2 Transform => Matrix3x2.CreateScale(ScaleX, ScaleY)
        * Matrix3x2.CreateRotation(Rotation * MathF.PI / 180) * Matrix3x2.CreateTranslation(X, Y);
    public Vector2 ToLocal(Vector2 point) => Matrix3x2.Invert(Transform, out var inverse)
        ? Vector2.Transform(point, inverse) : point;
    public Vector2 ToDocument(Vector2 point) => Vector2.Transform(point, Transform);

    public Layer Snapshot()
    {
        var result = (Layer)MemberwiseClone();
        result.Pixels = Pixels?.Snapshot();
        result.Mask = Mask?.Snapshot();
        // Curves and Levels are immutable records, including their point collections.
        return result;
    }

    public static Layer Raster(string name, int width, int height) => new()
    {
        Name = name,
        Kind = LayerKind.Raster,
        Width = width,
        Height = height,
        Pixels = new PixelSurface(width, height)
    };
}
