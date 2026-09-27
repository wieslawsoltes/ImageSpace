namespace ImageSpace.Core;

/// <summary>Input black/white, midpoint gamma and output endpoints for one channel.</summary>
public sealed record LevelsChannel
{
    public double InputBlack { get; init; }
    public double InputWhite { get; init; } = 255;
    public double Gamma { get; init; } = 1;
    public double OutputBlack { get; init; }
    public double OutputWhite { get; init; } = 255;

    public void Validate()
    {
        if (!double.IsFinite(InputBlack) || !double.IsFinite(InputWhite) || !double.IsFinite(Gamma) ||
            !double.IsFinite(OutputBlack) || !double.IsFinite(OutputWhite) ||
            InputBlack < 0 || InputWhite > 255 || InputWhite - InputBlack < 1 ||
            Gamma is < 0.1 or > 10 || OutputBlack is < 0 or > 255 || OutputWhite is < 0 or > 255)
        {
            throw new InvalidDataException("Invalid Levels endpoints or gamma. Input white must exceed black by at least one.");
        }
    }

    public byte[] CreateLookup()
    {
        Validate();
        var table = new byte[256];
        for (var i = 0; i < table.Length; i++)
        {
            var normalized = Math.Clamp((i - InputBlack) / (InputWhite - InputBlack), 0, 1);
            table[i] = Rgba32.Byte(OutputBlack + (OutputWhite - OutputBlack) * Math.Pow(normalized, 1 / Gamma));
        }
        return table;
    }
}
