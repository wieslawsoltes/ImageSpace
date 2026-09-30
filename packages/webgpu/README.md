# ImageSpace WebGPU

MIT-licensed RGBA8 filter pipelines with immutable resident source images, cached buffers and ordered GPU filter stacks. This JavaScript/WGSL package does not depend on Uno, .NET, a UI framework or an Adobe SDK.

## Resident editing

```js
import { createSession, capabilities } from '@wieslawsoltes/imagespace-webgpu';

const session = await createSession(rgbaBytes, width, height);
if (session === null) {
  // No compatible adapter, or the initial allocation exceeds a supported limit.
  // Run your application's CPU fallback here.
} else {
  try {
    const filtered = await session.apply([
      { kind: 'GaussianBlur', amount: 3 },
      { kind: 'BrightnessContrast', amount: 8, secondary: 15 },
      { kind: 'Sepia', enabled: false }
    ]);
    // A second evaluation starts from the SAME original image, not `filtered`.
    // The source is not uploaded again; buffers and bind groups are reused.
    const nextPreview = await session.apply([
      { kind: 'GaussianBlur', amount: 5 },
      { kind: 'BrightnessContrast', amount: 8, secondary: 15 }
    ]);
    console.log(session.statistics(), capabilities());
  } finally {
    session.dispose();
  }
}
```

Use `execute(operations)` to leave the output resident without reading pixels. Use `read()` for a detached `Uint8Array` of the most recent result. `apply(operations)` combines those operations. An empty or entirely disabled stack restores the original captured source. Session source bytes are copied before asynchronous initialization, including when the caller supplies a subarray with a nonzero byte offset.

`applyFilter(rgba, width, height, kind, amount, secondary)` remains backward-compatible. `applyChain(...)` creates a temporary session and disposes it after returning the final output. For interactive parameter edits, retain a session instead of repeatedly calling these convenience methods.

## Kernels and semantics

Thirteen kernels are implemented: Invert, Grayscale, Sepia, BrightnessContrast, Saturation, Gamma, Threshold, Posterize, GaussianBlur, Sharpen, Emboss, Edges and Pixelate. Color/convolution kernels operate on straight-alpha RGBA8. Gaussian blur uses a premultiplied float intermediate and separable passes. Pixelate uses a 64-lane workgroup reduction per block, with integer alpha-weighted accumulation and partial-edge block handling.

Parameters follow the established ImageSpace CPU implementation. Gaussian amount is sigma, clamped to 0–32 pixels; positive sigma uses a three-sigma kernel. Pixelate amount is a 2–128-pixel block size. Individual stages round back to RGBA8 except the float intermediate between the two Gaussian passes. Floating-point implementations can differ by a byte near rounding boundaries, with accumulated differences possible in long chains. The regression suite compares actual GPU output against both a scalar JavaScript oracle and generated C# FilterEngine outputs.

Seeded Noise is deliberately unsupported by this module. ImageSpace's C# host falls back to its existing CPU sequence rather than presenting a different random generator as an identical result. Unsupported one-shot operations return `null`; unsupported operations on an existing session throw and retire that session.

## Resource and concurrency contract

Each session owns an immutable source, two RGBA ping-pong buffers, one map-readable buffer and an aligned uniform arena. Gaussian scratch is allocated lazily. Bind groups and pipelines are reused. Up to sixteen operations execute with no intermediate CPU pixel transfers. A compute submission is followed by a separate copy/readback submission when reading the output.

The aggregate requested-buffer budget is 256 MiB. Initial over-budget/unsupported-buffer requests return `null`; a later blur scratch allocation that exceeds the budget or adapter limit throws. The host should release the session and use CPU fallback. This budget counts requested GPU buffer sizes, not total driver memory, JavaScript arrays, managed pixels or map result copies. Dimensions are limited to 8192 per side and sixteen megapixels, but not every image size can execute every operation within a particular adapter's limits.

Operations are serialized across the device, including error scopes, so concurrent callers cannot accidentally pop one another's asynchronous validation errors. Validation, out-of-memory and internal GPU errors are surfaced. Device loss invalidates sessions; callers must create a new one. Explicit, idempotent `dispose()` releases buffers. A failed in-flight operation retires its session rather than reusing partially written output. A readback timeout is reported rather than silently accepted.

`statistics()` reports source uploads, bytes, dispatches, submissions, readbacks, buffer allocations and bind-group construction. These are work counters—not GPU timing. CPU submission timestamps and software-adapter wall-clock benchmarks do not establish physical-GPU performance.

## Build and validate

```sh
npm run pack:webgpu
dotnet run --project tests/ImageSpace.FilterStackTests -c Release
npx playwright test --config playwright.gpu.config.mjs
```

Run from the repository root. Tests require Python 3, the pinned .NET SDK, Node and installed Playwright Chromium. Registry publication is separate from producing the tarball. HTTPS or localhost is required for browser WebGPU.

For Uno integration, transactional editing and the gallery workflow, see `docs/filter-gallery.md` in the repository.

## Quantization-preserving color fusion

Adjacent enabled color operations now share one WGSL dispatch. Each operation still packs to RGBA8 and decodes before the next; fusion does not silently change the filter chain to a higher-precision algorithm. Blur, convolution and pixelation terminate a color run. The `createSession` option `{ fuseColorOperations: false }` selects the unfused reference path. `logicalOperations`, `fusedPasses`, `dispatches` and `parameterBytesUploaded` distinguish work without pretending to measure physical GPU time. The session reuses its host parameter arena; initial/final image copies and readbacks remain.

The differential suite covers every ordered pair of color kernels, a sixteen-stage chain and mixed spatial chains, including hidden RGB and low-alpha inputs. The eight-stage benchmark compares two warmed resident sessions on the same adapter, retaining all timing samples and resource counters.

## Evidence-gated Gaussian execution

`createSession` accepts `{ gaussianBlur: 'auto' | 'direct' | 'tiled' }`. **Auto now uses direct for every uncalibrated sigma**. The original area/radius heuristic was removed after real CI reports showed that tiled execution was slower on SwiftShader despite fewer algorithmic loads. The shader math is unchanged.

Explicit `session.calibrateGaussian(sigma, { samples: 5, warmups: 1, signal })` compares real RGBA output and alternating queue-completion samples. It selects tiled only for byte-identical output, at least 1.15× median speedup, and at least 70% paired wins. It preserves the currently published result, reuses existing source/ping-pong resources, and never runs automatically during interactive work. The returned immutable profile is limited to this session and exact normalized sigma; at most eight are cached. `gaussianCalibration(sigma)` reads a profile; `clearGaussianCalibrations()` removes profiles. `force: true` explicitly repeats calibration.

Five baseline pipelines initialize normally. The two tiled pipelines compile only when first forced or calibrated, bringing the actual initialized count to seven. Horizontal 32×4 tiles retain packed RGBA in 3584 bytes; vertical 4×32 tiles retain float data in 14,336 bytes. Counters report requested shader loads, pass counts, calibrations and cache hits—not physical GPU timing. The frozen `shaderSources` diagnostic map is readable without initialization.

Read [the measurements, ownership and validation contract](../../docs/tiled-gaussian.md). Calibration has a real cost and should be an explicit profiling action, not a pointer-handler task. Software-adapter results do not certify hardware-GPU performance. New calibration tests need their own CI execution; previous tiled-kernel results are not substituted for them.

## Bounded host preparation caches

A resident session keeps up to eight least-recently-used execution plans and eight Gaussian coefficient tables. Repeating a recipe still dispatches the shaders, but reuses its private uniform template and routing descriptors. Editing a color operation after an unchanged Gaussian can reuse the coefficient table. Cached plans never contain output pixels, are never shared between sessions and are cleared on disposal or device loss.

`executionPlanBuilds`, `executionPlanHits`, `gaussianWeightBuilds` and `gaussianWeightHits` expose construction/reuse counts. `executionPlansCached`, `gaussianWeightsCached` and `cachedParameterBytes` report retained preparation state. The parameter-byte figure excludes JavaScript object overhead, the reusable staging arena, coefficient arrays and all GPU allocations; it is not a process memory measurement. Eight full sixteen-slot plans retain at most 128 KiB of parameter snapshots at a 1024-byte slot stride.

Changing or clearing a Gaussian calibration invalidates prepared plans. An automatic source calibration applies only to a Gaussian directly consuming the original source, not one following another effective stage. Forced direct/tiled strategies remain available. See the [full contract](../../docs/gpu-preparation-cache.md).
