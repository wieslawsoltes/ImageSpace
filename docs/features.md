# Feature and compatibility matrix

This file is the release boundary, not a checklist that labels placeholders as complete.

| Feature | Status | Important qualification |
| --- | --- | --- |
| Genuine Uno browser application | Implemented | WebAssembly; not a separate HTML mockup |
| Windows/Linux/macOS host projects | Implemented | Build artifacts are unsigned developer builds |
| Photoshop-style shell | Implemented | Familiar layout, not pixel-identical Photoshop |
| Custom icons, menus and key editor controls | Implemented | Uses Uno layout, text-input and focus primitives |
| Multi-document tabs | Implemented | Maximum twelve; recovery covers the active tab |
| Sparse RGBA tiles and copy-on-write snapshots | Implemented | 128 px tiles; 8-bit straight alpha |
| Transactional undo/redo and save markers | Implemented | 64-entry/192 MiB target history; not persisted |
| Brush/pencil/eraser | Implemented | Round tips; no imported ABR or textured/dynamic tip engine |
| Pen pressure | Implemented | Pressure-to-radius/opacity; no tilt or barrel-rotation engine |
| Clone stamp | Implemented | Alt-click source, active raster layer, snapshot source |
| Dodge/burn/smudge | Basic | Simple kernels, not Photoshop's advanced tonal/healing models |
| Fill and linear gradient | Implemented | Foreground/background, active pixel layer |
| Rectangle/ellipse/lasso/wand | Implemented | Add/subtract/intersect; wand uses contiguous color |
| Feather/invert/alpha selections | Implemented | No select-subject, hair refinement or edge-decontamination |
| Layer masks | Implemented | Pixel, text and shape masks; not full adjustment/group-mask semantics |
| Layer opacity/visibility/locks/reorder | Implemented | Individual layers; no layer groups or clipping chains |
| Blend modes | Implemented | Sixteen Skia-supported modes; no Blend If controls |
| Text | Basic editable | Inter, multiline, size, weight and color; no complete font/paragraph shaping UI |
| Shape layers | Basic editable | Rectangles, rounded rectangles, ellipses, fill and stroke |
| Move/resize/rotation | Implemented | Eight handles, angle handle, constrained operations, numeric input |
| Canvas crop and resize | Implemented | Non-destructive offsets preserve off-canvas pixels |
| Image sizing/rotation | Implemented | Non-destructive layer scaling; no Photoshop resampling presets |
| CPU filters | Implemented | Invert, grayscale, sepia, brightness/contrast, saturation, gamma, threshold, posterize, blur, sharpen, emboss, edges, pixelate, noise |
| WebGPU color filters | Implemented | Eight kernels; adapter-dependent, CPU fallback; readback required |
| Live adjustment layers | Implemented subset | Brightness/contrast, saturation, invert, grayscale, sepia, blur |
| Native `.imagespace` archive | Implemented | Versioned UTF-8 manifest, RGBA layers/masks, metadata validation |
| PSD | Raster subset | v1, RGB/8, raw/PackBits, layer positions/opacity/visibility/blend |
| PSB / high-bit PSD / CMYK PSD | Rejected | Not silently interpreted as an editable equivalent |
| PNG/JPEG/WebP | Implemented | Raster import/export; JPEG exports against white |
| BMP/GIF import | Codec-dependent | Static raster decode; no animated document/timeline |
| Clipboard | Application-local | Pixel clipboard, not complete OS/browser rich-image interoperability |
| Recovery | Implemented | IndexedDB or native file; not a cloud backup |
| Histogram/channels panel | Implemented subset | RGB component histograms and composite-alpha selection, not editable spot channels |
| Physical GPU performance validation | Not yet established | CI is not a substitute for a device matrix |

## Explicitly not implemented

Camera Raw, CMYK/Lab/spot-channel editing, 16/32-bit HDR documents, ICC soft-proofing, smart objects, Photoshop-compatible adjustment metadata, pen/path editing, healing/content-aware reconstruction, generative AI, liquify, puppet/perspective warp, layer styles, advanced typography/font discovery, linked assets, layer groups/clipping groups, actions/macros, plug-ins, video/timeline, Photoshop cloud services and lossless PSD roundtrips.

## PSD preservation policy

Always retain the original PSD. Raw raster layers can differ from the Photoshop composite when the source relies on unsupported masks, effects, groups, smart objects or adjustment metadata. Import warns about this boundary. The application never overwrites the original PSD through its native Save command.

PSD export writes standard RGB/8 raster layers, with type/shapes/transforms and supported masks rasterized. A document with live adjustment layers exports a flattened compatibility image to preserve its visible appearance. Native `.imagespace` files retain the application-specific editable state.

## Validation interpretation

Engine tests validate model, pixel, archive, PSD and compositor invariants. Regression tests cover live cache invalidation and mask/undo behavior. Browser tests use actual pointer/keyboard input and file pickers/downloads, and inspect the rendered screenshot rather than accepting an empty canvas. GPU diagnostics explicitly distinguish an available adapter from an unavailable adapter; a software adapter is not claimed to be hardware acceleration evidence.
