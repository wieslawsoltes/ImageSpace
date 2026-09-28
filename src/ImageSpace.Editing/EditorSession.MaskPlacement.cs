using System.Numerics;
using ImageSpace.Core;

namespace ImageSpace.Editing;

public sealed partial class EditorSession
{
    public void SetMaskLinked(bool linked)
    {
        if (Document.ActiveLayer is not { Locked: false, Mask: not null } layer || layer.MaskLinked == linked)
            return;
        Execute(linked ? "Link layer mask" : "Unlink layer mask", _ => MaskGeometry.SetLinked(layer, linked));
    }

    /// <summary>Set an independent document-space mask frame. Linked masks must be unlinked first.</summary>
    public void SetMaskDocumentPlacement(AffinePlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        placement.Validate();
        if (Document.ActiveLayer is not { Locked: false, Mask: not null } layer)
            return;
        if (layer.MaskLinked)
            throw new InvalidOperationException("Unlink the mask before changing its independent placement.");
        if (layer.MaskPlacement == placement)
            return;
        Execute("Transform mask", _ => layer.MaskPlacement = placement);
    }

    public void AlignMaskToLayer()
    {
        if (Document.ActiveLayer is not { Locked: false, Mask: not null } layer)
            return;
        var placement = layer.MaskLinked ? AffinePlacement.Identity : AffinePlacement.FromMatrix(layer.Transform);
        if (layer.MaskPlacement == placement)
            return;
        Execute("Align mask to layer", _ => layer.MaskPlacement = placement);
    }

    /// <summary>Keyboard or programmatic movement. Linked pairs move together; unlinked targets move alone.</summary>
    public void NudgeActiveTarget(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y))
            throw new ArgumentException("Nudge offsets must be finite.");
        if (x == 0 && y == 0)
            return;
        if (Document.ActiveLayer is not { Locked: false } layer)
            return;
        var mask = Document.EditMask && layer.Mask is not null;
        if (!mask && layer.Kind == LayerKind.Adjustment)
            return;
        Execute(mask && !layer.MaskLinked ? "Nudge mask" : "Nudge layer", _ =>
        {
            if (mask && !layer.MaskLinked)
                layer.MaskPlacement = AffinePlacement.FromMatrix(layer.MaskDocumentTransform * Matrix3x2.CreateTranslation(x, y));
            else
            {
                layer.X += x;
                layer.Y += y;
            }
        });
    }
}
