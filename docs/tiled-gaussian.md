# Gaussian WebGPU execution: measure before choosing tiles

ImageSpace provides both direct and workgroup-tiled implementations of the existing separable Gaussian operation: clamped source edges, three-sigma radius (maximum 96), a premultiplied float intermediate, and final straight-alpha RGBA8 output. The operation and supported image formats are unchanged.

**Automatic execution defaults to the direct shader.** The original area/radius heuristic is removed. A lower algorithmic storage-load count did not translate into faster execution on the tested adapter. Tiled execution is now an explicit option or a decision backed by an opt-in, per-session calibration. Opening Filter Gallery, editing parameters or applying a normal filter never initiates calibration.

## Why the default changed

PR #13's first complete Build passed all correctness checks, including direct/tiled byte comparisons, but its performance artifact recorded the following medians on **SwiftShader software WebGPU**:

| Image | Sigma | Direct | Tiled |
| --- | ---: | ---: | ---: |
| 512 × 256 | 2 | 48.2 ms | 770.2 ms |
| 512 × 256 | 8 | 153.1 ms | 848.8 ms |
| 257 × 129 | 32 | 147.7 ms | 356.9 ms |

These are historical measurements from Build **36636163263**, source **71945140a31a88c668783b692dc37aba48b1dba7**, reported in `resident-gpu-tiled-performance.json`. They include dispatch, copy, mapping and returned array allocation, with two warmups and seven alternating samples. They are not measurements of this correction, physical-GPU timings, or an application-wide speedup. They demonstrate why the old default was unsafe to merge despite passing correctness tests.

Source report: https://github.com/wieslawsoltes/ImageSpace/actions/runs/36636163263/artifacts/11065168769

## Public API

```javascript
import { createSession } from '@wieslawsoltes/imagespace-webgpu';

const abortController = new AbortController();
const session = await createSession(rgba, width, height, {
  gaussianBlur: 'auto',       // Untuned sigma values use direct execution.
  fuseColorOperations: true  // Independent of Gaussian selection.
});
if (session !== null) {
  try {
    // Normal interactive work does not benchmark anything.
    const pixels = await session.apply([{ kind: 'GaussianBlur', amount: 8 }]);

    // Explicit maintenance/profiling action only; do not run in a pointer/selection handler.
    const report = await session.calibrateGaussian(8, {
      samples: 5, warmups: 1, signal: abortController.signal
    });
    console.log(report.selected, report.directMedian, report.tiledMedian);

    // Uses the measured strategy for this exact normalized sigma and this session.
    const next = await session.apply([{ kind: 'GaussianBlur', amount: 8 }]);
    console.log(session.gaussianCalibration(8));
    await session.clearGaussianCalibrations();
  } finally {
    session.dispose();
  }
}
```

`direct` and `tiled` force their respective paths regardless of recorded calibration. Invalid strategy values fail before adapter acquisition. `auto` uses direct unless a retained profile selects tiled for that sigma. Profiles do not transfer across images, dimensions, sessions, devices, or sigma values. Gaussian amounts normalize to the existing supported range; zero blur is a bypass and cannot be calibrated.

`calibrateGaussian` requires 3, 5, 7 or 9 samples and 1–3 warmups. It checks actual direct/tiled RGBA8 output, including hidden RGB, before permitting the alternative. Tiled is selected only with exact output equivalence, at least **1.15× median speedup**, and paired wins in at least **70% of samples**. Zero-resolution clock samples, marginal improvements, inconsistent wins, or differing output retain direct execution. This conservative policy still cannot guarantee performance under future device contention.

The method returns an immutable report containing both timing arrays, medians, output differences, selection, generation and measurement scope. An existing profile is returned without new GPU work; `{ force: true }` reruns calibration. At most eight profiles are retained per session. `clearGaussianCalibrations` is serialized with other operations. Disposal/device loss invalidates the profiles, and a replacement session starts conservatively.

## Calibration ownership and timing

Calibration runs on the library's serialized device queue. It reuses the immutable source and existing ping-pong, uniform and readback buffers. Its single-Gaussian evaluations write only `ping[0]`; a currently published `ping[0]` image is first copied into the otherwise unused `ping[1]`. A prior source or `ping[1]` image needs no backup. The published result is restored on completion or cancellation. Source pixels are not uploaded again, and there is no separate backup allocation. Gaussian float scratch can still be allocated on first use, subject to the existing residency budget.

The two correctness evaluations perform real readbacks and increment normal work counters. The timing samples then alternate direct and tiled execution and await `GPUQueue.onSubmittedWorkDone()`. They exclude pipeline compilation and correctness readback, but include parameter upload, command encoding, queue completion and JavaScript scheduling. They are wall-clock completion measurements, **not timestamp-query GPU durations**. Other users of the same hardware and browser scheduling can affect them. This scope differs from the historical end-to-end table above.

Abort is observed between completed stages; submitted GPU work is not preempted. Cancelling preserves the previous output and leaves no partially learned profile. Other GPU failures retire the session rather than reuse potentially invalid resources. Queue completion and readback waits have explicit timeouts. Reentrant `dispose()` and device loss release residency and reject further use.

See the normative WebGPU queue-completion contract: https://www.w3.org/TR/webgpu/#dom-gpuqueue-onsubmittedworkdone

## Lazy pipeline compilation

Normal initialization compiles the existing five pipelines for color/convolution, fused color, direct horizontal/vertical blur and Pixelate. Tiled horizontal/vertical pipelines compile only on the first forced tiled evaluation or explicit calibration and are reused thereafter. `describe().pipelineCount` reports actual initialized pipelines rather than a hardcoded total. Shader-source diagnostics expose a frozen `shaderSources` map; reading it does not initialize an adapter.

A colour-only session or an untuned automatic blur therefore pays no tiled-pipeline compilation cost. There is still initial compilation of the five baseline pipelines; no zero-startup-cost claim is made.

## Kernel layout and work counters

Horizontal workgroups contain 32 × 4 lanes and load four packed-RGBA rows plus their left/right halos. At radius 96 the tile occupies `(32 + 2 × 96) × 4 = 896` words, or **3584 bytes**. Vertical workgroups contain 4 × 32 lanes and retain 896 exact `vec4f` intermediate entries, or **14,336 bytes**. All lanes reach the barrier, including tail lanes outside the image; the bounds return occurs afterwards.

The shader source and mathematical tap order are unchanged by this scheduling correction. Both kernels use 128 invocations and fit the core WebGPU shared-memory baseline. The full-resolution float intermediate remains allocated; tiling does not remove it.

`tiledGaussianPasses`, `directGaussianPasses` and `gaussianInputReads` count encoded work, not asynchronous completion. The direct pair requests `2 × W × H × (2R + 1)` shader-input loads. The tiled pair requests:

```text
4 × (32 + 2R) × (
    ceil(W / 32) × ceil(H / 4)
  + ceil(W / 4)  × ceil(H / 32)
)
```

These are algorithmic accesses, including halo and tail loads—not measured DRAM traffic. Workgroup barriers, scheduling, register pressure and the implementation affect observed execution time. `gaussianCalibrations` and `gaussianCalibrationHits` distinguish explicit profiling from reuse. Calibration's submissions, dispatches and readbacks remain included in the other counters.

## Validation

```sh
npm run test:browser-helpers
npm run pack:webgpu
dotnet run --project tests/ImageSpace.FilterStackTests -c Release
npx playwright test --config playwright.gpu.config.mjs
```

The Node tests validate descriptor/storage constraints, cooperative addressing and scheduling/ownership. The scheduling double copies symbolic bytes and advances a synthetic clock; it neither compiles WGSL nor measures GPU performance. Deterministic policy cases cover slow, fast, marginal, noisy and mismatched alternatives, cache bounds, forced strategies, source/ping/pong restoration, cancellation, concurrent callers and device loss.

The real-GPU suite retains 56 direct/tiled boundary/stack comparisons and the existing C# output fixtures, plus new automatic-default, lazy-compilation, calibration, preserved-preview and cancellation checks. New calibration reports use `resident-gpu-calibration-*.json`. Historical passing GPU results must not be presented as execution of new tests.

This optimization adds no new Photoshop tools, desktop filtering backend, Smart Filters, archive semantics, or UI-parity claim. Initial/final browser interop transfers and the existing document model remain unchanged.

## Prepared-plan integration

Automatic profiles apply only when the Gaussian consumes the session's original source (the first effective operation after disabled/no-op steps are removed). A Gaussian following another effect receives different input pixels and stays on direct sampling, even when the same sigma was calibrated on the original image. An explicit `gaussianBlur: "tiled"` remains a deliberate override.

Every successful calibration or explicit profile clear invalidates prepared execution plans. Cached immutable parameter templates and Gaussian coefficients avoid repeated host preparation without altering the shader code or returning a previous output. See [GPU preparation cache](gpu-preparation-cache.md) for resource bounds, diagnostic counters and validation scope.
