# Adjustment-mask editing

## Status and scope

The alpha-correct mask compositor is connected to the shared desktop/browser editing controls. It does not claim full Photoshop parity or lossless PSD mask interchange.

`PixelTarget` selects the authoritative surface: raster pixels in content mode, or authored mask coverage on any supported layer kind in mask mode. No command falls back from a missing mask to the layer's pixel data. `MaskOperations` stores coverage in alpha and converts to/from opaque grayscale for CPU kernels. `PixelEdits` shares selection-aware fill, gradient, clear and filtered-output logic between the viewport and commands.

The mask inspector offers density, feather, whole-mask inversion, enable/disable, delete and view-only grayscale/red-overlay inspection. Pixel-selection-based Ctrl+I differs deliberately from the whole-mask Properties action. Locked layers cannot modify masks. Selecting a mask or changing inspection mode is not a content edit. All actual edits use the existing snapshot transaction/history model.

## Compositor contract

For source `S`, full-strength effect `F(S)`, opacity `o`, mask density `d`, and locally feathered mask `m`, the effective coverage is `c = o * (1 - d + d * m)`. The adjustment result is the premultiplied sum `c * F(S) + (1 - c) * S`. This is not two source-over draws: those would increase the alpha of translucent content. Opacity is applied after effect clamping, and blur is blended at its full selected radius.

The renderer implements that filter graph. The workbench uses `MaskPreviewRenderer`, which owns a separate bounded-lifetime graph cache while sharing tile-image uploads with `ImageRenderer`. Grayscale maps effective coverage into RGB with opaque display alpha. Overlay displays red at hidden coverage. Neither path is called by native or image exports. No viewport pixel readback is introduced by mask inspection.

Masks start linked to their layer transform. They can now be unlinked, transformed independently and relinked without changing appearance; see [independent mask placement](mask-placement.md). Creating a mask with no selection reveals all authored pixels, including off-canvas content. With a selection, coverage is sampled at transformed pixel centers. Negative coordinates are rejected before flooring; they cannot leak into document pixel zero.

## File compatibility

The native reader accepts versions 1, 2, 3 and 4. Independent placement and unlink metadata require version 4. Feature-free legacy files retain their minimal writer version. Adjustment masks, non-default density/feather, and fractional adjustment output crossfades require version 3. Readers that only support versions 1/2 reject these files rather than silently ignore their new semantics. Authored mask pixels and non-destructive parameters roundtrip separately.

Legacy version-1/2 files use the current compositor when opened. In particular, partial-opacity nonlinear adjustments now crossfade the clamped full effect; older parameter-interpolation rendering is not preserved as a separate legacy mode. Keep originals when exact historical pixel output is required.

PSD remains the documented raster-interchange subset. Adjustment-layer PSD export still uses a flattened compatibility composite. Preserve original PSD files; the new native features do not add Photoshop mask metadata roundtripping.

## Verification commands

```sh
dotnet run --project tests/ImageSpace.Tests -c Release
dotnet run --project tests/ImageSpace.RegressionTests -c Release
dotnet run --project tests/ImageSpace.MaskTests -c Release
dotnet run --project tests/ImageSpace.MaskEditingTests -c Release
python3 scripts/fetch-assets.py
dotnet publish src/ImageSpace.App -c Release -f net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/ImageSpace/ -o artifacts/publish
python3 scripts/collect-site.py artifacts/publish site
npm ci
npx playwright install chromium
npm run test:browser
```

The new console cases cover each layer kind, transformed/out-of-canvas coverage, empty selections, brush/clone/eraser behavior, filters, locking, history, cancellation, archive versions and inspection cache behavior. Two new browser scenarios use real pointer/keyboard input and native archive download/open, not document-mutating test hooks.

The older tonal regression expected opacity changes to rebuild the lookup table. It now checks that the full-strength table is reused while the rendered zero/half-opacity output remains correct. Build and Release run both mask test suites before browser publishing/package generation. These tests must be executed on a .NET 10/Uno-capable environment before merging; syntax checks alone do not establish runtime or GPU correctness.

## References

- Skia filter composition: https://api.skia.org/classSkImageFilters.html
- Public mask workflow reference: https://helpx.adobe.com/photoshop/using/masking-layers.html

The algorithms, icons and controls are independent implementations. Density/feather units and the linked/unlinked transform behavior are documented above; they are not a claim of byte-identical Adobe processing.
