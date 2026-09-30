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

Authored mask alpha is separate from non-destructive density and feather. Coverage is `1 - density + density * featheredMask`; feather is evaluated in mask-local pixels before affine mask placement. Masks can be linked to content or placed independently in document space; unlink/relink preserves the current document-space matrix. Skia caches the recorded source/feather graph by surface identity/revision and feather, then caches placement/density wrappers separately. Placement edits therefore do not rerecord source pixels. View-only grayscale/overlay inspection owns a separate graph cache and borrows tile uploads without raster readback. Folder groups remain outside this implementation. Grouped clipping chains now use the base-alpha-preserving compositor described in [clipping and fusion](clipping-and-fusion.md). See [independent mask placement](mask-placement.md) for the affine and native archive v4 contract.

All editing routes through `PixelTarget`. An operation-local `PixelMapping` captures the selected target's forward/inverse affine frame and selection, avoiding matrix reconstruction and trigonometry inside coverage loops. Pixel filters convert authored mask alpha to opaque grayscale, run the filter, then convert luminance back to alpha. This includes fully hidden pixels. Selection coverage is sampled at transformed pixel centers, with explicit outside-canvas rejection before integer conversion. Async filter results are discarded when the session revision, layer, edit channel or target tile revision changes.

Native archives require version 5 for clipping relationships, version 4 for independent/link placement metadata, version 3 for adjustment masks, non-default density/feather or fractional adjustment output crossfades, and version 2 for Curves/Levels. Versions 1–3 remain readable and default to linked identity placement. The writer retains the minimal supported version when later features are unused; older readers reject newer versions rather than silently dropping their semantics.

Export uses a separate Skia raster surface. It cannot directly reuse an arbitrary UI-thread GPU context on a background worker. PNG and WebP retain alpha; JPEG composites against white. Raster effects operate on the authoritative tile model, with selection coverage applied to the output.

## WebGPU compute

Automatic Gaussian execution uses the direct path unless an explicit session calibration supports tiled execution. Tiled pipeline compilation is lazy; calibration is not initiated by gallery or selection interactions. See [measured dispatch policy](tiled-gaussian.md).

The standalone engine compiles five pipelines implementing thirteen RGBA8 filters: color/convolution, fused color stacks, Gaussian horizontal, Gaussian vertical and block-reduced Pixelate. Seeded Noise retains the established CPU sequence. See [Filter Gallery](filter-gallery.md) for the complete backend and UI contracts.

Resident sessions capture an immutable source, allocate two RGBA ping-pong buffers, an aligned parameter arena and readback storage, and allocate premultiplied float blur scratch only when needed. Bind groups and pipelines are reused. Each evaluation resets to the original source; intermediate filter stages never cross back to CPU. Explicit execute/read/apply methods separate computation from optional output readback. Reading adds a separate copy/map submission.

Device error scopes and submissions are serialized across callers. Loss, disposal, validation failures and allocation limits invalidate sessions explicitly. Aggregate requested GPU buffers are capped at 256 MiB; per-buffer adapter limits can be stricter. This is not a process-wide memory bound and does not include JavaScript/managed image copies or native driver allocations. Spatial stages retain their computed hidden RGB deterministically for subsequent filters; color-only stages keep their established transparent-pixel normalization.

The browser host uses these sessions for gallery previews and commits, retaining initial/final .NET-to-JavaScript RGBA transfers. A sampled 256-pixel preview is not a full-resolution proof of final output. CPU fallback is available for unsupported operations, adapters and memory limits; desktop gallery evaluation currently uses CPU kernels while the document compositor remains Skia-based. Device-loss and SwiftShader tests establish correctness, not physical-GPU speed or input-to-photon latency.

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

### Bounded GPU preparation

Resident sessions retain at most eight execution plans and eight Gaussian coefficient tables, separate from image buffers. Plans capture private uniform bytes, ping/pong routing and shader selections. Reusing a plan never skips execution or returns cached output. Calibrated automatic routes apply only to original-source Gaussian inputs and invalidate plans when updated. [Preparation and lifetime contract](gpu-preparation-cache.md).

## Channel Mixer and Exposure (0.6)

Editable color mixing and linear-light exposure use retained inspectors and cached Skia filters. Native archive v6 preserves their settings; older archive versions remain readable. See [the workflow, algorithms, versioning and reusable APIs](channel-mixer-exposure.md). Full Photoshop, HDR, ICC and lossless PSD adjustment metadata are not implied.
