namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private const string FilterGalleryHelp = """
        FILTER GALLERY

        Select unlocked raster content, then choose Filter → Filter Gallery.
        Add effects from the catalogue. The stack runs from top to bottom. Select an
        effect to edit its parameters; the checkbox bypasses it without deleting it.
        Arrow buttons reorder the selected effect. The comparison slider reveals
        the original on the left. Apply filters records the complete stack as one
        undoable edit. Cancel never changes the document. Repeat filter stack applies
        the last accepted settings to the current raster target.

        The preview is a sampled layer-local view, at most 256 pixels on its long
        edge. Blur sigma and pixelate block size are scaled for this preview. The
        final result uses original dimensions and parameters, and is then restricted
        by the current selection. Previewing does not create history entries, change
        recovery state, or rebuild the main editor's retained panels. This is not a
        Smart Filter or an editable effect stack persisted in the document.

        GPU PROCESSING

        Browser sessions upload their source once. Ping-pong buffers keep intermediate
        results on the GPU; repeated previews reuse buffers, bind groups and pipelines.
        Only the final output is read back. Source pixels remain immutable so each
        preview restarts from the original instead of accumulating previous effects.

        Thirteen kernels run through WebGPU, including two-pass Gaussian blur,
        sharpen, emboss, edges and block-reduced pixelation. Seeded Noise preserves
        the existing .NET sequence through CPU fallback. Desktop Filter Gallery uses
        CPU kernels while the main document compositor remains Skia-based. Adapter
        limits, device loss and a 256 MiB resident allocation budget can also select
        fallback. The .NET/browser boundary still transfers initial/final RGBA data.

        Up to sixteen operations are accepted. CPU Gaussian fallback uses a pooled
        rolling row cache instead of a full-image float intermediate. Browser gallery
        blur work yields between row batches; cancellation is checked within rows
        and after timer yields. Other CPU kernels still cancel between stages.
        Preview updates are coalesced, stale results are discarded, and closing
        releases preview resources. A row can exceed the scheduling time budget;
        neither a hard frame-latency bound nor GPU-only desktop filtering is implied.
        No physical-GPU performance or pixel-identical Photoshop output is implied.
        """;
}
