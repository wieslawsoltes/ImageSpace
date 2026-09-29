namespace ImageSpace.Filters;

/// <summary>Immutable parameters for an ordered RGBA8 operation. Disabled steps retain their position.</summary>
public sealed record FilterOperation(FilterKind Kind, float Amount = 0, float Secondary = 0, bool Enabled = true)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Kind) || !float.IsFinite(Amount) || !float.IsFinite(Secondary) ||
            Math.Abs(Amount) > 1_000_000 || Math.Abs(Secondary) > 1_000_000)
            throw new ArgumentException("Invalid filter kind or parameters.");
    }

    public static FilterOperation Default(FilterKind kind) => new(kind, kind switch
    {
        FilterKind.GaussianBlur => 3,
        FilterKind.Gamma or FilterKind.Sharpen => 1,
        FilterKind.Threshold => 128,
        FilterKind.Posterize => 5,
        FilterKind.Pixelate or FilterKind.Noise => 12,
        _ => 0
    });
}

public static class FilterRecipe
{
    public const int MaximumOperations = 16;
    public static FilterOperation[] Capture(IReadOnlyList<FilterOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        if (operations.Count > MaximumOperations)
            throw new ArgumentException("A filter stack supports at most sixteen operations.");
        var result = new FilterOperation[operations.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = operations[i] ?? throw new ArgumentException("A filter operation is null.");
            result[i].Validate();
        }
        return result;
    }

    /// <summary>Spatial amounts are scaled for a sampled preview; final application always uses original parameters.</summary>
    public static FilterOperation[] ForPreview(IReadOnlyList<FilterOperation> operations, float scale)
    {
        if (!float.IsFinite(scale) || scale is <= 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(scale));
        return Capture(operations).Select(op => op.Kind switch
        {
            FilterKind.GaussianBlur => op with { Amount = op.Amount * scale },
            FilterKind.Pixelate => op with { Amount = Math.Max(2, op.Amount * scale) },
            _ => op
        }).ToArray();
    }
}
