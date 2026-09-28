using ImageSpace.Core;
using ImageSpace.Filters;
using ImageSpace.Imaging;

namespace ImageSpace.Editing;

public static class PixelFilterPipeline
{
    /// <summary>Runs on a private snapshot. No session/UI access; safe for host-selected worker execution.</summary>
    public static PixelSurface Apply(PixelSurface source, bool mask, FilterKind kind, float amount = 0, float secondary = 0)
    {
        if (!float.IsFinite(amount) || !float.IsFinite(secondary))
            throw new ArgumentException("Filter parameters must be finite.");
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!mask) return FilterEngine.Apply(source, kind, amount, secondary);
        if (kind == FilterKind.Invert) return MaskOperations.Invert(source);
        return MaskOperations.FromGrayscale(FilterEngine.Apply(MaskOperations.ToGrayscale(source), kind, amount, secondary));
    }
}
