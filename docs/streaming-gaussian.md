# Streaming Gaussian fallback

The CPU Gaussian kernel now uses a rolling window of horizontally filtered rows rather than a full-image float intermediate. Browser WebGPU continues to use the separate [tiled GPU implementation](tiled-gaussian.md). This change improves CPU fallback and desktop Filter Gallery processing; it does not make those operations GPU-resident.

## Numerical contract

The kernel remains a separable Gaussian, with clamped source edges, a three-sigma radius, a maximum sigma of 32 and a maximum radius of 96. A nonpositive sigma returns a copy-on-write snapshot. NaN and infinity are rejected.

Horizontal accumulation keeps the original single-precision intermediate. Vertical accumulation retains the original double-precision coefficients and accumulation. Coefficient construction, tap order, premultiplied alpha treatment and final RGBA8 rounding are unchanged. Computed RGB is preserved even when output alpha rounds to zero. There is no box-blur approximation, tap reordering or lower-precision buffer in this path.

Each source row is bulk-copied from sparse tiles into a byte buffer with prefilled clamped halos. Horizontal taps therefore avoid repeated tile dictionary lookups and coordinate clamps. Vertical row offsets are prepared once per output row, not once per pixel/tap.

## Working memory

For width `W`, height `H` and radius `R`, the float intermediate changes from:

```text
Old: 16 * W * H bytes
New: 16 * W * min(H, 2 * R + 1) bytes
```

The byte source/halo/output rows request `8 * W + 8 * R` bytes. Coefficient and row-offset arrays have a total payload of `16 * (2 * R + 1)` bytes. Source and output tiles remain full-sized as needed; the processor does not claim to eliminate the edited image itself.

For a 4096x4096 image at sigma 8, the float intermediate is 3,211,264 bytes (49 rows), versus 268,435,456 bytes previously. This is an algorithmic working-buffer calculation, not a measured process-RSS reduction. At heights below the kernel diameter, the rolling cache covers all rows and provides no intermediate-size reduction.

`GaussianBlurProcessor.GetWorkspace` returns a `GaussianBlurWorkspace` value without allocating image storage. Its `RequestedScratchBytes` excludes array headers, pool bucket rounding, retained pool capacity, source/output tiles and async runtime overhead. Pooled float/byte buffers are returned in `Dispose` with clearing requested, including on failure or cancellation. Every horizontal output slot is fully overwritten before use, so stale pooled values cannot enter the image.

## Reusable APIs

```csharp
using ImageSpace.Filters;

var workspace = GaussianBlurProcessor.GetWorkspace(source.Width, source.Height, sigma: 8);

// Synchronous processing; caller keeps source immutable for the duration.
var result = GaussianBlurProcessor.Apply(source, sigma: 8, cancellationToken);

// Captures source before suspension and cooperates with the calling context.
var preview = await GaussianBlurProcessor.ApplyAsync(source, sigma: 8, cancellationToken);
```

`FilterEngine.Blur(source, sigma)` retains its existing signature and delegates to the synchronous processor. Existing callers therefore receive the reduced-memory algorithm without changing their call sites. The new async API captures copy-on-write ownership before the first suspension; changing the caller's original source afterwards cannot alter the pending evaluation.

## Scheduling and cancellation

The async processor yields with a timer initially and after an elapsed processing budget of approximately eight milliseconds, checked between complete rows. The budget is not a hard latency bound: one very wide row or pool allocation/cleanup can exceed it. Cancellation checks occur every 64 output pixels inside horizontal and vertical rows, as well as at row and timer boundaries. In a single-threaded browser, input-triggered cancellation is delivered when the event loop regains control.

`CpuFilterSession` retains its existing native worker execution. Native Gaussian work uses the synchronous cancellable processor there. Browser Filter Gallery sessions use the cooperative API for Gaussian stages. Other scalar filters still yield/cancel between stages; this change does not introduce a worker thread or row scheduling for every CPU kernel. Existing synchronous filter-menu/selection-feather callers benefit from the memory/access optimization but remain synchronous.

Microsoft explicitly warns that `Task.Yield` alone should not be relied upon to keep UI input responsive: [Task.Yield remarks](https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.yield). Array-pool ownership transfers back on return; see [ArrayPool.Return](https://learn.microsoft.com/dotnet/api/system.buffers.arraypool-1.return). No caller may use a returned scratch buffer.

The existing editor transaction layer remains authoritative: gallery previews do not change the document, and final stack output is validated against the captured target before one undoable commit. Cooperative processing does not weaken stale-result, selection or ownership checks.

## Validation

```sh
dotnet run --project tests/ImageSpace.GaussianTests -c Release
```

The suite freezes the pre-change implementation from commit `a59eca8078e553d266c4bdd8aef8dea792b22ad3` and compares exact RGBA bytes. Cases cover thin/tall images, tile and ring-wrap boundaries, zero/minimum/maximum/clamped sigma, sparse and mixed-alpha input, hidden RGB, independent output ownership, async source capture, cancellation, concurrent independent calls and dirty pooled memory. The workspace estimator is checked at maximum supported image sizes without creating those images.

The dedicated Gaussian workflow runs this suite on Windows, Linux and macOS. Build and Release also run it with all existing engine, clipping, mask, real Uno browser and GPU tests. The full GPU suite continues to compare actual GPU output with generated C# fixtures.

`gaussian-tests.json` records results. `gaussian-performance-results.json` records two warmups and seven alternating same-process samples against the frozen reference, including managed allocation, requested scratch and all timing samples. The timed path is synchronous CPU Release evaluation, not the timer-sliced UI path. Warm managed bytes exclude retained pool capacity; no machine-dependent performance ratio is asserted as a correctness requirement.

## Scope

No image-format, clipping, UI-layout, shader, package-publication or layer-model behavior is changed. The implementation is not full Photoshop parity or a hardware-GPU benchmark. It complements resident/tiled GPU processing by making the fallback less memory-intensive and the gallery's browser Gaussian stage cooperative.
