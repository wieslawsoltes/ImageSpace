# Selection responsiveness: validation and follow-up fixes

The retained-UI work in [ui-responsiveness.md](ui-responsiveness.md) keeps selection separate from content edits. This follow-up removes the remaining avoidable updates found in browser traces and extends the interaction coverage.

## Fixed-cost selection work

Layer-row selection uses cached row-owned brushes and an independent inner highlight border. The thumbnail's border thickness, layout dimensions and source do not change on selection. The previous one/two-pixel border switch caused both the old and newly selected thumbnail canvases to remeasure and repaint. Warm selection tests now require unchanged thumbnail-render counters as well as unchanged scene draws, row constructions, inspector constructions, options, tabs and history widgets.

Blend-mode presentation no longer alternates its automation/tooltip name between the decorated text and semantic mode for every selection. Tool options are retained and keyed by their actual inputs: selected tool, content/mask channel, transform-control flag, foreground color, brush settings and tolerance. An unrelated layer selection does not run all option setters. Dimensions/zoom status updates are skipped for target-only changes and assign text only when its value changes.

Foreground and background swatches have independent displayed-value caches. Both are initialized once and a background-only edit updates its swatch even when foreground has not changed. A screenshot-based browser regression covers both the initial colors and this independent update.

## Trustworthy interaction checks

Diagnostics are read-only and periodically sampled. Numeric affine tests wait for the requested model value of each field before calculating the next handle position. An already-correct angle is not evidence that a later Y-position edit has reached the diagnostics snapshot. The old test clicked thirty document pixels away from the current handle; its trace confirmed a normal move, not a broken resize. Assertions still verify both scale axes, fixed X/Y anchor, one history state and unchanged authored pixels.

Tall Curves/Levels inspectors legitimately reduce the layer list's viewport. The list has the automation name `Layer list`. Tests reach off-screen layers through real wheel input instead of assuming every row is visible. This exercises the actual scrolling and retained-row behavior; it does not expose a JavaScript document-mutation endpoint.

### Sample freshness during scrolling

The histogram regression exposed an additional sequencing error: the test sent a large wheel movement and immediately consumed a pre-scroll control snapshot. The subsequent pointer click selected Footer rule rather than Description. A control being visible in a sampled array does not establish that its coordinates remain valid during animation.

`tests/browser/layer-list.mjs` waits for three distinct consecutive diagnostics publications with identical viewport and visible-row geometry. Multiple reads of the same array never count as new samples. Changed bounds or a hidden layer list reset stability. The helper waits after every real wheel step, returns the settled row bounds, and fails explicitly at an unreachable target or bounded scroll limit. Selection then uses one pointer click and verifies the exact resulting layer name. It does not retry selection until a wrong click happens to be corrected.

The browser histogram case repeats the tall/small inspector and Channels/Layers transitions three more times. It continues to require unchanged tone/channel histogram build counts. Six deterministic Node tests cover stale publications, moving bounds, hidden lists, viewport resizing, input sequencing and bounded failure. They execute the same browser-side observer in an isolated JavaScript realm and run before the real browser suite through `npm run test:browser`.

A separate fixture imports a standards-shaped native archive containing 96 nonoverlapping editable shape layers. Real canvas clicks select targets across the full layer stack. Assertions require no new controls, history states, content revisions, image compositing submissions or hidden histograms after initialization. Scrolling may legitimately draw newly exposed row thumbnails, so the large-list test does not confuse those thumbnail paints with expensive full-document compositing.

## Reports and interpretation

`artifacts/browser-tests/ui-selection-performance.json` records warmed mixed-schema selection counters and synchronous C# refresh samples. `ui-selection-stress.json` records the same scope for the 96-layer document. Measurements are diagnostic samples, not event-to-photon latency, a physical-GPU benchmark, or a universal speedup. CI asserts work invariants rather than machine-dependent millisecond thresholds. First-use inspector construction and first appearance of a row are measured separately from warm selection; deferred optional preview work still has a cost when it actually runs.

The complete engine, desktop, real Uno browser and package workflows must pass on the final source before merging. Initial trace failures and their corrections are retained in the PR discussion rather than hidden by retries or removed assertions.
