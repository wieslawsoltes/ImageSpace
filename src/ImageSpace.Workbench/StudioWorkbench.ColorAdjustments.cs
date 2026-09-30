namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private const string ColorAdjustmentHelp = """
        CHANNEL MIXER

        Choose Image → Channel Mixer adjustment layer, or Channel Mixer from the
        adjustment-layer menu. Select an output channel and set its Red, Green,
        Blue and Constant contributions from -200% to +200%. Total reports the
        source sum; values above/below 100% can change brightness. Contributions
        are combined before clipping to the RGB/8 output range.

        Monochrome uses an independently retained gray mix. Turning it off restores
        the saved color-channel mixes. Presets include red/green/blue/yellow filters,
        an infrared-style monochrome mix and a red/blue channel swap. These are
        numeric recipes, not proprietary Adobe presets or a claim of exact parity.

        EXPOSURE

        Choose Image → Exposure adjustment layer. Exposure adjusts gain in stops;
        Offset adds a linear-light offset; Gamma applies a reciprocal power. This
        implementation decodes sRGB, evaluates the correction and encodes an 8-bit
        sRGB lookup. One stop doubles linear intensity, not the encoded RGB byte.
        It does not add HDR, 16/32-bit processing or ICC profile management.

        EDITING AND PERFORMANCE

        Drag a slider for a live preview. Releasing commits one history entry;
        Escape or lost capture restores the original settings. Numeric inputs and
        arrow keys support precise edits. Source pixels remain unchanged. Preview
        toggles visibility; Reset restores neutral parameters. Inspectors and controls
        are retained across selection and undo, and do not compute histograms.

        New adjustments are inserted above the active clipping unit. An active
        selection becomes an editable adjustment mask. Create Clipping Mask can
        then explicitly restrict the adjustment to the layer below. Existing density,
        feather, independent mask placement and layer opacity remain available.

        Cached Skia color matrices/lookups run inside the ordinary compositor.
        A GPU-backed host can evaluate them without a per-frame CPU image loop or
        readback; software fallback still exists. These are not new WebGPU compute
        kernels. Filter-build counters are not GPU timing or whole-editor latency.

        SAVE AND INTERCHANGE

        Native Save uses manifest version 6 when these settings are present. Older
        readers reject that version rather than silently discarding the correction.
        Versions 1–5 remain readable. PSD export uses the established flattened
        compatibility path for visible adjustments; editable Photoshop Channel Mixer
        and Exposure metadata are not exported or imported by this implementation.
        """;
}
