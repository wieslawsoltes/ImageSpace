using System.Numerics;
using System.Text.Json.Serialization;

namespace ImageSpace.Core;

/// <summary>Immutable, JSON-safe row-vector affine placement. No pixel resampling is implied.</summary>
public sealed record AffinePlacement(
    float M11 = 1, float M12 = 0, float M21 = 0, float M22 = 1, float X = 0, float Y = 0)
{
    public static AffinePlacement Identity { get; } = new();
    [JsonIgnore] public Matrix3x2 Matrix => new(M11, M12, M21, M22, X, Y);

    public static AffinePlacement FromMatrix(Matrix3x2 value)
    {
        var result = new AffinePlacement(value.M11, value.M12, value.M21, value.M22, value.M31, value.M32);
        result.Validate();
        return result;
    }

    public void Validate()
    {
        if (!float.IsFinite(M11) || !float.IsFinite(M12) || !float.IsFinite(M21) || !float.IsFinite(M22) ||
            !float.IsFinite(X) || !float.IsFinite(Y) || Math.Abs(X) > 1_000_000 || Math.Abs(Y) > 1_000_000 ||
            Math.Abs(M11) > 10000 || Math.Abs(M12) > 10000 || Math.Abs(M21) > 10000 || Math.Abs(M22) > 10000 ||
            Math.Abs((double)M11 * M22 - (double)M12 * M21) < 1e-12 ||
            !Matrix3x2.Invert(Matrix, out var inverse) ||
            !float.IsFinite(inverse.M11) || !float.IsFinite(inverse.M12) || !float.IsFinite(inverse.M21) ||
            !float.IsFinite(inverse.M22) || !float.IsFinite(inverse.M31) || !float.IsFinite(inverse.M32))
            throw new InvalidDataException("Mask placement must be finite, invertible, and within the supported geometry bounds.");
    }
}
