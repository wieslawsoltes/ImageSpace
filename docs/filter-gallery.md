# Filter Gallery and resident GPU pipelines

## Editing workflow

Select an unlocked raster layer's content and choose **Filter → Filter Gallery**. The new workspace contains a fitted image preview, filter catalogue and ordered effect stack. Effects run from top to bottom. Add an effect, select its row to edit numeric parameters, disable it without deleting its settings, or move/remove it using the stack buttons. The comparison slider reveals the original image on the left.

**Apply filters** processes the full authored surface and commits the entire stack as one undoable edit. The current selection is blended with the final stack result once; it is not repeatedly applied at each intermediate stage. **Cancel** leaves pixels, history and dirty state untouched. **Filter → Repeat filter stack** reuses the most recently accepted recipe on the current unlocked raster target. The stack permits sixteen operations.

This is a destructive raster workflow with an interactive preview. It does not implement persisted Photoshop Smart Filters, editable PSD filter metadata, every Photoshop Filter Gallery category, or pixel-identical Photoshop algorithms. Text/shape/adjustment layers are not silently rasterized. Mask editing retains its existing single-filter coverage workflow; the gallery requires layer content.

## Preview responsiveness

The source is sampled once to at most 256 pixels on its long edge. Gaussian sigma and Pixelate block size are scaled for the sampled preview; final application retains the original parameters and dimensions. The preview is layer-local and is not a full-document blend/selection preview. This distinction is visible in the interface.

The editor owns one immutable preview source and, when available, one resident GPU session. Parameter changes use a 120 ms coalescing timer. Only one preview runs at a time. If newer settings arrive, the stale result is discarded and the latest captured recipe is evaluated next. Changing preview controls does not start a document transaction or rebuild the retained main inspector, layers, history or document tabs. Closing the gallery cancels pending stages, waits for owned in-flight work to finish and disposes the session and preview images.

Numeric fields and the preview surface remain retained during parameter editing; stack rows are rebuilt only when the stack's structure changes. The first use still constructs the gallery and initializes pipelines. Browser CPU Gaussian fallback cooperatively processes row batches, with timer yields and cancellation checkpoints. Its rolling row cache avoids a full-image float intermediate. Other scalar CPU kernels still yield/cancel between stages rather than during a stage. Read [streaming Gaussian fallback](streaming-gaussian.md) for scheduling, ownership, memory bounds and the distinction from synchronous filter-menu callers.

## Reusable C# contracts

`ImageSpace.Filters` defines immutable `FilterOperation`, validation/capture helpers in `FilterRecipe`, and `IFilterSession`. Every session evaluation restarts from its original captured source, including when the previous result is discarded. Returned `PixelSurface` instances are independently owned.

```csharp
using ImageSpace.Filters;

await using var preview = new CpuFilterSession(source);
var recipe = new FilterOperation[]
{
    new(FilterKind.GaussianBlur, Amount: 3),
    new(FilterKind.Saturation, Amount: -35),
    new(FilterKind.BrightnessContrast, Amount: 5, Secondary: 12)
};
var output = await preview.ApplyAsync(recipe, cancellationToken);
```

`EditorSession.ApplyFilterStackAsync` accepts a backend factory and owns its disposal. It checks session/document/active-layer identity, revision, source tile revision, selection identity/revision, target transform, lock/channel/transaction state, dimensions and cancellation before committing. A failed/stale result cannot become a new history entry. Output receives copy-on-write ownership before assignment.

```csharp
await editorSession.ApplyFilterStackAsync(
    recipe,
    (pixels, token) => Task.FromResult<IFilterSession>(new CpuFilterSession(pixels)),
    cancellationToken);
```

`StudioWorkbench.FilterSessionFactory` allows a host to supply an accelerated session without introducing browser APIs into the model or editing library. `FilterGalleryEditor` can also be embedded independently. The browser host supplies `BrowserFilterSession`; desktop currently uses the CPU session, while ordinary desktop document compositing remains Skia-based.

## Resident WebGPU execution

Automatic Gaussian execution uses the direct path unless an explicit session calibration supports tiled execution. Tiled pipeline compilation is lazy; calibration is not initiated by gallery or selection interactions. See [measured dispatch policy](tiled-gaussian.md).

The original eight color kernels are retained. Gaussian blur, sharpen, emboss, edge detection and pixelation add five GPU kernels. The deterministic .NET Noise sequence remains on CPU. Five pipelines initialize normally: combined color/convolution, fused color stacks, direct Gaussian horizontal/vertical and block-reduced Pixelate. Two additional tiled Gaussian pipelines compile on demand. See [tiled Gaussian execution](tiled-gaussian.md) for strategy and workgroup-storage contracts.

A session uploads its source once and retains two RGBA ping-pong buffers, readback storage and an aligned uniform arena. Blur's premultiplied float scratch is created only when first needed. Bind groups are cached by input/output pairing. Ordered operations dispatch against these buffers, never reading intermediate stage pixels back to C# or JavaScript. Reading the final output uses a separate copy/readback submission. Repeated preview evaluations reuse the same buffers and original source.

The public JavaScript API exposes `createSession`, `applyChain`, the backward-compatible `apply`/package `applyFilter`, and session `execute`, `read`, `apply`, `statistics` and `dispose`. `execute` performs no readback. The .NET/browser integration still transfers the initial and final RGBA values through its string bridge; this increment does not claim zero-copy Wasm integration or a GPU-only document model.

Device operations and error scopes are serialized. Device loss invalidates owned resources; a new session can obtain a replacement device. Invalid inputs, allocation failures, shader errors and timeouts are reported. Initial admission checks and a 256 MiB aggregate requested-buffer budget bound residency. The adapter can impose stricter per-buffer limits. The C# host falls back to CPU when the adapter or operation cannot run, preserving the authoritative edit model.

## Validation

Run the existing engine suites plus:

```sh
dotnet run --project tests/ImageSpace.FilterStackTests -c Release
dotnet run --project tests/ImageSpace.GaussianTests -c Release
npx playwright test --config playwright.gpu.config.mjs
npm run test:browser
```

The filter-stack suite checks operation order, immutable source/result ownership, all established CPU kernels, recipe validation, cancellation, atomic undo/redo, selection handling and stale-output rejection. It also writes actual C# `FilterEngine` output fixtures. The GPU suite compares all thirteen kernels with those fixtures and an independent scalar reference, including odd dimensions, transparency, maximum blur radius, partial pixel blocks, reset-to-source, concurrent clients, resource reuse and device loss. The Gaussian suite independently freezes the pre-streaming CPU algorithm for exact all-channel comparisons and memory/ownership/cancellation tests.

Real Uno browser tests exercise gallery preview pixels, parameter controls, effect ordering/bypass/removal, Apply/Cancel, Repeat, undo/redo, native save/reopen and CPU fallback, including a blurred edge with WebGPU explicitly unavailable. No JavaScript mutation endpoint drives the C# editor. Build runs the new suites before producing a successful Pages artifact. The independent Resident GPU and Gaussian workflows provide faster diagnostics.

Reports include `filter-stack-tests.json`, `filter-stack-fixtures.json`, `gaussian-tests.json`, `gaussian-performance-results.json`, `resident-gpu-results.json` and GPU performance reports, plus the normal browser report/screenshots. Work-counter assertions require one source upload, no additional warmed buffer/bind-group construction, no intermediate readbacks, and complete resource release. GPU wall-clock samples include dispatch, completion and final mapping; SwiftShader runs validate behavior but are not physical-GPU benchmarks. CPU timing excludes cooperative timer scheduling.

## Remaining boundaries

This increment does not add layer folder groups, non-default clipping-group options, smart objects, high-bit/CMYK/ICC editing, advanced type/paths/styles/warps/healing, PSB, Photoshop plug-ins/timeline/actions or lossless Photoshop metadata roundtrips. The gallery uses original Uno controls and public workspace conventions, not Adobe artwork or private APIs. Full Photoshop feature/UI parity is not claimed.

### Preview preparation reuse

The resident backend now reuses bounded execution-plan metadata and Gaussian coefficients during repeat evaluations and parameter edits. It does not initiate calibration when opening the gallery, selecting layers or moving a parameter. Untuned automatic Gaussian execution remains direct. The before/after preview, selection-limited final commit and history semantics are unchanged. See [GPU preparation cache](gpu-preparation-cache.md).
