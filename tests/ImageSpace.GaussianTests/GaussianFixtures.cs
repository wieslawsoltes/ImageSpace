using ImageSpace.Core;

internal static class GaussianFixtures
{
    public static PixelSurface Create(int width, int height, string pattern)
    {
        var source = new PixelSurface(width, height);
        if (pattern == "sparse")
        {
            source.Set(0, 0, new Rgba32(191, 17, 47, 1));
            source.Set(width / 2, height / 2, new Rgba32(17, 131, 239, 127));
            source.Set(width - 1, height - 1, new Rgba32(251, 211, 71, 255));
            return source;
        }
        byte[] coverage = [0, 1, 2, 63, 127, 192, 254, 255];
        var row = new byte[width * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var at = x * 4;
                row[at] = (byte)((x * 71 + y * 17) % 256);
                row[at + 1] = (byte)((x * 23 + y * 89) % 256);
                row[at + 2] = (byte)((x * 113 + y * 11) % 256);
                row[at + 3] = pattern == "hidden" ? (byte)0 : coverage[(x + y * 3) % coverage.Length];
            }
            source.WriteRow(0, y, row);
        }
        return source;
    }
}
