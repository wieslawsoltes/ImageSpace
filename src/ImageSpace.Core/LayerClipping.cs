namespace ImageSpace.Core;

/// <summary>Allocation-free queries over bottom-to-top clipping chains. No pixel or model mutations.</summary>
public static class LayerClipping
{
    public static int FindBaseIndex(IReadOnlyList<Layer> layers, int index)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if ((uint)index >= (uint)layers.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        while (index >= 0 && layers[index].IsClipped)
            index--;
        return index >= 0 && layers[index].Kind != LayerKind.Adjustment ? index : -1;
    }

    public static int FindEndIndex(IReadOnlyList<Layer> layers, int index)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if ((uint)index >= (uint)layers.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        while (index + 1 < layers.Count && layers[index + 1].IsClipped)
            index++;
        return index;
    }

    public static bool IsBase(IReadOnlyList<Layer> layers, int index) =>
        (uint)index < (uint)layers.Count && !layers[index].IsClipped &&
        index + 1 < layers.Count && layers[index + 1].IsClipped;

    public static bool IsMember(IReadOnlyList<Layer> layers, int index) =>
        (uint)index < (uint)layers.Count && (layers[index].IsClipped || IsBase(layers, index));

    public static void Validate(IReadOnlyList<Layer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        Layer? basis = null;
        foreach (var layer in layers)
        {
            if (!layer.IsClipped)
                basis = layer.Kind == LayerKind.Adjustment ? null : layer;
            else if (basis is null)
                throw new InvalidDataException("A clipping chain requires a preceding non-clipped raster, text or shape base.");
        }
    }
}
