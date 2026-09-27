namespace ImageSpace.Core;

/// <summary>Straight-alpha, 8-bit sRGB color. Pixel storage is RGBA, never native-endian BGRA.</summary>
public readonly record struct Rgba32(byte R, byte G, byte B, byte A = 255)
{
    public static Rgba32 Transparent => new(0, 0, 0, 0);
    public static Rgba32 Black => new(0, 0, 0);
    public static Rgba32 White => new(255, 255, 255);
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public Rgba32 WithAlpha(byte a) => new(R, G, B, a);
    public static byte Byte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    public static Rgba32 Parse(string text)
    {
        text = text.Trim().TrimStart('#');
        if (text.Length == 3) text = string.Concat(text.Select(c => new string(c, 2)));
        if (text.Length is not (6 or 8)) throw new FormatException("Use #RRGGBB or #RRGGBBAA.");
        return new(Convert.ToByte(text[..2], 16), Convert.ToByte(text[2..4], 16), Convert.ToByte(text[4..6], 16), text.Length == 8 ? Convert.ToByte(text[6..], 16) : (byte)255);
    }
    public static Rgba32 Lerp(Rgba32 a, Rgba32 b, double t) => new(Byte(a.R + (b.R-a.R)*t), Byte(a.G + (b.G-a.G)*t), Byte(a.B + (b.B-a.B)*t), Byte(a.A + (b.A-a.A)*t));
    public static Rgba32 Over(Rgba32 bottom, Rgba32 top, double opacity = 1)
    {
        var a = top.A / 255.0 * Math.Clamp(opacity, 0, 1); var b = bottom.A / 255.0;
        var result = a + b * (1-a);
        return result <= 0 ? Transparent : new(Byte((top.R*a + bottom.R*b*(1-a))/result), Byte((top.G*a + bottom.G*b*(1-a))/result), Byte((top.B*a + bottom.B*b*(1-a))/result), Byte(result*255));
    }
}
