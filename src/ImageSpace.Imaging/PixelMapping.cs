using System.Numerics;
using ImageSpace.Core;

namespace ImageSpace.Imaging;

/// <summary>
/// Operation-local coordinate snapshot. Prepare once, then sample without rebuilding trigonometric
/// transforms per pixel. The caller retains the document's single-writer editing discipline.
/// </summary>
public readonly struct PixelMapping
{
    private readonly int _width, _height;
    private readonly PixelSurface? _selection;
    public Matrix3x2 ToDocumentMatrix
    {
        get;
    }
    public Matrix3x2 ToLocalMatrix
    {
        get;
    }

    public PixelMapping(ImageDocument document, Matrix3x2 transform)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!Matrix3x2.Invert(transform, out var inverse))
            throw new InvalidOperationException("The editing transform cannot be inverted.");
        ToDocumentMatrix = transform;
        ToLocalMatrix = inverse;
        _width = document.Width;
        _height = document.Height;
        _selection = document.Selection;
    }

    public Vector2 ToDocument(Vector2 point) => Vector2.Transform(point, ToDocumentMatrix);
    public Vector2 ToLocal(Vector2 point) => Vector2.Transform(point, ToLocalMatrix);
    public float Coverage(int x, int y)
    {
        var point = ToDocument(new Vector2(x + .5f, y + .5f));
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || point.X < 0 || point.Y < 0 ||
            point.X >= _width || point.Y >= _height)
            return 0;
        return _selection?.Get((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y)).A / 255f ?? 1;
    }
}
