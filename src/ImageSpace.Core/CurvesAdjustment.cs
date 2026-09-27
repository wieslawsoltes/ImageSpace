namespace ImageSpace.Core;

/// <summary>Independent RGB-channel curves followed by a composite RGB curve.</summary>
public sealed record CurvesAdjustment
{
    public ToneCurve Rgb { get; init; } = ToneCurve.Identity;
    public ToneCurve Red { get; init; } = ToneCurve.Identity;
    public ToneCurve Green { get; init; } = ToneCurve.Identity;
    public ToneCurve Blue { get; init; } = ToneCurve.Identity;

    public ToneCurve GetChannel(ToneChannel channel) => channel switch
    {
        ToneChannel.Rgb => Rgb,
        ToneChannel.Red => Red,
        ToneChannel.Green => Green,
        ToneChannel.Blue => Blue,
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };

    public CurvesAdjustment WithChannel(ToneChannel channel, ToneCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        curve.Validate();
        return channel switch
        {
            ToneChannel.Rgb => this with { Rgb = curve },
            ToneChannel.Red => this with { Red = curve },
            ToneChannel.Green => this with { Green = curve },
            ToneChannel.Blue => this with { Blue = curve },
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
    }

    public void Validate()
    {
        if (Rgb is null || Red is null || Green is null || Blue is null)
        {
            throw new InvalidDataException("All four curve channels are required.");
        }
        Rgb.Validate();
        Red.Validate();
        Green.Validate();
        Blue.Validate();
    }
}
