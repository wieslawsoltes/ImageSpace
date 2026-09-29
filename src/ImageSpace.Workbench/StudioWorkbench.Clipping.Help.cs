namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private const string ClippingHelp = """
        CLIPPING MASKS

        Select a layer above raster, type or shape content. Choose Layer → Create
        Clipping Mask, press Ctrl+Alt+G, or Alt/Option-click its lower layer-row boundary.
        Consecutive clipped layers use the nearest non-clipped content layer as their
        base. Clipped thumbnails are indented with a bent-arrow icon; the base name
        is underlined. The source pixels remain editable and are never destructively cut.

        The base's rendered alpha, including its mask and geometry, defines group coverage.
        Every clipped layer uses its own opacity, blend and optional mask inside this
        coverage. The base's opacity and blend apply once to the resulting group. Hiding
        the base hides the whole chain. Clipped adjustment layers affect their prefix
        inside the chain without expanding the base alpha or affecting unrelated layers.

        Release Clipping Mask releases the selected layer and all clipped layers above
        it. Unlock that tail first. Deleting a base releases its followers instead of
        attaching them to another layer. Reorder arrows move complete clipping units;
        duplicating a base duplicates its chain, while duplicating a clipped member
        adds another member. These actions are undoable. Merge Down rejects a partial
        chain; Flatten Image remains the explicit visible-composite operation.

        SAVE AND INTERCHANGE

        Save .imagespace for exact editable clipping relationships (manifest version 5).
        Older readers must reject version 5 rather than silently displaying unclipped
        content. Versions 1–4 remain readable. RGB/8 PSD import and raster export retain
        the standard clipping flags. Layered PSD export cannot retain live adjustment
        semantics; the application uses its existing flattened compatibility export.

        This implementation uses grouped clipping blending. Photoshop's alternative
        Blend Clipped Layers As Group disabled setting, group folders and advanced
        knockout/layer-style interactions are not implemented. A PSD warning identifies
        the non-default setting. Pixel-edge rounding can differ between rendering backends.

        RENDERING

        Cached Skia/SkSL blenders preserve group alpha without CPU pixel readbacks.
        Skia executes on the host backend, which may be GPU or software. Warm clipping
        does not rebuild blend shaders or upload unchanged source tiles. The independent
        browser WebGPU filter engine also fuses adjacent color operations while retaining
        each operation's RGBA8 quantization. Counters distinguish logical filters from
        physical dispatches. Software-adapter measurements are not hardware GPU timings.
        """;
}
