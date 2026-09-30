# Channel Mixer and Exposure

Two editable color adjustments share ImageSpace's retained inspector, transaction system and Skia compositor. They do not alter authored layer pixels. Their settings survive native save/reopen, duplication and undo/redo.

## Channel Mixer

Choose **Image → Channel Mixer adjustment layer**, or **Channel Mixer** in the adjustment-layer menu. Choose Red, Green or Blue as the output, then edit the contributions of the three input channels and a constant. Each accepts -200% to +200%. The Total label sums the three source contributions, excluding the constant, and indicates when the sum differs from 100%.

The normalized straight-color calculation for one output channel is:

```text
out = clamp(redPercent * R / 100
          + greenPercent * G / 100
          + bluePercent * B / 100
          + constantPercent / 100, 0, 1)
```

Contributions are combined before output clipping. Alpha is preserved and cannot be used as a source contribution. A negative contribution is signed channel subtraction, not a separately clipped inversion. RGB8 quantization occurs after the complete mix.

Monochrome uses its own Gray coefficients, initially 40/40/20. All three output channels receive that result. Returning to color restores the independently retained RGB coefficients; it does not copy the gray mix into each color row. The presets provide identity, red/blue swap, and monochrome red, green, blue, yellow and infrared-style recipes. These are explicit numeric recipes, not imported Adobe preset files. Preset changes and Reset are ordinary undoable edits.

Public Photoshop behavior reference: [Channel Mixer controls and monochrome recipes](https://helpx.adobe.com/au/photoshop/using/color-monochrome-adjustments-using-channels.html). ImageSpace does not claim support for Photoshop preset files, CMYK mixing or identical toggle/preset semantics in every Adobe version.

## Exposure

Choose **Image → Exposure adjustment layer** or **Exposure** in the adjustment menu. The controls are Exposure (-20 to +20 stops), Offset (-0.5 to +0.5), and Gamma (0.01 to 9.99). Each is independent: editing Exposure cannot reset Gamma or Offset.

ImageSpace defines its RGB8 working-space operation explicitly:

```text
linear    = decode_sRGB(inputByte / 255)
corrected = max(0, linear * 2^Exposure + Offset)^(1 / Gamma)
output    = round_even(255 * clamp(encode_sRGB(corrected), 0, 1))
```

The usual piecewise sRGB transfer is used. Decoding uses `c/12.92` below or at 0.04045 and `((c+0.055)/1.055)^2.4` otherwise. Encoding uses `12.92*c` below or at 0.0031308 and `1.055*c^(1/2.4)-0.055` otherwise. Negative values clip to zero because the working model stores RGB8, not signed HDR samples. One stop therefore doubles linear intensity rather than doubling an encoded sRGB byte: neutral byte 128 becomes approximately 175–176, not 255.

A 256-entry lookup captures this transfer when the settings change; it is shared by the three color channels with a separate identity-alpha table. Identity settings preserve all 256 byte values. This is not an HDR/ICC conversion pipeline and does not introduce 16/32-bit layers. Adobe describes Exposure as a linear-space correction, but does not specify every byte-level implementation detail; the ImageSpace formula is an independent, testable contract rather than a claim of identical Photoshop output.

References: [Adobe Exposure behavior](https://helpx.adobe.com/in/photoshop/using/adjusting-hdr-exposure-toning.html), [Adobe parameter ranges](https://developer.adobe.com/firefly-services/docs/photoshop/guides/photoshop-v2/v1-to-v2/layer-operations-adjustments), [W3C sRGB conversion examples](https://www.w3.org/TR/css-color-4/#color-conversion-code).

## Interaction and transactions

Each inspector is constructed once per schema and reused across selection, document changes and restored undo objects. Each parameter consists of a retained numeric input and original Skia-drawn slider. No histogram or full-resolution preview readback runs during inspector selection.

Pointer press opens one transaction. Pointer movement changes only the immutable settings and invalidates rendering; release creates one history state. Escape, canceled capture or unloading rolls back the settings. Arrow keys step by the configured increment; Shift uses ten increments. Home/End use the parameter's limits. The slider consumes those keys so they cannot also move a layer. Numeric entries are individual transactions. A gesture returning to its original value records no history entry.

`EditorSession.ColorAdjustmentGesture` owns the exact pre-edit snapshot, not just a Boolean transaction flag. A delayed pointer callback after Undo or a document change cannot commit/cancel a replacement gesture. Callers must dispose uncommitted gestures. Normal single-writer session discipline still applies.

New Channel Mixer/Exposure layers are inserted **above the entire active clipping unit**, not between existing members. An existing selection becomes an editable mask without changing the selection itself. The new layer starts unclipped; use Create Clipping Mask explicitly to restrict it. The older generic `AddAdjustment` insertion API retains its existing in-chain behavior.

## Rendering and invalidation

Channel Mixer uses a normalized 4x5 `SKColorFilter` matrix. Exposure uses a cached Skia lookup. Both pass through the existing adjustment compositor, which applies the complete clamped effect first and then crossfades using layer opacity and effective mask coverage. Clipped adjustments retain the base alpha and obey existing clipping-group behavior. Density, feather and independent mask placement use the existing graph pipeline.

The renderer caches by layer ID, adjustment kind and immutable settings value. Equivalent records reuse the native filter. Geometry, names, channel-selection UI, opacity or mask edits do not rebuild the color matrix/LUT; deleting/rasterizing a layer prunes its resource. `LayerRenderStamp` includes new settings so sampled previews above these effects invalidate correctly.

A GPU-backed Skia host can evaluate these filters as part of its compositor without a per-frame CPU pixel loop or image readback. Skia may also run in software, and raster export/tests use CPU surfaces. No Metal/Vulkan/D3D performance or whole-editor frame-rate claim follows from compile success or software-adapter results. The resident JavaScript WebGPU kernel catalogue is unchanged.

`ImageRenderer.ColorAdjustmentFilterBuilds` counts cumulative matrix/LUT construction. `CachedColorAdjustments` reports live entries. These are work counters, not timings, and include work performed by that particular renderer only.

## Reusable APIs

```csharp
using ImageSpace.Core;
using ImageSpace.Editing;

var session = new EditorSession(document);
var layer = session.AddColorAdjustment(AdjustmentKind.ChannelMixer);
session.SetChannelMixer(new ChannelMixerAdjustment
{
    Red = new ChannelMix(0, 0, 100),
    Blue = new ChannelMix(100, 0, 0)
});

session.AddColorAdjustment(AdjustmentKind.Exposure);
session.SetExposure(new ExposureAdjustment { Exposure = 1, Offset = 0.01, Gamma = 1.1 });

// A complete interactive drag. Invalidate the owning viewport after each Set.
using (var gesture = session.BeginColorAdjustmentEdit())
{
    gesture.Set(new ExposureAdjustment { Exposure = 0.5 });
    gesture.Set(new ExposureAdjustment { Exposure = 0.75 });
    gesture.Commit();
}
```

Core settings have no Uno or Skia dependency. `ChannelMixerAdjustment.Transform` and `ExposureAdjustment.Transform` provide scalar RGBA8 references. `CreateMatrix` and `CreateLookup` return independent arrays; callers cannot mutate cached settings through those arrays. `ColorAdjustmentEditor` and `AdjustmentParameter` are reusable Uno controls. Settings and document validation reject missing/nonfinite/out-of-range metadata before it reaches rendering.

## Persistence and compatibility

Native archive **version 6** is required for either new adjustment kind or non-default authored settings, including currently inactive settings. Older archives without those properties receive neutral defaults. Existing documents without new metadata retain minimal versions 1–5. A downgraded manifest cannot carry v6 metadata silently; the reader rejects it. Old readers reject v6 rather than opening a visually different document.

PSD continues to use the established compatibility export when visible adjustments are present. These features do not add editable Photoshop Channel Mixer/Exposure records to PSD import/export. Keep an editable `.imagespace` file and preserve imported originals.

## Validation

```sh
dotnet run --project tests/ImageSpace.ColorAdjustmentTests -c Release
npm run test:browser
```

The headless suite covers scalar/matrix/LUT agreement, extremes, negative coefficients, linear-light behavior, monotonic tables, identity alpha, masks, opacity, clipping, filter/preview invalidation, native versions, immutable ownership, locks and stale gestures. Existing engine suites continue to run. Four real Uno browser scenarios use pointer/keyboard/menu input and screenshot pixels for preset mixing, monochrome/color toggles, Exposure live preview/cancellation, selected masks/clipping, save/reopen and warm retained-inspector counters.

Reports are retained with the normal engine and browser artifacts. Passing tests establish the tested implementation behavior, not full Photoshop compatibility, universal bit identity across GPU drivers or physical-device performance.
