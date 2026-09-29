# Clipping masks and quantization-preserving GPU fusion

## Layer workflow

Select a layer above a non-clipped raster, text or shape base, then use **Layer → Create Clipping Mask**, **Ctrl+Alt+G**, or Alt/Option-click its lower row boundary. Multiple consecutive clipped layers share the nearest preceding non-clipped content layer. Clipped thumbnails are indented and display an original bent-arrow icon; the base name is underlined. A clipped adjustment affects its chain prefix rather than unrelated lower layers.

Source pixels remain unchanged. The base's content alpha, geometric transform and enabled mask define the chain coverage. Each clipped layer retains its own opacity, mask, transform and one of the sixteen supported blend modes. The base opacity and blend apply once to the composed chain. Hiding the base hides all its clipped dependents. The application uses Photoshop's default **Blend Clipped Layers As Group** convention; disabling that advanced option is not implemented.

Release removes the selected clipping relationship and the clipped tail above it. Deleting a base explicitly releases its followers rather than reparenting them to unrelated content. A locked tail must be unlocked first. Reorder buttons move whole clipping units, not arbitrary Photoshop drag/drop reparenting. Duplicating a base clones its complete chain with new identities and independent copy-on-write ownership; duplicating a member inserts another clipped member. Merge Down rejects a partial chain; Flatten Image is the existing explicit whole-document operation. Each structural operation is transactional and supports undo/redo.

**File → Clipping mask study** opens original procedural artwork with an editable rounded base, aurora texture, Screen-blended sheen and clipped saturation adjustment. No Adobe artwork or private SDK is used.

## Renderer contract

Let `a` be the current group alpha, `b` its unpremultiplied color, and `s` an incoming premultiplied clipped layer including its own opacity/mask. The cached runtime blender evaluates the existing Skia blend mode against an opaque backdrop `b`, then multiplies the resulting color by `a` while restoring alpha to `a`. Normal uses the equivalent built-in SrcATop path. This avoids both squaring the base mask and growing translucent edges with repeated source-over passes.

The group is isolated once for the base opacity/blend. Clipped layers are isolated as needed for combined content/mask/opacity, not blended primitive-by-primitive. Adjustment color is evaluated with the existing filter graph and its prefix alpha is restored, including for spatial blur. Clipping never expands the base alpha. Ordinary unclipped documents retain the prior direct-layer fast path.

Blenders are lazily cached by blend mode and owned by the renderer; warm draws do not recompile them. The implementation introduces no CPU rasterization/readback in the clipping draw path. It uses the caller's Skia backend, which can be GPU or software; this is not proof that a particular desktop host has hardware acceleration enabled. Canvas save state is restored on success and exceptions. Work counters report clipping group draws and blender builds, not GPU timings.

Selection hit testing uses authored raster alpha and analytic shape bounds, transformed base coordinates and unfeathered mask coverage without rendering pixels. Editable text uses its bounds. Feathered-mask and antialiased/stroked boundaries remain conservative approximations; this is not exact rendered-pixel picking. Target selection still uses the retained inspectors and overlay-only invalidation path.

## Model, storage and interoperability

`Layer.IsClipped` is a relationship to a preceding content base, not an owned mask surface. `LayerClipping` provides allocation-free base/end/membership queries and validation. A bottommost clipped layer or an adjustment used as a base is rejected rather than rendered unrestricted. Clipped adjustments above valid content bases are supported.

The native writer uses manifest **version 5** only when a clipping relationship is present. It retains versions 1–4 otherwise. The reader accepts versions 1–5 and rejects clipping metadata falsely declared as legacy. Rendering stamps include the clipping relationship so cached document previews invalidate correctly; selection/name changes still do not invalidate content.

RGB/8 PSD layer records preserve the standard byte: 0 for a base/unclipped layer and 1 for a clipped layer. Other values are rejected. The default grouped blending is retained. A `clbl` block requesting the unsupported ungrouped blend behavior produces an explicit warning. Imports whose omitted/unsupported records leave an orphan clip fail validation instead of silently exposing its pixels. PSD export rasterizes authored content/transform/masks but does not pre-apply clipping, allowing the relationship to survive. Live clipped adjustment semantics cannot be preserved by the raster-only PSD writer; the workbench uses its existing flattened compatibility export when adjustments are visible. Preserve the original PSD and use native saves for editable application state.

## GPU color-stage fusion

The browser engine adds a fifth reusable pipeline for adjacent color operations. A run of up to sixteen color stages executes one load, stage-local pack/decode operations, then one final store. It preserves each logical operation's RGBA8 quantization, including normalization of transparent color-only pixels. Spatial operations form explicit boundaries; Gaussian still has its premultiplied float intermediate. No unsupported operations are secretly replaced.

`createSession(bytes, width, height, { fuseColorOperations: false })` selects the unfused reference mode. The default fuses eligible runs. Source and ping-pong buffers, bind groups and both CPU/GPU parameter arenas are retained across evaluations. An eight-color chain therefore has one physical dispatch rather than eight. This is a work-count statement, not an eight-times speed claim.

`statistics()` now includes `logicalOperations`, `fusedPasses` and `parameterBytesUploaded`. Existing transfer, buffer, binding and readback counters remain. Every preview still begins from the immutable original; `execute` does not read output; `read` or `apply` returns a detached final RGBA array. Allocation budgets, serialized device error scopes, fallback and device-loss contracts are unchanged. GPU residency is not a zero-copy .NET/browser bridge or a GPU-only authoritative document.

## Validation and measurement

Run `dotnet run --project tests/ImageSpace.ClippingTests -c Release`, the existing engine suites, `npx playwright test --config playwright.gpu.config.mjs`, then the normal Uno browser tests after publishing/collecting the site.

The clipping suite covers all sixteen blend modes across translucent bases and source opacity, one-time base blend/opacity, holes, masks, adjustment scope, shape/type/affine coverage, warm caches, save-stack restoration, transactions, locks, chain-safe mutations, native versioning and four PSD compression modes. Browser scenarios use actual menus, keyboard, canvas picking and file downloads/uploads, with visible-pixel and retained-UI counters.

The fusion probe covers 68 deterministic recipes: all ordered pairs of the eight color filters, a sixteen-stage chain and mixed spatial/color runs. It compares actual fused/unfused GPU bytes and validates immutable reset and warmed resource counts. The eight-stage A/B fixture uses two warmups and seven alternating same-device samples. Timing includes API completion and final readback. Software adapters validate behavior, not physical-device performance or input-to-photon latency. CI reports record actual outcomes; absent reports are not successful checks.

## Remaining scope

Folder groups and arbitrary hierarchy reparenting, alternative clipping blend-group settings, knockout/layer-style interactions, smart objects and persisted Smart Filters, high-bit/CMYK/ICC workflows, advanced typography/paths/warps/healing, PSB, plug-ins/actions/timeline and full Photoshop pixel/UI parity remain separate work. The component APIs and format claims above are the implemented contract.

## Public references

- Adobe clipping-mask workflow: https://helpx.adobe.com/ca/photoshop/using/revealing-layers-clipping-masks.html
- Adobe grouped blend options: https://helpx.adobe.com/ph_fil/photoshop/using/layer-opacity-blending.html
- Adobe PSD layer-record specification: https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/
- Skia runtime effects and premultiplied color: https://skia.org/docs/user/sksl/
- WGSL buffer layout and shader semantics: https://www.w3.org/TR/WGSL/
