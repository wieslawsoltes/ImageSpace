using ImageSpace.Core;

namespace ImageSpace.Editing;

public sealed partial class EditorSession
{
    public bool CanApplyLayerMask => !IsInTransaction && Document.ActiveLayer is
        { Kind: LayerKind.Raster, Locked: false, Pixels: not null, Mask: not null, MaskEnabled: true };

    /// <summary>
    /// Bakes a raster mask using a host-supplied renderer and commits one undoable edit.
    /// The renderer receives a detached copy-on-write snapshot. Invalid or stale output
    /// is rejected before a history entry is opened; selection is not consumed.
    /// </summary>
    public void ApplyLayerMask(Func<Layer, PixelSurface> renderMaskedPixels)
    {
        ArgumentNullException.ThrowIfNull(renderMaskedPixels);
        if (!CanApplyLayerMask)
            throw new InvalidOperationException("Select an unlocked raster layer with an enabled mask and finish the current gesture first.");
        var layer = Document.ActiveLayer!;
        var revision = Revision;
        var sourceWidth = layer.Pixels!.Width;
        var sourceHeight = layer.Pixels.Height;
        var output = renderMaskedPixels(layer.Snapshot())
            ?? throw new InvalidOperationException("Mask application produced no pixel surface.");
        if (Revision != revision || IsInTransaction || !ReferenceEquals(Document.ActiveLayer, layer) || !CanApplyLayerMask)
            throw new InvalidOperationException("The document changed while applying its mask. No mask result was committed.");
        if (output.Width != sourceWidth || output.Height != sourceHeight)
            throw new InvalidOperationException("Mask application must preserve the authored pixel dimensions.");

        Execute("Apply layer mask", document =>
        {
            // Detach ownership from the renderer's result as well as the pre-edit state.
            layer.Pixels = output.Snapshot();
            layer.Mask = null;
            layer.MaskLinked = true;
            layer.MaskPlacement = AffinePlacement.Identity;
            layer.MaskEnabled = true;
            layer.MaskDensity = 1;
            layer.MaskFeather = 0;
            document.EditMask = false;
        });
    }
}
