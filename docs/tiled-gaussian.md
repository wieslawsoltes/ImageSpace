# Tiled Gaussian WebGPU execution

Gaussian blur now has a workgroup-tiled implementation alongside the direct-reference shaders. Both implement the existing separable Gaussian operation: clamped source edges, a three-sigma radius (maximum 96), a premultiplied float intermediate, and one final straight-alpha RGBA8 output. This is a processing optimization, not a change to the filter's public sigma parameter or Photoshop compatibility.

## Choosing the path

```javascript
import { createSession } from '@wieslawsoltes/imagespace-webgpu';

const session = await createSession(rgba, width, height, {
  gaussianBlur: 'auto',       // 'direct' and 'tiled' force a comparison path.
  fuseColorOperations: true  // Independent of the Gaussian strategy.
});
if (session !== null) {
  try {
    const result = await session.apply([
      { kind: 'GaussianBlur', amount: 8 },
      { kind: 'Saturation', amount: -25 },
      { kind: 'Gamma', amount: 1.2 }
    ]);
    console.log(session.statistics());
  } finally {
    session.dispose();
  }
}
```

An omitted strategy selects `auto`. The initial heuristic uses tiles at a kernel radius of at least six and an image area of at least 4096 pixels; smaller workloads use direct sampling. `auto` is not driver-specific autotuning and cannot guarantee the fastest path on every image/device. Forced strategies support profiling and differential validation. Invalid strategy values fail before adapter acquisition or allocation.

Existing one-shot filters and the browser Filter Gallery use the default strategy automatically. Desktop filtering, UI layout, undo ownership, authored pixel dimensions and archive formats are unchanged. No GPU copy is added between the two Gaussian passes.

## Kernel layout

The horizontal pass uses 32 × 4 lanes. Each workgroup cooperatively loads four rows with a left/right halo into packed-RGBA workgroup memory. Its largest tile has `(32 + 2 × 96) × 4 = 896` elements, or 3584 bytes. Keeping packed input avoids introducing an extra premultiplication rounding boundary.

The vertical pass uses 4 × 32 lanes and cooperatively loads four columns with top/bottom halos from the existing float intermediate. Its 896 `vec4f` entries occupy 14,336 bytes. Both kernels use 128 invocations and stay within the core WebGPU workgroup limits without requesting elevated limits. See the [WebGPU limits](https://www.w3.org/TR/webgpu/#limits).

All lanes load their assigned elements and reach `workgroupBarrier`, including lanes outside the image in a partial workgroup. The image-bounds return occurs only after synchronization. Each valid output accumulates taps in the same order as the direct shader. Coordinates clamp exactly as in the reference. The [WGSL uniformity requirements](https://www.w3.org/TR/WGSL/#uniformity) explain why the barrier cannot follow a per-lane early return.

The direct horizontal/vertical shaders remain available. Two additional cached compute pipelines bring the module total to seven; this does not mean seven new filter types. Buffer and bind-group ownership, aggregate residency limits, error-scope serialization and device-loss cleanup retain their existing contracts.

## Work counters

`SessionStatistics` adds `tiledGaussianPasses`, `directGaussianPasses` and `gaussianInputReads`. These accumulate with completed command encoding; they do not measure asynchronous GPU completion.

For a width `W`, height `H` and radius `R`, the direct pair performs `2 × W × H × (2R + 1)` shader-input loads. The tiled pair's cooperative load count, including clamped halo and unused tail lanes, is:

```text
4 × (32 + 2R) × (
    ceil(W / 32) × ceil(H / 4)
  + ceil(W / 4)  × ceil(H / 32)
)
```

These are algorithmic accesses in the shaders, not hardware performance counters, bytes measured on the bus, or actual DRAM transactions. Driver caching, workgroup scheduling and synchronization affect observed time. The original full-resolution float intermediate still exists; tiling reduces repeated input reads rather than removing that surface.

## Validation and reports

```sh
node --test tests/tooling/tiled-blur.test.mjs
dotnet run --project tests/ImageSpace.FilterStackTests -c Release
npx playwright test --config playwright.gpu.config.mjs
```

The Node suite verifies declarations, storage bounds, cooperative coverage, address arithmetic and pre-initialization argument rejection. Its descriptor-only double does not execute WGSL and is not GPU validation.

The real-GPU suite compares all four channels, including hidden RGB, across seven image shapes, seven sigma settings and multi-filter chains: 56 paired direct/tiled comparisons, followed by auto-choice checks. It also checks source immutability, bypass/reset, no readback during `execute`, reuse of warmed allocations, invalid options and device-loss cleanup/recreation. The existing C# reference, transparent-stack and fused-color tests continue to run.

`resident-gpu-tiled-parity.json` records each comparison. `resident-gpu-tiled-performance.json` records three workloads with two warmups and seven alternating samples per strategy. Timings include dispatch, copy, map completion and output allocation. The tests assert work/resource invariants rather than machine-dependent speed thresholds. SwiftShader results validate software-adapter behavior and do not certify hardware GPU performance or whole-editor interaction latency.

## Compatibility boundary

No new image-editing feature or pixel-identical Photoshop output is implied. The same maximum sigma, memory limits, CPU fallback, initial/final browser interop copies and document model remain in effect. Floating-point behavior may vary across drivers; a passed same-device differential test is not proof of universal bit identity.
