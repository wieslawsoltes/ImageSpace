using ImageSpace.Skia;

namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private void ApplyLayerMask() => Run(() =>
    {
        Session.ApplyLayerMask(Surface.Renderer.BakeLayerMask);
        Surface.SetMaskPreview(MaskPreviewMode.Composite);
    });

    private const string MaskApplicationHelp = """
        APPLY A LAYER MASK

        Select an unlocked pixel layer with an enabled mask. Choose Layer → Apply layer mask.
        Effective mask coverage, including density, feather and affine placement, is multiplied
        into the source pixel alpha. The separate mask is then removed in one undoable edit.

        Original RGB values, authored pixel dimensions, off-canvas pixels, layer position,
        scale, rotation, blend mode and opacity are retained. The current selection is ignored:
        this command applies the whole mask, not just selected pixels. Native Save preserves
        the resulting pixels. Undo restores the editable mask and its original placement.

        Enable a disabled mask before applying it. Use Delete mask to discard a mask without
        applying its effect. Adjustment layers are not eligible; applying an effect mask to
        an adjustment requires a different compositing operation. This command accepts raster
        layers only. It does not silently rasterize type or shape layers.

        Coverage is sampled once at the authored pixel resolution. Scaled or rotated previews
        may differ at subpixel edges from a live filtered mask. Keep an editable native copy
        before baking important masks. This is not a lossless replacement for live mask metadata.

        DIRECT LAYER DRAWING

        Normal layers with unit layer opacity and no effective mask avoid a per-layer offscreen
        surface. Other blends, fractional layer opacity and effective masks retain isolated
        compositing. The document remains isolated from the workspace and export background.

        Reusable renderer counters report direct and isolated layer draws. They are CPU path
        counters, not GPU timings. Software-adapter tests do not certify physical GPU performance.
        """;
}
