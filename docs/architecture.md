# Architecture

## Separation of responsibilities

```text
Uno App host
  ├─ Browser: file input, Blob downloads, IndexedDB, WebGPU bridge
  └─ Desktop: native file pickers, atomic recovery file
                  │
           ImageSpace.Workbench
     controls / panels / commands / tabs
                  │
            ImageSpace.Editor
     camera / pointer gestures / preview
                  │
          ImageSpace.Editing
      transactions / history / invariants
         ┌────────┼───────────┐
      Imaging   Filters    Documents
         └────────┼───────────┘
             Core model
                  │
             Skia renderer
  revision-cached tiles / masks / blend / type
                  │
    Uno SKCanvasElement / host graphics backend
```

`ImageSpace.WebGpu` is an optional parallel compute service. Its output returns to `PixelSurface`; it does not establish a second competing document model. `ImageSpace.Storage` contains host contracts, not a browser singleton. The editor and workbench can be embedded without using `ImageSpace.App`.

## Pixel and ownership model

Pixels are straight-alpha RGBA8 in the sRGB working workflow. Surfaces contain sparse 128 × 128 tiles. Transparent areas allocate no tile until edited. Each tile has an ownership generation and content revision.

A snapshot copies the tile dictionary and changes ownership generations on both surfaces. Shared tiles are then immutable. A subsequent write clones only the affected tile. This supports transactional rollback and history without copying every pixel in every layer for each pointer event. Public pixel access does not expose a writable array.

The Skia cache keys entries by tile identity **and tile content revision**. Identity alone is insufficient: an owned tile can be mutated repeatedly during one brush gesture. Regressions specifically test a second write without taking a snapshot, unchanged-tile upload reuse, mask changes, and undo to an older tile generation.

Skia tile images are disposable. Viewport clipping avoids drawing off-screen tiles, and unused cache entries are pruned when the cache grows. Draw counters measure CPU submission and upload activity; they do not pretend to measure asynchronous GPU execution.

## Transactions and recovery

The document is single-writer. The UI owns its active transaction. Begin captures a snapshot; Commit validates and records before/after versions; Cancel restores the exact pre-edit document. Undo and redo preserve a separate saved-version marker. Selecting a layer is not an edit to pixel content.

History retains at most 64 undo entries and targets a 192 MiB unique-tile budget, retaining at least the newest entry. A single large document or transaction can exceed that target; it is not a process-wide hard memory limit. Native saves do not persist history or transient selections.

Recovery serializes a snapshot after committed changes, approximately every eight seconds. Browser storage uses a transactional IndexedDB record. Desktop storage writes a temporary file and atomically replaces the recovery file. Errors are surfaced to the user. Recovery never marks the document as explicitly saved. Only the active document is recovered in this initial release.

## Compositing

Layers are stored bottom to top. Each layer owns a scale/rotation/translation transform, visibility, opacity, a blend mode, optional mask and content. Pixel, text and shape content share the same renderer. Layer masks use alpha coverage and destination-in compositing.

Adjustment layers recursively compose layers below them through Skia image-filter graphs. Opacity crossfades the complete clamped effect output rather than interpolating matrix coefficients or blur radius. Mask coverage uses complementary premultiplied branches: `result = coverage * effect(source) + (1 - coverage) * source`. The branches are added, not source-over blended, so color-only adjustments retain the original alpha.

Authored mask alpha is separate from non-destructive density and feather. Coverage is `1 - density + density * featheredMask`; feather is evaluated in mask-local pixels before the linked layer transform. Skia caches the native mask graph by surface identity/revision, density, feather, transform and output bounds. View-only grayscale/overlay inspection owns a separate graph cache and borrows the renderer's tile uploads, avoiding cache-key thrashing without raster readback. Group isolation, clipping groups and independent/unlinked mask transforms remain outside this implementation.

All editing routes through `PixelTarget`. Pixel filters convert authored mask alpha to opaque grayscale, run the filter, then convert luminance back to alpha. This includes fully hidden pixels. Selection coverage is sampled at transformed pixel centers, with explicit outside-canvas rejection before integer conversion. Async filter results are discarded when the session revision, layer, edit channel or target tile revision changes.

Native archives use version 3 when adjustment masks, non-default density/feather or fractional adjustment output-crossfade semantics are present. Version 1/2 files remain readable; feature-free files retain their minimal supported version. Older readers reject version 3 instead of silently dropping mask behavior.

Export uses a separate Skia raster surface. It cannot directly reuse an arbitrary UI-thread GPU context on a background worker. PNG and WebP retain alpha; JPEG composites against white. Raster effects operate on the authoritative tile model, with selection coverage applied to the output.

## WebGPU compute

The standalone JavaScript module requests a compatible adapter and creates one reusable compute pipeline. RGBA pixels are packed as little-endian `u32` values in storage buffers. A 16 × 16 workgroup operates on one pixel per invocation. Dimensions, parameter ranges and storage-buffer limits are checked before allocation.

Eight color kernels share one shader. Dispatch writes a storage output, copies to a map-readable buffer and reads back into the editable model. GPU buffers are destroyed in a `finally` block, and device loss clears the cached device/pipeline. Unsupported kernels return `null` so the host can use its CPU implementation.

This tradeoff favors edit-model consistency and reusable public APIs over a misleading GPU-only claim. It incurs upload/readback overhead. A future resident-tile backend can implement `IComputeFilterBackend` without replacing the editor. WebGPU results and CPU kernels can differ by one byte at floating-point rounding boundaries.

## Dependency policy

The baseline was selected against the stable Uno package on September 27, 2026: Uno SDK 6.7.30 and .NET SDK 10.0.401. Managed SkiaSharp and the native Skia implementation supplied by the Uno rendering stack must stay ABI-compatible. The pinned 3.119.4 line is intentional; independently updating to a newer Skia major is not considered a safe upgrade.

References:
- Uno SDK: https://www.nuget.org/packages/Uno.Sdk/6.7.30
- Uno Skia renderer: https://platform.uno/docs/articles/features/using-skia-rendering.html
- Uno SKCanvasElement: https://platform.uno/docs/articles/controls/SKCanvasElement.html
- SkiaSharp: https://github.com/mono/SkiaSharp
- WebGPU: https://www.w3.org/TR/webgpu/
- WGSL: https://www.w3.org/TR/WGSL/

The Uno host chooses its graphics backend and may fall back to software. CI validates correctness and software-adapter behavior where available. Physical Metal/Vulkan/Direct3D hardware performance, input latency, color-profile fidelity and driver-specific behavior require separate device testing.

## UI design boundary

The workspace uses original Skia-drawn icons, custom button templates, custom menu popups, numeric editors, layer thumbnails, spectrum and histogram controls. Standard Uno primitives provide layout, focus, text input, scrolling, check boxes and dialog lifecycle. This is custom application chrome, not a reimplementation of the operating system's accessibility and text-input machinery.

The visual hierarchy follows public Photoshop workspace conventions. It does not include Adobe assets or claim pixel-identical cross-platform rendering. Reference: https://helpx.adobe.com/photoshop/desktop/get-started/learn-the-basics/workspace-overview.html
