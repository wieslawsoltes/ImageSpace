# Changelog

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
