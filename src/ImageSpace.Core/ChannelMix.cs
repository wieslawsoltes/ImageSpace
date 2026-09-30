namespace ImageSpace.Core;

/// <summary>Source contributions and a normalized constant, expressed as percentages.</summary>
public sealed record ChannelMix(double Red = 100, double Green = 0, double Blue = 0, double Constant = 0)
{
    public double Total => Red + Green + Blue;

    public void Validate()
    {
        if (!Valid(Red) || !Valid(Green) || !Valid(Blue) || !Valid(Constant))
            throw new InvalidDataException("Channel Mixer contributions and constant must be finite percentages from -200 to 200.");
    }

    private static bool Valid(double value) => double.IsFinite(value) && value is >= -200 and <= 200;

    internal byte Evaluate(Rgba32 source) => Rgba32.Byte(
        (Red * source.R + Green * source.G + Blue * source.B + Constant * 255) / 100);
}
