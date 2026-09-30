# GPU execution-policy correction and preparation reuse

## Conservative Gaussian routing

The area/radius heuristic in the first tiled-blur implementation selected a slower path on the recorded SwiftShader workloads. The correction defaults to direct Gaussian sampling. Tiled shader modules are compiled only on explicit use or explicit calibration; selection, gallery opening and ordinary parameter edits never start benchmarking.

`calibrateGaussian` compares the actual original-source RGBA result, including hidden RGB, before considering timing evidence. Tiled automatic selection requires identical output, a median speedup of at least 1.15 and a win in at least 70% of paired samples. Calibration is scoped to the session source, dimensions, device generation and normalized sigma. A Gaussian consuming a preceding stage's output has a different input and remains direct in automatic mode. Forced `direct` and `tiled` strategies deliberately override that policy.

The prepared patch in this release includes calibration from the previous local continuation and the bounded preparation caches described below. This document describes implementation, not a claim that a new public deployment or hardware benchmark has passed.

## What gets cached

Each session retains at most eight execution plans and eight Gaussian coefficient tables. A plan contains an ordered list of kernel descriptors, the session-owned input/output buffer references, and a private copy of the used uniform bytes. It never contains a filtered output image. Every evaluation resets to the original source, executes the requested operations and performs the usual final readback when requested.

Effective recipe keys include operation kind, amount, secondary amount, order and any explicit calibration override. Input operations are copied and validated before asynchronous queueing. Disabled operations and zero-sigma blur steps are removed before keying. Fusion policy, source dimensions and buffer ownership are fixed for the lifetime of a session. No plan is shared across sessions or device generations.

Gaussian coefficients are keyed by normalized sigma. They use the same double-precision `Math.exp` and normalization sequence as the uncached writer, then round once to a private `Float32Array`. A color-parameter edit after an unchanged Gaussian builds a new plan but reuses its coefficients. Cached coefficient arrays are never exposed to callers.

The final filtered pixels are **not** cached. This is preparation reuse, not a shortcut around running the shader, queue ordering, source immutability, RGBA8 stage rounding, mask/selection restriction or transaction ownership. The seven WGSL source strings remain unchanged by this increment.

## Invalidation and ownership

A successful calibration, forced recheck or explicit profile clear invalidates execution plans, because their shader strategy is captured at preparation. Failed/cancelled calibration never installs a new profile. Profile eviction also clears plans before another evaluation can use outdated routing. Coefficients depend only on sigma and remain reusable across profile changes.

The plan cache and coefficient cache use bounded least-recently-used eviction. Evicting a plan drops its CPU metadata only; GPU buffers remain session-owned. Disposing the session or losing its device releases all plans, coefficient tables, bind groups and buffer ownership. Existing serialized error scopes and queue ordering are retained.

Calibration continues to preserve source/ping/pong results using the already-owned second ping-pong buffer. No additional source upload or backup-image allocation is introduced. Cancellation cannot preempt already-submitted GPU work.

## Resource accounting

The per-session plan limit is eight and the recipe limit is sixteen uniform slots. Each slot is 816 bytes rounded up to the device's uniform-offset alignment. With a 1024-byte slot stride, all eight maximum-size templates retain at most 131,072 bytes. Eight maximum-radius Gaussian tables retain at most 6,176 coefficient bytes. Descriptor objects, keys, map overhead and the fixed staging arena add separate JavaScript memory costs.

`cachedParameterBytes` counts the exact retained template byte lengths, not process RSS, driver memory or GPU allocation. The existing 256 MiB budget still applies to requested GPU buffer bytes; it does not include host-side caches. There is no claim that the application as a whole is allocation-free.

| Counter | Meaning |
| --- | --- |
| `executionPlanBuilds` / `executionPlanHits` | Cumulative metadata preparations and recipe hits |
| `gaussianWeightBuilds` / `gaussianWeightHits` | Cumulative coefficient construction and reuse during new plan preparation |
| `executionPlansCached` / `gaussianWeightsCached` | Current retained entry counts, each at most eight |
| `cachedParameterBytes` | Current retained uniform-template bytes; zero after disposal |

A plan hit does not look up coefficients again, so it increments `executionPlanHits` rather than `gaussianWeightHits`. These counters measure host preparation, not GPU completion or a frame-rate increase.

## Reuse API

```ts
import { createSession } from '@wieslawsoltes/imagespace-webgpu';

const session = await createSession(rgba, width, height);
if (session !== null) {
  try {
    const recipe = [
      { kind: 'GaussianBlur', amount: 8 },
      { kind: 'BrightnessContrast', amount: 5, secondary: 12 },
      { kind: 'Gamma', amount: 1.25 }
    ] as const;

    const first = await session.apply(recipe);
    const repeated = await session.apply(recipe); // Reuses metadata, reruns filters.
    console.log(session.statistics());

    // Opt-in profiling; do not call this on selection or automatically per frame.
    await session.calibrateGaussian(8, { samples: 5, warmups: 1 });
    const calibrated = await session.apply(recipe); // Routing plan is rebuilt.
    await session.clearGaussianCalibrations();
  } finally {
    session.dispose();
  }
}
```

This API remains a browser/WebGPU service. The C# authoritative model, desktop CPU filter backend, retained Uno panels and readback/string bridge are unchanged.

## Validation contract

`npm run test:browser-helpers` exercises the production scheduling and preparation code with descriptor/symbolic-byte doubles. Tests verify one plan/one coefficient construction across 250 repeated evaluations, no skipped dispatches, independent coefficient reuse during parameter changes, byte-identical coefficient uploads, arena overwrite protection, LRU bounds, immutable request capture, calibration invalidation, source-only routing, session ownership and device loss. The doubles do not compile WGSL, compute a real Gaussian or measure a GPU.

`tests/gpu/plan-cache.spec.mjs` adds real-WebGPU comparisons against fresh sessions through parameter edits and eviction, warm mixed-stack output/resource checks, and calibrated-source versus intermediate-input routing checks. Existing real shader, CPU-reference, clipping, transparency and Uno interaction tests remain in place. A headless-contract pass is not a substitute for those execution checks.

Build and Resident GPU run dependency-free contracts early and retain their TAP output. Exact pinned dependencies and real-browser/GPU execution must be verified on the new commit before merging. Local engine checks performed with an offline Skia override must be identified separately from the repository's pinned 3.119.4 set.

References: [WebGPU queue completion](https://gpuweb.github.io/types/interfaces/GPUQueue.html#onSubmittedWorkDone), [WebGPU buffer mapping](https://gpuweb.github.io/types/interfaces/GPUBuffer.html#mapAsync), [WebGPU specification](https://gpuweb.github.io/gpuweb/).
