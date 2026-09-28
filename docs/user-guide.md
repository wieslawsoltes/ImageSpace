# ImageSpace user guide

## Start with an editable document

The **After the light** sample contains original procedural sky, dune and foreground pixel layers, separate headline/description/footer type, and a thin shape rule. Select a layer in Layers or use Move to click visible content.

File → New creates a blank document. File → Open reads a raster image, native archive or compatible PSD. File → Place image inserts an imported composite into the current document. Keep original imports; unsupported PSD metadata is not preserved.

## Paint, select and mask

Add a pixel layer with **+**, choose Brush (`B`), and set size, hardness, opacity and flow. `[`/`]` change size; `E` erases. A pressure-reporting pen affects radius and opacity. Pencil is a hard round tip. Clone (`S`) requires an Alt-click source on the active layer. Basic smudge, dodge and burn are available but are not healing/content-aware tools.

The foreground controls paint/type/shape color. Use the spectrum or hex dialog, `X` to swap swatches, and `D` for black/white.

`M` selects a rectangle; Shift+M an ellipse; `L` a lasso; `W` a contiguous color region. Shift adds, Alt subtracts, Shift+Alt intersects. Ctrl+A selects all, Ctrl+D deselects, Ctrl+Shift+I inverts. Select → Feather softens coverage. A deliberately empty selection edits nothing.

Add a layer mask to convert the selection into coverage, including on Curves, Levels and other adjustment layers. With no selection, a new mask reveals all authored content, including off-canvas pixels. Existing masks are never silently replaced. Select the mask thumbnail to edit it: black hides, white reveals; Eraser reduces coverage independently of the foreground color. Brush, pencil, clone, basic dodge/burn/smudge, fill and linear gradient operate on mask coverage. Destructive filters also process coverage rather than mask RGB.

The mask Properties inspector exposes Density (0–100%) and Feather (0–32 mask-local pixels) without rewriting the original mask. Density zero bypasses authored hiding. Disable retains the mask for comparison; Delete removes it but remains undoable. The Properties **Invert mask** action operates on the whole authored mask; **Ctrl+I** follows the active pixel selection. Locks protect both source content and mask changes.

**Composite**, **Mask** and **Overlay** are inspection modes. Grayscale shows effective coverage after density/feather; the red overlay highlights hidden regions. These views do not add undo entries and are never baked into native saves or image exports. Back to layer content restores the ordinary inspector. Masks start linked to their layer. Use the chain icon to unlink a mask, select its thumbnail and use Move, the eight resize handles, rotation or the numeric placement fields to edit it independently. Relinking preserves the current appearance; Align mask to layer deliberately resets placement. Group masks remain unsupported. See [independent mask placement](mask-placement.md).

## Transform and navigate

Move (`V`) displays eight resize handles and an upper rotation handle. Shift constrains motion/proportions/rotation. Properties exposes X/Y/W/H/Angle and content-specific settings. Arrow keys nudge by one pixel; Shift+Arrow by ten, except when editing a graph or text field.

Crop (`C`) previews a rectangle with thirds guides. Enter applies; Escape cancels. Cropping preserves off-canvas pixels. Image → Canvas size is centered; Image → Image size scales layers non-destructively.

Space temporarily pans. The wheel zooms around the pointer. Ctrl+0 fits, Ctrl+1 selects 100%. View controls rulers, pixel grid and panel visibility.

## Curves and Levels

Choose **Image → Curves adjustment layer** or **Image → Levels adjustment layer**. The adjustment is placed immediately above the selected layer and changes the composite below it, without rewriting source pixels or affecting layers above.

Curves exposes RGB, Red, Green and Blue, a sampled input histogram and a draggable curve. Click to add a point; drag to adjust it. Input/Output fields and arrows provide precise edits. Shift+Arrow moves by ten. Delete removes an interior point. Presets and Reset channel affect only the chosen channel. Up to sixteen points are supported, including endpoints.

Levels exposes input black/white, gamma and output endpoints through numeric fields and histogram/gradient handles. Moving the midpoint left lightens midtones. RGB and individual color channels are independent. Up/Down while the graph has focus chooses a handle; Left/Right nudges it.

Both controls preview changes while dragging and record one undo state on release. Escape restores the pre-drag settings. Preview toggles adjustment visibility; layer opacity controls strength. Channel selection and control focus are preserved across normal commits and undo.

These are independent algorithms, not a claim of identical Photoshop output. Inspector histograms are sampled, not full-resolution statistics. [Read the complete tonal-editing guide and reusable APIs](tonal-adjustments.md).

## Layers, type and filters

The layer list displays topmost content first. Use eyes, locks, opacity and blend mode for visibility/protection/compositing. Arrow buttons reorder; Ctrl+J duplicates. Double-click a layer name to rename. `T` adds editable text; double-click text to edit. Rectangle/ellipse tools create editable shapes.

Rasterize type/shapes before applying pixel tools. Destructive filters respect selections and use WebGPU for supported browser kernels when available, otherwise CPU. Live adjustments remain editable. Merge Down supports ordinary normal-mode layers; interacting blend/adjustment cases require Flatten to preserve the visible result.

History lets you return to previous edits. Redo remains available until a new edit replaces that branch. Copy merged samples the visible composite; Cut samples the active pixel layer. The clipboard is application-local, not complete OS/browser rich-image interoperability.

## Save and recover

**Ctrl+S writes `.imagespace`**, the editable roundtrip format. Tone-only files use manifest version 2. Adjustment masks, non-default density/feather and fractional adjustment crossfades require version 3. Independent mask placement/link metadata requires version 4; the reader accepts versions 1–4. Older readers reject unsupported versions rather than silently dropping the new features. History and transient selections are not stored in archives.

Browser saves are downloads. Native saves use file pickers; cancellation does not mark the document as saved. PNG/JPEG/WebP export the visible composite. JPEG places transparency against white.

PSD import/export is a bounded RGB/8 raster subset. Preserve originals and read the warning. Type/shapes/transforms and masks are rasterized on export; visible live adjustments require a flattened compatibility export. Native Save never silently replaces the original PSD.

A recovery copy of the active tab is saved locally after committed changes, approximately every eight seconds. Browser storage cleanup can delete it; it is not a cloud backup. File → Clear recovery removes that record only. Export important work regularly.

## Keyboard reference

| Shortcut | Action |
| --- | --- |
| Ctrl+N / O / S | New / Open / Save native archive |
| Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y | Undo / Redo |
| Ctrl+Shift+N / Ctrl+J | New pixel layer / Duplicate |
| Ctrl+A / D / Shift+I | Select all / Deselect / Inverse |
| Ctrl+C / X / V | Application pixel clipboard |
| Ctrl+I | Invert pixels or active mask |
| Ctrl+M / Ctrl+L | Curves / Levels adjustment where the host allows these keys |
| Ctrl+T / Ctrl+E | Show transform / Merge down where supported |
| Ctrl+0 / 1 / R | Fit / 100% / Rulers |
| Space / Tab | Temporary hand / Hide panels |
| Arrow / Shift+Arrow | Nudge layer or focused curve point by 1 / 10 |
| X / D | Swap colors / Reset black and white |
| Enter / Escape | Apply crop / Cancel current gesture |
| F1 | In-app guide |

Browsers reserve some shortcuts, especially Ctrl+L. The corresponding menu commands remain available. Text fields retain normal editing keys instead of triggering canvas tools. See [features](features.md) for limits and unsupported capabilities.

## Modify a selection

Select → Expand selection, Contract selection, Border selection and Smooth selection open a radius control (0–256 pixels). These operations preserve soft alpha instead of thresholding it. The neighborhood is square, with a diameter of `2 × radius + 1`; pixels outside the canvas are unselected. Smooth performs an opening followed by a closing. Each operation is one undo entry; these are explicit ImageSpace kernels, not a claim of identical Photoshop edge shaping.

## Extended PSD interchange

PSD v1 RGB/8 supports raw, PackBits, ZIP and ZIP prediction. Unicode layer names and resolution are retained. Simple user-mask data is placed into the pixel layer’s coordinates, honoring disabled/inverted/default coverage and supported density/feather parameters. Layer masks remain editable after import. Embedded profiles, vector/group/clipping semantics and advanced metadata are warned about rather than silently claimed to survive. Export uses ZIP raster channels by default; application shapes/type/transforms and masks are still rasterized by the export callback.

## Move a mask independently

Select its mask thumbnail and click the chain icon to unlink it. Use Move, the eight resize handles, the rotation handle, or Mask X/Y/width/height/angle in Properties. Arrow keys nudge by one pixel; Shift+Arrow uses ten. Relink to move the layer/mask pair together without changing the current placement. Align mask to layer is a separate explicit reset. Save as `.imagespace` to retain editable affine placement; PSD export bakes it into pixels. See [mask placement](mask-placement.md).
