# Independent mask placement and interaction performance

## Editing workflow

Layer masks begin linked to their content. Click the chain icon beside the mask thumbnail, the **Toggle mask link** button in Properties, or **Layer → Unlink mask from layer** to make the mask independent.

With the mask thumbnail selected, the Move tool translates an unlinked mask without moving its layer. Eight corner/edge handles resize it; the upper handle rotates it. Shift constrains movement, preserves proportions during resizing, or snaps rotation to 15-degree increments. Arrow keys move the active target by one document pixel; Shift+Arrow uses ten. Linked mask gestures move the layer and mask together using the layer's transform frame. Selecting layer content while unlinked moves the content without moving the mask.

The unlinked mask inspector exposes document-space X/Y, basis width/height and angle. Affine geometry retains reflection and shear rather than decomposing them into an approximate angle/scale model. Relinking preserves the current appearance; it does not snap the mask back. **Align mask to layer** deliberately resets the placement. All these commands participate in undo/redo and respect layer locks. Pixel tools and their brush-cursor outline use the selected target's coordinate frame.

Mask movement, resizing, rotation and relinking do not resample authored pixels. Feather remains in mask-local pixels before placement; density is applied after placing coverage. The display compositor uses Skia linear image-filter sampling. Selection loading samples authored alpha at document pixel centers; it is not a Select and Mask edge-refinement feature.

## Model and persistence

`AffinePlacement` is an immutable six-component JSON-safe row-vector affine value. `Layer.MaskPlacement` is relative to the layer when `MaskLinked` is true and relative to the document otherwise:

```text
linked:   maskToDocument = maskPlacement * layerTransform
unlinked: maskToDocument = maskPlacement
```

To unlink, store the current mask-to-document matrix. To relink, store `maskToDocument * inverse(layerTransform)`. No decomposition or source-pixel rewrite occurs. Content-local compositing converts a document-local mask with `maskToDocument * inverse(layerTransform)`.

`MaskGeometry` provides the coordinate conversions and affine handle algorithms. `EditorSession` supplies undoable link, placement, alignment and nudge commands. Document-wide crop, resize, quarter-turn and flip operations also transform unlinked masks; otherwise an unlinked mask would remain at stale coordinates when the canvas changed.

Non-default placement or unlink metadata requires native archive version **4**. Version 1–3 files remain readable and default to linked identity placement. A file that declares an older version while carrying independent placement is rejected. The writer retains minimal versions for older feature sets and preserves RGBA mask bytes exactly.

PSD remains raster interchange: the placed mask is baked into exported raster-layer pixels. Photoshop link metadata, editable affine masks, folder groups, non-default clipping-group options, vectors and smart objects are **not** losslessly roundtripped. Preserve `.imagespace` for editable placement and keep original PSDs.

## Performance changes

`PixelMapping` captures target-to-document and inverse matrices once per operation. Brush dabs, fill, gradient, clear and selection-restricted filters no longer recompute sine/cosine and SRT matrix products for every pixel. Its selection reference is operation-local under the document's single-writer discipline, not a thread-safe mutable document view.

`MaskFilterCache` now separates the authored mask/feather graph from placement/density wrappers. Translation, resize, rotation, relinking and density changes reuse the source graph. A changed pixel generation or feather radius rebuilds it. `MaskSourceBuilds` and `MaskFilterBuilds` distinguish these costs. No image readback was added.

`SelectionContours.Trace` replaces the full-frame RGBA temporary with two pooled rows and one vertical-run table. It emits exact, merged, axis-aligned threshold boundaries. Holes, disconnected components and corner-touching islands retain their edges. Complexity remains O(width × height); scratch storage is `12 × width + 4` bytes requested from pools. Contour output can still be large for checkerboard/noisy selections—this is not a constant-size path guarantee. Cached paths are released on deselection.

A solid 1024 × 1024 selection previously generated 4096 unit edges and a 4 MiB RGBA temporary; it now generates four collinear-run segments with about 12 KiB of pooled scratch. Pooled array capacity may exceed the requested byte count. Benchmark allocation measurements are warmed-pool steady-state numbers, not peak memory.

## Validation

The compatibility suite adds exhaustive comparison against scalar boundary edges for all 512 binary 3 × 3 selections, seeded soft-alpha selections across tile boundaries, cancellation/receiver-exception checks, affine reflection/shear resize anchors, movement/linking/undo, versioned persistence, selection-aware mask painting and actual Skia output.

Browser tests use the real Uno controls, mouse drags, keyboard events and file pickers. They cover independent mask movement/relinking, numeric affine editing and on-canvas resizing, native roundtrip, and four-segment full-selection geometry. Diagnostics remain read-only and opt-in.

Run:

```sh
dotnet run --project tests/ImageSpace.CompatibilityTests -c Release
dotnet run --project tests/ImageSpace.Benchmarks -c Release
npm run test:browser
```

CPU microbenchmarks compare frozen baseline functions with optimized functions in one Release process, using two warm-ups and seven alternating samples. The benchmark reports separate baseline revisions for the original raster kernels and the new interaction kernels. It excludes native path-memory allocations and actual GPU execution. Physical Metal/Vulkan/Direct3D device performance is not established by these measurements.

## Public reference

Adobe's documented linked/unlinked mask workflow:
https://helpx.adobe.com/photoshop/desktop/create-masks/layer-masks/unlink-layers-and-masks.html

This is an independent implementation of the documented workflow, not a claim of byte-identical Photoshop transforms or complete application parity.
