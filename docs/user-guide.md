# ImageSpace user guide

## Start with the sample

The initial document, **After the light**, is original procedural artwork. The sky, distant dunes and foreground are separate pixel layers. The headline, description and footer are editable type layers. The thin footer rule is a shape. Select a layer in the Layers panel, or select Move and click visible content.

Use File → New for a blank document, File → Open for a raster image/native archive/compatible PSD, or File → Place image to add an imported composite to the current document. Import never silently overwrites your original file.

## Paint

Create a pixel layer with **+** below the layer list. Choose Brush (`B`) and adjust Size, Hardness, Opacity and Flow in the top options bar. Paint with mouse, touch or a pressure-reporting pen. `[` and `]` adjust size. Pencil uses a hard round tip; Eraser (`E`) removes alpha.

Clone (`S`) needs an Alt-click source point on the active pixel layer. Its source is a snapshot taken at stroke start. Basic smudge, dodge and burn are available in the toolbox. These are not substitutes for Photoshop's healing, content-aware or advanced tonal algorithms.

The foreground swatch controls brush/shape/type color. Use the color spectrum, the hex-color dialog, `X` to swap foreground/background, or `D` to reset black/white.

## Select and mask

`M` draws a rectangular selection; Shift+M selects an ellipse; `L` draws a lasso; `W` selects a contiguous color region. Hold Shift to add, Alt to subtract, or Shift+Alt to intersect. Selection coverage is respected by painting, clearing, fills and raster filters.

Ctrl+A selects all. Ctrl+D removes the selection. Ctrl+Shift+I inverts it. Select → Feather softens the coverage. Select → Layer alpha builds a selection from the selected layer's rendered alpha.

Add a layer mask to convert the current selection into non-destructive coverage. Select the mask icon in its layer row before painting it. Black hides and white reveals. Use Layer → Disable mask to compare with the original, or delete the mask without modifying source pixels. Live adjustment/group mask semantics are not supported.

## Transform and crop

Move (`V`) selects content and displays eight resize handles plus an upper rotation handle. Shift constrains motion, proportions or rotation. The Properties panel exposes X/Y, rendered width/height, angle, and text/shape-specific settings.

Crop (`C`) draws a preview with rule-of-thirds guides. Enter applies; Escape cancels. Cropping changes canvas dimensions and layer offsets, retaining pixels outside the canvas. Image → Canvas size is centered. Image → Image size scales layers non-destructively. Undo remains available.

Space temporarily switches to the hand. The mouse wheel zooms around the pointer. Ctrl+0 fits the document; Ctrl+1 displays 100%. Rulers and the high-zoom pixel grid can be toggled in View.

## Layers and effects

Layer order is bottom-to-top in the model and top-to-bottom in the panel. Use the eye to hide, lock to protect, opacity to fade and blend mode to combine with layers below. Arrow buttons reorder; Ctrl+J duplicates. Double-click a layer name to rename it.

Type (`T`) adds an editable text layer. Double-click the text or choose Type → Edit text to change content. Rectangle and ellipse tools create editable shape layers. Rasterize these layers before using pixel tools or destructive filters.

Filter commands modify pixels and respect selections. The browser uses WebGPU for supported kernels when a compatible adapter is available, otherwise the CPU kernel. Adjustment layers affect layers below them without changing source pixels. Their Amount/Contrast fields are in Properties. Merge Down preserves ordinary normal-mode layers; interacting blend/adjustment cases require Flatten Image.

The History tab lets you undo back to an earlier state. Redo remains available until a new edit replaces the redo branch. Recovery and explicit saves do not erase history.

## Save safely

**Ctrl+S writes an editable `.imagespace` archive.** Browser saves are downloads. Native saves use a file picker; canceling does not mark the document as saved. PNG, JPEG and WebP export the visible composite, not edit history. JPEG uses a white background for transparent pixels.

PSD is an RGB/8 raster interchange subset. Unsupported Photoshop semantics are not preserved. Keep the original PSD and read the compatibility message. Export rasterizes type, shapes, transforms and masks; visible live adjustments force a flattened compatibility export.

A local recovery copy of the active tab is saved approximately every eight seconds after committed changes. It can be restored on the next launch. Recovery is not a multi-device backup and may be removed by browser storage cleanup. File → Clear recovery removes only that recovery record. Export important work regularly.

## Keyboard reference

| Shortcut | Action |
| --- | --- |
| Ctrl+N / O / S | New / Open / Save native archive |
| Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y | Undo / Redo |
| Ctrl+Shift+N / Ctrl+J | New pixel layer / Duplicate layer |
| Ctrl+A / Ctrl+D / Ctrl+Shift+I | Select all / Deselect / Inverse |
| Ctrl+C / X / V | Application pixel clipboard |
| Ctrl+I | Invert pixels or active mask |
| Ctrl+T | Show transform controls |
| Ctrl+E | Merge down where supported |
| Ctrl+0 / Ctrl+1 | Fit / 100% |
| Ctrl+R | Toggle rulers |
| Space / Tab | Temporary hand / Hide panels |
| Arrow / Shift+Arrow | Nudge by 1 / 10 pixels |
| B, E, V, M, L, W, C, S, G, T, U, I, H, Z | Common tools |
| Shift+B/M/G/O/U | Alternate pencil/ellipse selection/fill/burn/ellipse tools |
| X / D | Swap colors / Reset black and white |
| Enter / Escape | Apply crop / Cancel active gesture |
| F1 | In-app guide |

Native/browser key routing varies with operating system and browser-reserved shortcuts. Text fields keep their normal editing keys instead of triggering canvas tools.
