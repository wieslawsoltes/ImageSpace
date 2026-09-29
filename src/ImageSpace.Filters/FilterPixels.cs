using ImageSpace.Core;

namespace ImageSpace.Filters;

/// <summary>Decodes exact filter output, including hidden RGB needed by subsequent spatial stages.</summary>
public static class FilterPixels
{
    public static PixelSurface FromRgba(int width, int height, ReadOnlySpan<byte> bytes)
    {
        PixelSurface.ValidateSize(width, height);
        if (bytes.Length != checked(width * height * 4)) throw new ArgumentException("Invalid filter result dimensions.", nameof(bytes));
        var output = new PixelSurface(width, height);
        for (var y = 0; y < height; y++) output.WriteRow(0, y, bytes.Slice(y * width * 4, width * 4));
        return output;
    }
}
