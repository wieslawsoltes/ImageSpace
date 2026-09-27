namespace ImageSpace.Workbench;

internal static class StudioGuide
{
    public const string UserGuide = """
        GETTING STARTED
        Open an image with File → Open, or create a document with Ctrl+N. The original sample contains separate pixel, shape and editable text layers. File → Place adds an image to the current document.

        PAINTING
        Add a pixel layer with the + button below Layers. Select Brush (B), set size, hardness, opacity and flow, then paint. [ and ] change size. E erases. Pen pressure affects radius and opacity. Alt-click with Clone (S) sets its source. Smudge, dodge and burn are basic pixel tools, not healing or content-aware reconstruction.

        SELECTIONS AND MASKS
        M draws a rectangle; Shift+M selects an ellipse; L is lasso; W selects contiguous color. Shift adds, Alt subtracts, Shift+Alt intersects. Ctrl+D deselects. Select → Feather softens coverage. A layer mask turns a selection into non-destructive transparency: black hides, white reveals. Adjustment-layer masks are not implemented.

        TRANSFORMS
        V selects and moves layers. Eight handles resize; the upper handle rotates. Shift constrains motion, proportions or rotation. The Properties panel allows precise geometry edits. C previews a crop; Enter applies and Escape cancels. Cropping preserves off-canvas pixels.

        CURVES
        Choose Image → Curves adjustment layer. It is inserted immediately above the selected layer and affects the layers below it. Select RGB, Red, Green or Blue in Properties. Click to add a point; drag to adjust input/output. The curve supports up to sixteen ordered points, with shape-preserving cubic interpolation.
        Use Input/Output fields or arrow keys for precision. Shift+Arrow moves by ten. Delete removes an interior point. Escape cancels a drag. One completed gesture makes one undo state, while the canvas previews changes before release. Presets and Reset channel affect only the selected channel. Preview toggles visibility; layer opacity controls strength.

        LEVELS
        Choose Image → Levels adjustment layer. The upper histogram handles adjust input black, midpoint gamma and input white; the lower gradient adjusts output endpoints. Numeric fields expose all five values. Each color channel is independent. Gamma above one lightens midtones. Reset channel and Reset all levels are separate commands.
        Both tonal inspectors display a sampled input histogram of the composite below the adjustment. It is not a full-resolution statistical measurement. Tone settings are immutable and undoable; the underlying pixels are unchanged. The algorithms are independent implementations, not a claim of identical Photoshop output.

        LAYERS AND TYPE
        Eye buttons control visibility; locks protect edits. Blend and opacity controls are above the layer list. Double-click a layer name to rename. T adds type; double-click text to edit. Shapes/type can be rasterized from Layer. Normal-mode layers can merge down; interacting blend/adjustment cases need Flatten to preserve the visible result.

        FILTERS AND RENDERING
        Destructive color kernels use WebGPU where available in the browser, otherwise CPU. Curves, Levels and other live adjustments use the shared Skia compositor. Filter commands respect selections. Host software rendering is still possible; a displayed draw-submission time is not a hardware-GPU timing measurement.

        FILES AND RECOVERY
        Ctrl+S writes .imagespace, preserving editable layers, transforms, pixels, masks and tonal settings. Curves/Levels documents use manifest version 2; version-1 files remain readable. Older readers reject version-2 files rather than silently dropping adjustments.
        PNG/JPEG/WebP export the composite. PSD is bounded RGB/8 raster interchange, not a lossless Photoshop editor. Retain the original PSD. Visible adjustments export to PSD as a flattened compatibility image. A recovery copy of the active document is saved locally after changes; it is not a cloud backup.

        KEYBOARD
        Ctrl+N/O/S: new/open/save. Ctrl+Z/Shift+Z: undo/redo. Ctrl+J: duplicate. Ctrl+A/D/Shift+I: select all/deselect/inverse. Ctrl+C/V: application clipboard. Ctrl+0/1: fit/100%. Space: temporary hand. X: swap colors. D: black/white. Tab: hide panels. Arrow keys move a selected layer, or the selected graph point/handle when its control has focus. Browser-reserved shortcuts vary; menus remain available.

        LIMITS
        RGB8 sRGB workflow; at most 8192 pixels per side and sixteen megapixels per surface, 128 layers and twelve open documents. Large edits can still exhaust browser memory. See Features and compatibility for the exact current boundary.
        """;

    public const string Capabilities = """
        IMPLEMENTED
        Layered sparse RGBA pixels and copy-on-write snapshots; transactional undo/redo; pressure-aware paint tools; selections/feather/alpha masks; transforms/crop; basic editable type/shapes; masks, visibility, locks, sixteen blend modes and opacity; fourteen CPU filters; eight live adjustments including Curves and Levels; eight optional WebGPU color kernels; native archives, bounded raster PSD and raster image export; local recovery; shared Uno desktop/WebAssembly hosts.

        TONAL EDITING
        Curves: composite RGB plus red/green/blue, 2–16 points, shape-preserving cubic interpolation, presets, numeric/keyboard edits and live transactional dragging. Levels: independent channel endpoints/gamma, histogram handles and presets. Tone filters preserve alpha and source pixels. The inspector histogram is sampled. Independent algorithms do not imply Photoshop-identical output.

        NOT PHOTOSHOP PARITY
        No Camera Raw, CMYK/Lab/spot channels, 16/32-bit HDR, ICC proofing, smart objects, PSD adjustment metadata, pen/path editor, healing/content-aware reconstruction, generative AI, liquify, puppet/perspective warps, layer styles, advanced typography/font discovery, groups/clipping semantics, actions/macros, plug-ins, video/timeline, cloud services, PSB or lossless PSD roundtrips. Adjustment masks and automatic tonal correction are not implemented. The workspace is not pixel-identical Photoshop.

        FILES
        Native manifest version 2 retains Curves/Levels; legacy version 1 remains readable. PSD import supports v1 RGB/8 raw/PackBits raster layers, offsets, visibility, opacity and supported blend keys. Unsupported masks/groups/type/profiles/smart objects/adjustment metadata are not retained. Export rasterizes type/shapes/transforms and supported masks; visible adjustment documents export a flattened compatibility image. Keep originals and use .imagespace for editable roundtrips.

        GPU
        Uno's Skia renderer selects the host's graphics backend. Curves/Levels use cached Skia lookup filters. Browser WebGPU is an optional destructive-filter accelerator with readback to the common tile model. This is not a GPU-only engine; software rendering is a supported fallback. CI is not physical-GPU performance validation.
        """;
}
