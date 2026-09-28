# Curves and Levels

ImageSpace provides non-destructive **Curves** and **Levels** layers with independent composite RGB, red, green and blue controls. Choose **Image → Curves adjustment layer** or **Image → Levels adjustment layer**, or use the Adjustments menu. The new layer is inserted immediately above the selected layer and affects the visible layers below it. Content above it is unchanged.

These are independent ImageSpace implementations. They reproduce useful tonal-editing workflows, not Photoshop's private interpolation algorithms or PSD adjustment metadata.

## Curves

The horizontal axis represents the input intensity, and the vertical axis represents the output intensity, both in the 0–255 domain. The diagonal is the identity mapping. Click the graph to add an interior point, then drag it. A channel supports up to sixteen points including its two endpoints. Inputs stay strictly ordered; points cannot cross their neighbors.

Choose RGB, Red, Green or Blue above the graph. The channel selection persists while editing and undoing. The graph displays a sampled histogram of the composite below the adjustment, so it does not feed its own output back into the histogram.

The selected point can also be edited through Input and Output fields. Arrow keys move it one unit; Shift+Arrow moves it ten. Delete removes an interior point, but cannot remove the endpoints. Escape cancels an in-progress pointer gesture and restores the exact prior settings. A complete drag is one history entry rather than an entry for every pointer event.

Presets include Linear, Strong contrast, Lift shadows, Fade blacks and Negative. Presets affect the currently selected channel. Reset channel restores its identity mapping without discarding other channel edits. The Preview button toggles the adjustment's visibility; layer opacity controls effect strength.

### Interpolation and composition

`ToneCurve.CreateLookup()` evaluates an independent shape-preserving cubic Hermite interpolation. Interior slopes use weighted harmonic means of adjacent secants. At a slope sign change, the tangent becomes zero. One-sided endpoint slopes are limited, and each segment is bounded by its endpoint outputs. This avoids interpolation overshoot without requiring the whole curve to be monotonic; negative and intentionally nonmonotonic curves remain supported.

Each channel becomes a 256-entry lookup. Channel-specific lookup is applied first, followed by the composite RGB lookup. The renderer applies the full-strength lookup, then combines its result with the original through opacity and optional mask coverage. Alpha is preserved independently; mask compositing uses a complementary premultiplied sum rather than two source-over draws.

## Levels

The upper histogram handles set input black, midpoint gamma and input white. Dragging the black and white handles remaps the input range. Dragging the midpoint changes gamma without moving the endpoints. The lower gradient has two handles for output black and output white. Reversed output endpoints are deliberately supported.

Numeric fields expose all five parameters. Input white must exceed input black by at least one intensity unit. Gamma is bounded to 0.1–10. Choose Red, Green or Blue to adjust a single component, or RGB for the composite mapping. Up/Down while the graph has focus selects a handle; Left/Right nudges it. Escape cancels a pointer edit.

For a channel, the mapping is:

```text
normalized = clamp((input - inputBlack) / (inputWhite - inputBlack), 0, 1)
output = outputBlack + (outputWhite - outputBlack) * normalized^(1 / gamma)
```

The midpoint handle lies at `inputBlack + (inputWhite - inputBlack) * 0.5^gamma`, so dragging it left lightens midtones. Presets include contrast, lightening, darkening and output fading. Reset channel and Reset all levels are separate commands.

## Reuse from C#

The math and settings are in `ImageSpace.Core`, with no Uno or Skia dependency:

```csharp
using ImageSpace.Core;
using ImageSpace.Editing;

session.Execute("Add Curves", document =>
{
    var adjustment = new Layer
    {
        Name = "Contrast",
        Kind = LayerKind.Adjustment,
        Adjustment = AdjustmentKind.Curves,
        Width = document.Width,
        Height = document.Height,
        Curves = new CurvesAdjustment
        {
            Rgb = new ToneCurve
            {
                Points = [new(0, 0), new(64, 38), new(192, 218), new(255, 255)]
            }
        }
    };
    document.Layers.Add(adjustment);
    document.ActiveLayerId = adjustment.Id;
});
```

A headless pixel operation is also available:

```csharp
using ImageSpace.Core;
using ImageSpace.Filters;

var settings = new LevelsAdjustment
{
    Rgb = new LevelsChannel { InputBlack = 16, InputWhite = 240, Gamma = 1.2 },
    Blue = new LevelsChannel { Gamma = 0.95 }
};
var lookup = RgbLookupTables.FromLevels(settings);
var result = ToneFilterEngine.Apply(source, lookup, cancellationToken);
```

The standalone `CurveEditor` and `LevelsControl` in `ImageSpace.Controls` expose begin/change/complete/cancel events and immutable values. `ToneAdjustmentEditor` in Workbench connects those events to `EditorSession` transactions. Consumers can embed the controls without the application shell.

## Rendering and ownership

Tone settings use immutable records; curve points use `ImmutableArray<CurvePoint>`. Undo snapshots can share them without aliasing later edits. The renderer caches the full-strength `SKColorFilter` by layer ID and settings identity. Opacity and mask coverage are applied after filtering, so changing opacity reuses the lookup table. It rebuilds a table when its settings change, not on every unchanged frame.

Skia executes these lookup filters on the graphics backend used by the host. This extends the shared desktop/browser compositor; it does not add a separate WebGPU dispatch/readback per pointer event. The standalone WebGPU module still supports its eight existing destructive color kernels. Software rendering remains possible when selected by the host, and CI software-adapter results do not establish physical-GPU performance.

Histograms in the tone inspector are explicitly sampled from a preview with a maximum edge of 192 pixels. They are editing aids, not exact full-resolution statistical reports. The Channels panel's existing full composite histogram remains separate.

## Files and compatibility

Tone-only documents use manifest **version 2**, retaining all channel settings and curve points. Adjustment masks, non-default mask density/feather and fractional adjustment output crossfades require **version 3**. The reader accepts versions 1–3 and supplies identity tone settings for older files; files without newer features retain their minimal writer version. Older readers reject unsupported versions rather than silently dropping their appearance.

PSD adjustment compatibility is not implemented. PSD export with visible adjustment layers produces the existing flattened compatibility image; save `.imagespace` to retain editability. Curves/Levels currently operate in the RGB8 workflow and do not provide CMYK, Lab, HDR, ICC soft proofing, clipping groups, black/white eyedroppers or automatic color correction.

Adjustment masks, density/feather, mask painting and view-only grayscale/overlay inspection are described in [mask editing](mask-editing.md). Masks are linked to the layer transform; groups and independently transformed masks remain outside this implementation.

## Regression coverage

Tests cover all-byte identity/negative mappings, interpolation through knots, seeded monotonicity checks, local extrema, invalid inputs, alpha preservation, sparse pixel processing, immutable snapshots, undo/redo, archive v1/v2 roundtrips, malformed archives, Skia-versus-CPU ramp output, cache invalidation and layer ordering. Browser acceptance drives real graph pointers, keyboard edits, cancellation, preview toggles and native file downloads/reopens; model-only tests are not used as evidence of working interaction.
