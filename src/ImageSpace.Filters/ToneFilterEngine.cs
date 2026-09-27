using ImageSpace.Core;

namespace ImageSpace.Filters;

public static class ToneFilterEngine
{
    /// <summary>Applies a tone lookup without modifying source pixels or alpha coverage.</summary>
    public static PixelSurface Apply(PixelSurface source, RgbLookupTables tables, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(tables);
        var result = new PixelSurface(source.Width, source.Height);
        foreach (var (tileX, tileY, bytes, _) in source.EnumerateTiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var span = bytes.Span;
            for (var y = 0; y < Math.Min(PixelSurface.TileSize, source.Height - tileY); y++)
            {
                for (var x = 0; x < Math.Min(PixelSurface.TileSize, source.Width - tileX); x++)
                {
                    var offset = (y * PixelSurface.TileSize + x) * 4;
                    if (span[offset + 3] == 0)
                    {
                        continue;
                    }
                    result.Set(tileX + x, tileY + y, tables.Map(new Rgba32(
                        span[offset], span[offset + 1], span[offset + 2], span[offset + 3])));
                }
            }
        }
        return result;
    }
}
