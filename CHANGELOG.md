# Changelog

## 0.5.0-alpha.1 — 2026-09-29

- Added editable contiguous clipping-mask chains, base-alpha-preserving Skia/SkSL compositing for all sixteen supported blend modes, clipped adjustment scopes and one-time base opacity/blending.
- Added create/release/shortcut/boundary actions, retained layer indicators, chain-safe reorder/duplicate/delete behavior, clipping-aware geometric hit testing, and an original editable clipping study.
- Added native manifest v5 with strict legacy-version rejection, standard RGB/8 PSD clipping flags and explicit warnings for unsupported non-default grouped blending.
- Fused adjacent WebGPU color stages while retaining stage-by-stage RGBA8 quantization, reused the host parameter arena, and separated logical filter counters from physical dispatches. Retained an unfused reference mode.
- Added clipping model/renderer/format tests, real Uno browser interactions and fusion differential/work-counter benchmarks. Reports describe their actual backend and measurement scope.
- Folder groups, non-default advanced clipping interactions, smart objects, high-bit/ICC workflows and other documented Photoshop gaps remain separate work; no full UI/feature parity or hardware performance certification is asserted.

## 0.4.0-alpha.1 — 2026-09-29

- Added a real Uno Filter Gallery with ordered enable/reorder/remove controls, parameter editing, original/result comparison, sampled/coalesced previews, Apply/Cancel and Repeat filter stack.
- Added immutable filter recipes and reusable CPU/resident session contracts with atomic selection-restricted commits, stale-result rejection, cancellation and independent copy-on-write ownership.
- Expanded WebGPU support from eight to thirteen kernels with premultiplied separable blur, sharpen, emboss, edges and block-reduced Pixelate. Seeded Noise remains on CPU.
- Resident sessions upload source once, cache buffers/bind groups, keep intermediate stages on GPU and expose explicit execution/readback/disposal and memory-budget diagnostics.
- Fixed spatial-filter hidden RGB output so it no longer depends on sparse-tile allocation order. Exact bridge decoding retains these intermediates; transparent multi-stage CPU/GPU regressions cover the behavior.
- Added C# transaction/ownership fixtures, independent WGSL and actual-C# parity checks, concurrent-session/device-loss tests, and real Uno gallery/pixel/undo/file/fallback acceptance.
- Preserved existing retained UI optimizations and NuGet Trusted Publishing/release configuration. This is not full Photoshop parity, persisted Smart Filters, a GPU-only engine, or physical-GPU certification.

## 0.3.2-alpha.1 — 2026-09-28

- Retained schema-specific inspectors, identity-keyed layer rows, tool options and document tabs instead of rebuilding the workbench on selection.
- Cached button templates per UI thread, suppressed unchanged numeric/graph/layout writes, and retained focus and current model binding through undo.
- Separated active-target and saved-state notifications from content changes; plain Move-tool clicks create neither history snapshots nor recovery work.
- Deferred/coalesced sampled histogram work with independent rendering caches and content-dependent prefix validation. Hidden History/Channels content is built on demand.
- Split scene and interaction overlay drawings; hover, handles and marching ants no longer explicitly invalidate the image compositor.
- Removed thumbnail tile snapshots; added deterministic preview/selection ownership cases and real-pointer UI responsiveness regressions with structural counters.
- Validation artifacts establish exact-head results; CPU refresh timing is not event-to-photon or physical-GPU timing.

## 0.3.1-alpha.1 — 2026-09-28

- Added **Layer → Apply layer mask** for enabled raster masks, including density, feather and linked/unlinked affine placement, sampled at authored pixel resolution.
- Preserved source RGB, full authored extent, off-canvas content, layer geometry, opacity, blend mode and selection; one undoable transaction restores the editable mask and original pixels.
- Added renderer-independent mask-application contracts with detached callback input, independent output ownership, and pre-transaction validation of stale, failed, null and wrong-size results.
- Avoided per-layer offscreen surfaces for Normal/unit-opacity layers without effective masks while retaining document isolation and the original paths for mask/blend/group-opacity semantics.
- Removed unused raster shape paints and allocated draw stopwatches; added direct/isolated counters and an always-isolated differential reference switch.
- Added a dedicated layer regression suite, same-process raster CPU benchmark, two real-browser mask-application scenarios, in-app guidance and a technical guide. CI results, not this changelog, establish validation status for a given commit.
- Affine/feathered masks are sampled once when baked; scaled or rotated previews can differ at subpixel edges from live filtering. Adjustment masks and non-raster layers are not silently flattened.

## 0.3.0-alpha.1 — 2026-09-28

- Linked/unlinked affine layer-mask placement, pointer and numeric transformations, relink-without-jump and transactional keyboard nudging.
- Mask-local painting, fills, gradients and selection filtering use prepared coordinate mappings; transformed brush cursors match the actual tip frame.
- Native archive v4 preserves independent mask metadata while retaining reads of versions 1–3.
- Two-stage mask source/placement filter caching avoids rerecording authored mask content during movement/density changes.
- Exact streaming selection contours use O(width) pooled scratch and merged collinear edges instead of an image-sized temporary.
- Expanded geometry, archive, compositor, topology and real-browser regression coverage; interaction CPU benchmarks.

## 0.2.0-alpha.1 — 2026-09-28

- Merged all pending mask/compositor and dependency work; aligned Skia 3.119.4 and current CI Actions.
- Fixed small-mask clipping with an isolated destination-in pass.
- Added bounded RGB/8 PSD ZIP/prediction decoding, compressed raster export, Unicode names, DPI, and placed user masks.
- Added Expand, Contract, Border and Smooth selection operations with soft-coverage preservation and transactional Undo.
- Optimized tile fills/imports, row operations, resizing, crop, flip, histogram, native Skia transfers and streamed archives.
- Added independent PSD/geometry/storage regressions, browser compatibility tests and reproducible CPU A/B benchmarks.
- Full Photoshop parity remains explicitly out of scope for this release; see the feature matrix.

## 0.1.0-alpha.1 — development, 2026-09-27

### Tonal editing and deployment follow-up

- Added editable Curves and Levels adjustment layers with composite RGB and independent red/green/blue controls.
- Added immutable tone settings, bounded shape-preserving cubic interpolation, validated channel lookup tables and sparse CPU tone processing.
- Added renderer-owned cached Skia tone filters with explicit identity alpha and settings/opacity invalidation.
- Added reusable Uno curve and histogram/levels controls with live pointer previews, numeric/keyboard editing, presets and cancellation. A completed drag is one undo entry.
- Added adaptive tonal inspectors that preserve channel selection and focus across undo/redo, and bounded sampled input histograms.
- Added native archive version 2 for tonal documents while retaining version-1 read compatibility; older readers reject unsupported adjustment semantics.
- Corrected Cut to use the selected raster layer, handled empty-selection copying explicitly, and prevented graph key events from also nudging layers.
- Fixed Uno's generated browser manifest by using literal asset properties, and syntax-check published JavaScript before deployment.
- Added tonal math, snapshot, parser, Skia-output and browser interaction regression coverage.

### Initial editor

- Eleven reusable libraries for the tiled model, brushes/selections, filters, editing, formats, Skia, WebGPU, storage, controls, viewport and workbench.
- Original layered artwork and a Photoshop-style dark workspace with actual editing commands.
- Masks, selections, transforms/crop, editable type/shapes, blend/opacity, history, raster interchange and local recovery.
- Windows/Linux/macOS build matrix, real-browser acceptance, package artifacts and commit-verified GitHub Pages publishing.

This is not complete Photoshop parity. Exact implemented behavior and unsupported features are documented in `docs/features.md`.
