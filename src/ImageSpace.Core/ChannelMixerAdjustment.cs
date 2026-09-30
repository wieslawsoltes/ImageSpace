namespace ImageSpace.Core;

/// <summary>Immutable RGB/monochrome channel mixing. Alpha is never a source or output channel.</summary>
public sealed record ChannelMixerAdjustment
{
    public ChannelMix Red { get; init; } = new(100, 0, 0);
    public ChannelMix Green { get; init; } = new(0, 100, 0);
    public ChannelMix Blue { get; init; } = new(0, 0, 100);
    public ChannelMix Gray { get; init; } = new(40, 40, 20);
    public bool Monochrome
    {
        get; init;
    }

    public void Validate()
    {
        if (Red is null || Green is null || Blue is null || Gray is null)
            throw new InvalidDataException("Channel Mixer requires all color and monochrome channel settings.");
        Red.Validate();
        Green.Validate();
        Blue.Validate();
        Gray.Validate();
    }

    public ChannelMix GetChannel(ToneChannel channel) => channel switch
    {
        ToneChannel.Red => Red,
        ToneChannel.Green => Green,
        ToneChannel.Blue => Blue,
        ToneChannel.Rgb => Gray,
        _ => throw new ArgumentOutOfRangeException(nameof(channel))
    };

    public ChannelMixerAdjustment WithChannel(ToneChannel channel, ChannelMix value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        return channel switch
        {
            ToneChannel.Red => this with { Red = value },
            ToneChannel.Green => this with { Green = value },
            ToneChannel.Blue => this with { Blue = value },
            ToneChannel.Rgb => this with { Gray = value },
            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
    }

    /// <summary>Returns an independently owned row-major 4x5 matrix for normalized, straight RGBA.</summary>
    public float[] CreateMatrix()
    {
        Validate();
        var r = Monochrome ? Gray : Red;
        var g = Monochrome ? Gray : Green;
        var b = Monochrome ? Gray : Blue;
        return [
            (float)(r.Red / 100), (float)(r.Green / 100), (float)(r.Blue / 100), 0, (float)(r.Constant / 100),
            (float)(g.Red / 100), (float)(g.Green / 100), (float)(g.Blue / 100), 0, (float)(g.Constant / 100),
            (float)(b.Red / 100), (float)(b.Green / 100), (float)(b.Blue / 100), 0, (float)(b.Constant / 100),
            0, 0, 0, 1, 0];
    }

    /// <summary>Scalar RGBA8 reference. Clamps only after combining all source contributions.</summary>
    public Rgba32 Transform(Rgba32 source)
    {
        Validate();
        if (Monochrome)
        {
            var gray = Gray.Evaluate(source);
            return new(gray, gray, gray, source.A);
        }
        return new(Red.Evaluate(source), Green.Evaluate(source), Blue.Evaluate(source), source.A);
    }
}
