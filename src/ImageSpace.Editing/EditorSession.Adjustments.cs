using ImageSpace.Core;

namespace ImageSpace.Editing;

public sealed partial class EditorSession
{
    /// <summary>
    /// Inserts an adjustment above the current target. Inside a clipping unit the
    /// adjustment inherits that unit, preserving every existing member's base.
    /// </summary>
    public Layer AddAdjustment(AdjustmentKind kind)
    {
        if (!Enum.IsDefined(kind) || kind == AdjustmentKind.None)
            throw new ArgumentOutOfRangeException(nameof(kind));
        Layer? created = null;
        Execute("New " + kind + " adjustment", document =>
        {
            var active = document.ActiveLayer;
            var index = active is null ? document.Layers.Count : document.Layers.IndexOf(active) + 1;
            var clipped = active is not null && LayerClipping.IsMember(document.Layers, index - 1);
            created = new Layer
            {
                Name = kind.ToString(),
                Kind = LayerKind.Adjustment,
                Adjustment = kind,
                Amount = kind == AdjustmentKind.GaussianBlur ? 4 : 0,
                Width = document.Width,
                Height = document.Height,
                IsClipped = clipped
            };
            document.Layers.Insert(index, created);
            document.ActiveLayerId = created.Id;
            document.EditMask = false;
        });
        return created!;
    }
}
