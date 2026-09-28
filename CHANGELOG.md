# Changelog

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
