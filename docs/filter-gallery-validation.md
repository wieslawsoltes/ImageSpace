# Filter Gallery: comparison rendering and acceptance

## Transparent comparisons

A split view must not draw the original image over already-composited filtered pixels. With partial alpha that blends two versions of the image, making the original side inaccurate. `ImageComparisonRenderer.Draw` in `ImageSpace.Skia` uses disjoint, non-antialiased clips. The original and result each composite once against the caller's existing background. The helper borrows its images/canvas, preserves the save stack, transform and clip, and performs no raster readback or per-frame image allocation. Filter Gallery retains its preview images and draws the divider separately.

The preview keeps its last valid image if constructing a replacement fails. Disposal is idempotent and zero-size layout passes are ignored. This component works on a raster or GPU-backed Skia canvas; the regression suite's raster execution does not certify physical GPU performance.

`tests/ImageSpace.PreviewTests` checks 27 combinations of source/result alpha and comparison position, caller transforms/clipping, canvas stack balance, malformed ratios and empty bounds. The additional browser test imports an independently generated RGBA PNG through the real file chooser, moves the comparison slider with actual input, and checks screenshot colors against single-composite alpha arithmetic. Cancel must retain the document revision and release resident sessions.

## Fresh control geometry

The browser diagnostics array is sampled independently of pointer input. A visibility check followed by a second lookup can use different publications; the original array may even describe a popup that has already closed. The new `waitForControl` helper captures and returns the same observed control only after two fresh publications agree on its geometry. A missing/disabled/clipped target resets stability. It generates no clicks or editor commands, and never retries an edit to make a test pass.

Seven deterministic Node contracts exercise disappearing popups, stale publications, movement, invalid geometry, current numeric text and bounded failure. The Curves/Levels scenarios retain their original pixel, gesture, cancellation, keyboard, history and file-roundtrip assertions. There are no test retries or skipped failure cases.

## Reports

Build emits `validation-summary.json` with the exact checkout SHA, available headless totals, browser/GPU result summaries, actual failure details and scoped GPU measurements. Missing reports are not converted to successful checks. Source, packages, screenshots and original JSON reports remain separate artifacts. Release preserves the existing Trusted Publishing and six-runtime single-file packaging workflow while adding filter-stack, comparison and GPU regression gates.

The public Filter Gallery workflow reference is Adobe's documentation:
https://helpx.adobe.com/photoshop/desktop/effects-filters/get-started-with-filters/apply-filters-from-the-filter-gallery.html

ImageSpace's gallery remains an independently implemented raster stack, not the complete Photoshop filter catalogue, persisted Smart Filters or pixel-identical Photoshop output. See [Filter Gallery contracts and boundaries](filter-gallery.md).
