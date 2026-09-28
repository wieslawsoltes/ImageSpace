# Apply Layer Mask and direct layer rendering

## Apply a raster mask

Select an unlocked pixel layer with an enabled mask, then choose **Layer → Apply layer mask**. The editor samples effective mask coverage, multiplies it into the authored pixel alpha, and removes the separate mask in a single undoable transaction. **Help → Applying masks and rendering** contains the same operational guidance in the application.

The command preserves source RGB (including hidden colors), authored pixel dimensions, off-canvas content, layer transform, blend mode, opacity, and the current selection. Selection does not restrict this whole-mask operation. Undo restores the original pixels, editable mask, mask properties and edit channel; redo reapplies it. Native archives save the resulting pixel surface without requiring a new archive version.

Disabled masks are not applied silently. Enable a mask first, or use **Delete mask** to discard its effect. The command accepts raster layers only. It does not silently rasterize editable text/shapes or flatten an adjustment layer. Applying an adjustment mask is a different compositing operation and remains unsupported.

Coverage is sampled at the source's authored pixel resolution. An affine or feathered live mask may produce different subpixel edge sampling when a baked layer is later displayed under scaling or rotation. Applying a mask therefore preserves authored RGB and geometry, not editable mask metadata or every zoom-dependent sample. Retain an editable native copy before baking important masks.

## Reusable APIs and ownership

```csharp
using ImageSpace.Editing;
using ImageSpace.Skia;

// Invoke on the renderer's owning thread after finishing any active gesture.
using var renderer = new ImageRenderer();
var session = new EditorSession(document);
if (session.CanApplyLayerMask)
    session.ApplyLayerMask(renderer.BakeLayerMask);
```

`ImageRenderer.BakeLayerMask(Layer)` is a pure model operation: it returns a copy-on-write `PixelSurface` without assigning pixels, removing masks, or changing layer metadata. It evaluates the same `MaskFilterCache` density/feather/affine graph used for display, in layer-local coordinates over the complete source extent. Unlike a canvas-sized raster export, it does not cut away authored content outside the document. Density zero returns a snapshot without a coverage readback.

For nonzero density the implementation explicitly rasterizes coverage and reads it back. It visits allocated source tiles and computes `newAlpha = (sourceAlpha * coverageAlpha + 127) / 255`. Source RGB never passes through premultiplication or color conversion. Unchanged pixels keep their existing tile ownership where possible. This is a destructive editing operation, not a GPU-resident render path or a zero-allocation promise.

`EditorSession.ApplyLayerMask(Func<Layer, PixelSurface>)` keeps Editing independent of Skia. The callback receives a detached layer snapshot. Output must preserve source dimensions and arrive without a session revision/target change. Null, wrong-size, stale, failed, locked, disabled and in-gesture requests are rejected before the application transaction begins. Accepted output receives independent copy-on-write ownership and commits exactly one history entry. The editor remains single-writer; consumers must route document edits through the session rather than mutating its public model reentrantly.

## Direct-render eligibility

The renderer retains document-level isolation so effects cannot blend against workspace chrome or export backgrounds. It skips **per-layer** `SaveLayer` only when the layer has Normal blending, opacity exactly one, and no effective mask. A disabled mask or density-zero mask has no effective coverage operation. Standalone `RasterizeLayer` may also draw directly when it explicitly ignores opacity and blending.

Effective masks, fractional opacity, and other blend modes keep their original isolated path. In particular, opacity is still applied to the combined fill/stroke or text result, not independently to overlapping primitives. Raster layers no longer allocate an unused shape paint. Stopwatch timestamps replace an allocated stopwatch in the document draw path. Canvas save stacks are restored in `finally` blocks.

`EnableDirectLayerDrawing = false` selects the always-isolated reference path for deterministic comparisons. `DirectLayerDraws` and `IsolatedLayerDraws` are cumulative path counters, not physical GPU timings. Normal premultiplied compositing is associative in real arithmetic, but changing 8-bit intermediate grouping can introduce small rounding differences. Differential tests allow at most two RGBA byte values over an opaque backdrop; exact-identity cases use exact comparisons.

## Validation and measurement

```sh
dotnet run --project tests/ImageSpace.LayerTests -c Release
npm run test:browser
```

The layer suite covers all existing layer blend modes across raster, text, rectangle and ellipse content, transformed/reflected placement, opacity, mask bypass/fallback, warm tile reuse, save-stack balancing, mask coverage, hidden RGB, off-canvas extent, linked/unlinked affine frames, density/feather, rejected output, transaction ownership, undo/redo and native persistence. Existing engine, compositor, tonal, mask-placement and compatibility suites continue to run.

Two additional browser scenarios use real pointer/menu/keyboard/file input and screenshot pixels for Apply Layer Mask, undo/redo, save/reopen and disabled-mask command availability. They do not mutate the C# model through JavaScript.

CI emits `layer-tests.json` and `layer-performance-results.json` alongside the existing validation artifacts. The timing fixture compares eight 1024 × 768 translucent pixel layers with unit layer opacity: three warmups and nine alternating same-process samples, warmed tile caches, Release configuration, raster CPU surface. It records medians and warm managed allocation without asserting a machine-dependent speed threshold. This does not establish GPU performance, frame rate, interactive latency, or an application-wide speedup.

## Scope

This increment does not implement clipping-mask chains, layer groups, smart objects, high-bit/CMYK/ICC workflows, advanced type/paths/styles/warps/healing, PSB, or lossless Photoshop metadata roundtrips. These remain separate compatibility work.
