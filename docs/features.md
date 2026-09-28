# Feature and compatibility matrix

This is the implemented boundary, not a list of placeholders presented as complete Photoshop parity.

| Area | Status and boundary |
| --- | --- |
| Genuine Uno WebAssembly application | Implemented; shares the editor with desktop hosts, not a separate HTML mockup |
| Windows/Linux/macOS | Host projects and self-contained CI artifacts; unsigned developer builds |
| Photoshop-style workspace | Custom dark chrome, toolbox, tabs, menus, properties, layers and history; not pixel-identical Photoshop |
| Reusable controls | Original icons, buttons, menus, numeric fields, spectrum, histogram, Curves and Levels; Uno primitives handle layout, focus and text input |
| Documents | Up to twelve tabs; local recovery covers the active tab |
| Pixel model | Sparse 128-pixel RGBA8 tiles with copy-on-write snapshots |
| Undo/redo | Transactional; 64-entry/192 MiB target history, not a hard process-memory limit; history is not saved in archives |
| Brush/pencil/eraser | Pressure-aware round tips; no ABR imports or textured/dynamic-tip engine |
| Clone | Alt-click source, active raster layer, stroke-start source snapshot |
| Dodge/burn/smudge | Basic kernels; not healing/content-aware or advanced Photoshop tonal tools |
| Fill/gradient | Foreground/background, active pixel layer, linear gradient |
| Selection | Rectangle, ellipse, lasso, contiguous color; add/subtract/intersect, invert, feather, alpha selection, expand/contract/border/smooth (square neighborhoods; preserves soft coverage) |
| Masks | Pixel/text/shape/adjustment coverage masks, density, feather, selection-aware pixel tools and view-only grayscale/overlay inspection; groups and unlinked mask transforms remain unsupported |
| Layers | Visibility, locks, opacity, duplicate/reorder, rasterize, normal-mode merge and sixteen blend modes; no groups or clipping chains |
| Type | Editable multiline Inter text, size, weight and color; no complete typography/font-discovery UI |
| Shapes | Rectangle, rounded rectangle and ellipse, fill and stroke; no pen/path editor |
| Transforms | Move, eight resize handles, rotation handle, numeric inputs and constrained gestures |
| Canvas/image sizing | Non-destructive crop/offsets/scaling; no Photoshop resampling preset suite |
| CPU filters | Fourteen color/convolution kernels plus reusable Curves/Levels tone-lookup processing |
| WebGPU filters | Eight destructive color kernels, adapter-dependent, explicit CPU fallback and readback |
| Live adjustments | Brightness/contrast, saturation, invert, grayscale, sepia, blur, **Curves and Levels** |
| Curves | Four channels, up to sixteen points per channel, shape-preserving cubic interpolation, numeric/keyboard edits, presets and live transactional dragging |
| Levels | Four channels, input/output endpoints, gamma, draggable histogram handles, numeric edits and presets |
| Native archive | Version 1–3 reader; version 2 preserves Curves/Levels and version 3 protects new mask/crossfade semantics; UTF-8 manifest, pixel/mask data and input limits |
| PSD | Bounded PSD v1 RGB/8 raw/PackBits/ZIP/ZIP-prediction channels; Unicode names, DPI, raster offsets/visibility/opacity/blend keys and placed user masks; not lossless Photoshop roundtripping |
| PSB/high-bit/CMYK PSD | Rejected rather than silently interpreted as equivalent |
| PNG/JPEG/WebP | Raster import/export; JPEG is composited against white |
| BMP/GIF | Codec-dependent static raster import; no animation document/timeline |
| Clipboard | Application-local pixel clipboard; Cut samples the active raster layer, Copy merged samples the composite |
| Recovery | IndexedDB or atomic native recovery-file replacement; not a backup service |
| Histograms | Full composite RGB component histograms; bounded sampled input histogram in tone inspectors; no editable spot channels |
| Hardware performance | Not established by CI; software-adapter correctness is not a physical-device benchmark |

## Explicitly outside the current implementation

Camera Raw, CMYK/Lab/spot channels, 16/32-bit HDR, ICC soft proofing, smart objects, Photoshop-compatible adjustment metadata, pen/path editing, healing/content-aware reconstruction, generative AI, liquify, puppet/perspective warp, layer styles, advanced typography/font discovery, linked assets, group/clipping semantics, actions/macros, plug-ins, video/timeline, Photoshop cloud services, PSB and lossless PSD roundtrips.

Curves and Levels are independent algorithms and UI components, not a claim of byte-identical Photoshop output. Adjustment masks are supported; black/white eyedroppers and automatic color correction are not included. See [tonal adjustments](tonal-adjustments.md) for exact interpolation, channel order and interaction behavior.

## File-preservation policy

Always retain original imports. A source PSD using unsupported vector/combined mask semantics, effects, groups, smart objects or adjustments can differ from its Photoshop composite after raster-layer import. Import warns about that boundary. Native Save does not overwrite the original PSD.

PSD export rasterizes type, shapes, transforms and supported masks. Visible adjustment layers require a flattened compatibility export. Use `.imagespace` to preserve editable application-specific settings. Tone-only documents use manifest version 2. Adjustment masks, non-default mask density/feather and fractional adjustment output crossfades require version 3, preventing older readers from silently discarding those semantics.

Working limits are 8192 pixels per side, 16 megapixels per surface, 128 layers and twelve documents. Input/archive limits reduce risk but do not guarantee every combination fits a browser's memory budget.

## Validation

Engine and regression suites exercise model, parser and actual Skia pixel output. Browser tests operate real Uno controls through pointer/keyboard/file events and inspect screenshots. The Pages workflow verifies the deployed source SHA and repeats browser acceptance against the public URL. These checks are evidence for the tested behavior, not proof of complete Photoshop parity or production readiness.

## CPU and allocation improvements

Tile-native fills/imports, contiguous RGBA rows, sparse histogram scans, pooled row-based bilinear resizing, crop and flip, direct native Skia pixel spans, and streaming archive pixel entries. Correctness is checked against scalar implementations, independent PSD fixtures, archive/mask regressions and actual browser interactions. Timing results are CPU-only, workload-specific and retained by CI; no claim of physical GPU speed follows from them.
