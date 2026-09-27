namespace ImageSpace.Core;

/// <summary>Validated RGB byte lookups; alpha is intentionally left unchanged.</summary>
public sealed class RgbLookupTables
{
    private readonly byte[] _red;
    private readonly byte[] _green;
    private readonly byte[] _blue;
    public ReadOnlyMemory<byte> Red => _red;
    public ReadOnlyMemory<byte> Green => _green;
    public ReadOnlyMemory<byte> Blue => _blue;

    public RgbLookupTables(ReadOnlySpan<byte> red, ReadOnlySpan<byte> green, ReadOnlySpan<byte> blue)
    {
        if (red.Length != 256 || green.Length != 256 || blue.Length != 256)
        {
            throw new ArgumentException("Every tone table must contain exactly 256 entries.");
        }
        _red = red.ToArray();
        _green = green.ToArray();
        _blue = blue.ToArray();
    }

    public Rgba32 Map(Rgba32 color) => color.A == 0 ? Rgba32.Transparent :
        new Rgba32(_red[color.R], _green[color.G], _blue[color.B], color.A);

    public static RgbLookupTables FromCurves(CurvesAdjustment adjustment, float strength = 1)
    {
        adjustment.Validate();
        return Compose(adjustment.Rgb.CreateLookup(), adjustment.Red.CreateLookup(),
            adjustment.Green.CreateLookup(), adjustment.Blue.CreateLookup(), strength);
    }

    public static RgbLookupTables FromLevels(LevelsAdjustment adjustment, float strength = 1)
    {
        adjustment.Validate();
        return Compose(adjustment.Rgb.CreateLookup(), adjustment.Red.CreateLookup(),
            adjustment.Green.CreateLookup(), adjustment.Blue.CreateLookup(), strength);
    }

    private static RgbLookupTables Compose(byte[] master, byte[] red, byte[] green, byte[] blue, float strength)
    {
        if (!float.IsFinite(strength) || strength is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(strength));
        }
        for (var i = 0; i < 256; i++)
        {
            red[i] = Rgba32.Byte(i + (master[red[i]] - i) * strength);
            green[i] = Rgba32.Byte(i + (master[green[i]] - i) * strength);
            blue[i] = Rgba32.Byte(i + (master[blue[i]] - i) * strength);
        }
        return new RgbLookupTables(red, green, blue);
    }
}
