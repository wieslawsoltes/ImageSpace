namespace ImageSpace.Core;

/// <summary>
/// Immutable RGBA8 exposure settings. Decode sRGB, multiply by 2^Exposure, add Offset,
/// apply the reciprocal Gamma power, encode sRGB, then clamp/quantize to a byte.
/// This explicitly defined working-space algorithm is not an HDR or ICC pipeline.
/// </summary>
public sealed record ExposureAdjustment
{
    private static readonly double[] LinearInput = Enumerable.Range(0, 256)
        .Select(value => Decode(value / 255.0)).ToArray();
    public double Exposure { get; init; }
    public double Offset { get; init; }
    public double Gamma { get; init; } = 1;

    public void Validate()
    {
        if (!double.IsFinite(Exposure) || Exposure is < -20 or > 20 ||
            !double.IsFinite(Offset) || Offset is < -.5 or > .5 ||
            !double.IsFinite(Gamma) || Gamma is < .01 or > 9.99)
            throw new InvalidDataException("Exposure must be -20 to 20 EV, Offset -0.5 to 0.5 and Gamma 0.01 to 9.99.");
    }

    public byte[] CreateLookup()
    {
        Validate();
        var result = new byte[256];
        var gain = Math.Pow(2, Exposure);
        var exponent = 1 / Gamma;
        for (var i = 0; i < result.Length; i++)
            result[i] = Evaluate(LinearInput[i], gain, exponent);
        return result;
    }

    public Rgba32 Transform(Rgba32 source)
    {
        Validate();
        var gain = Math.Pow(2, Exposure);
        var exponent = 1 / Gamma;
        return new(Evaluate(LinearInput[source.R], gain, exponent),
            Evaluate(LinearInput[source.G], gain, exponent),
            Evaluate(LinearInput[source.B], gain, exponent), source.A);
    }

    private byte Evaluate(double linear, double gain, double exponent)
    {
        var value = Math.Max(0, linear * gain + Offset);
        if (exponent != 1) value = Math.Pow(value, exponent);
        if (value >= 1) return 255;
        return Rgba32.Byte(255 * Encode(value));
    }

    private static double Decode(double value) => value <= .04045 ? value / 12.92
        : Math.Pow((value + .055) / 1.055, 2.4);
    private static double Encode(double value) => value <= .0031308 ? 12.92 * value
        : 1.055 * Math.Pow(value, 1 / 2.4) - .055;
}
