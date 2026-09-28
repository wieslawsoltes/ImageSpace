using ImageSpace.Core;
using ImageSpace.Imaging;

namespace ImageSpace.Editing;

public sealed partial class EditorSession
{
    public void SelectMask(Guid layerId)
    {
        if (IsInTransaction)
            return;
        var layer = Document.Layers.FirstOrDefault(item => item.Id == layerId);
        if (layer?.Mask is null)
            return;
        if (Document.ActiveLayerId == layerId && Document.EditMask)
            return;
        Document.ActiveLayerId = layerId;
        Document.EditMask = true;
        Notify(EditorChange.ActiveTarget);
    }

    public void SetMaskProperties(float density, float feather)
    {
        if (!float.IsFinite(density) || density is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(density));
        if (!float.IsFinite(feather) || feather is < 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(feather));
        if (Document.ActiveLayer is not { Locked: false, Mask: not null } layer)
            return;
        if (layer.MaskDensity == density && layer.MaskFeather == feather)
            return;
        Execute("Mask properties", _ => { layer.MaskDensity = density; layer.MaskFeather = feather; });
    }

    public void SetMaskEnabled(bool enabled)
    {
        if (Document.ActiveLayer is not { Locked: false, Mask: not null } layer || layer.MaskEnabled == enabled)
            return;
        Execute(enabled ? "Enable mask" : "Disable mask", _ => layer.MaskEnabled = enabled);
    }

    public void DeleteMask()
    {
        if (Document.ActiveLayer is not { Locked: false, Mask: not null } layer)
            return;
        Execute("Delete mask", document =>
        {
            layer.Mask = null;
            layer.MaskLinked = true;
            layer.MaskPlacement = AffinePlacement.Identity;
            layer.MaskDensity = 1;
            layer.MaskFeather = 0;
            layer.MaskEnabled = true;
            document.EditMask = false;
        });
    }

    /// <summary>Invert all authored mask coverage. Unlike Ctrl+I, this mask-properties action ignores the pixel selection.</summary>
    public void InvertMask()
    {
        if (Document.ActiveLayer is not { Locked: false, Mask: not null } layer)
            return;
        var inverted = MaskOperations.Invert(layer.Mask);
        Execute("Invert mask", _ => layer.Mask = inverted);
    }

    public void FillPixels(Rgba32 color)
    {
        if (Document.ActiveLayer is not { Locked: false } layer || PixelTarget.Get(Document, layer) is null)
            return;
        Execute(Document.EditMask ? "Fill mask" : "Fill pixels", document => PixelEdits.Fill(document, layer, color));
    }
    public void LoadMaskSelection()
    {
        if (Document.ActiveLayer is not { Mask: not null } layer)
            return;
        var selection = MaskOperations.ToDocumentSelection(Document, layer);
        Execute("Select mask coverage", document => document.Selection = selection);
    }

}
