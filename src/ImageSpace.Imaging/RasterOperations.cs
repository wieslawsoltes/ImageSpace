using System.Buffers;
using System.Runtime.InteropServices;
using ImageSpace.Core;

namespace ImageSpace.Imaging;

public static class RasterOperations
{
    public static PixelSurface Resize(PixelSurface source, int width, int height, bool nearest = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new PixelSurface(width, height);
        var topBuffer = ArrayPool<byte>.Shared.Rent(source.Width * 4);
        var bottomBuffer = ArrayPool<byte>.Shared.Rent(source.Width * 4);
        var outputBuffer = ArrayPool<byte>.Shared.Rent(width * 4);
        try
        {
            var top = topBuffer.AsSpan(0, source.Width * 4);
            var bottom = bottomBuffer.AsSpan(0, source.Width * 4);
            var output = outputBuffer.AsSpan(0, width * 4);
            var left = new int[width];
            var right = new int[width];
            var fraction = new double[width];
            for (var x = 0; x < width; x++)
            {
                var sx = Math.Clamp((x + 0.5) * source.Width / width - 0.5, 0, source.Width - 1);
                left[x] = nearest ? (int)Math.Round(sx) * 4 : (int)sx * 4;
                right[x] = Math.Min(left[x] + 4, (source.Width - 1) * 4);
                fraction[x] = sx - (int)sx;
            }
            var topIndex = -1;
            var bottomIndex = -1;
            for (var y = 0; y < height; y++)
            {
                var sy = Math.Clamp((y + 0.5) * source.Height / height - 0.5, 0, source.Height - 1);
                var y0 = nearest ? (int)Math.Round(sy) : (int)sy;
                var y1 = Math.Min(y0 + 1, source.Height - 1);
                if (topIndex != y0)
                {
                    source.CopyRowTo(0, y0, top);
                    topIndex = y0;
                }
                if (!nearest && bottomIndex != y1)
                {
                    source.CopyRowTo(0, y1, bottom);
                    bottomIndex = y1;
                }
                for (var x = 0; x < width; x++)
                {
                    var color = nearest ? Read(top, left[x]) : Bilinear(
                        Read(top, left[x]), Read(top, right[x]), Read(bottom, left[x]), Read(bottom, right[x]),
                        fraction[x], sy - y0);
                    Write(output, x * 4, color);
                }
                result.WriteRow(0, y, output);
            }
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(topBuffer, clearArray: true);
            ArrayPool<byte>.Shared.Return(bottomBuffer, clearArray: true);
            ArrayPool<byte>.Shared.Return(outputBuffer, clearArray: true);
        }
    }

    /// <summary>Allocation-free bilinear interpolation in premultiplied color, returned as straight RGBA.</summary>
    public static Rgba32 Sample(PixelSurface source, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(x));
        x = Math.Clamp(x, 0, source.Width - 1);
        y = Math.Clamp(y, 0, source.Height - 1);
        var x0 = (int)x;
        var y0 = (int)y;
        var x1 = Math.Min(x0 + 1, source.Width - 1);
        var y1 = Math.Min(y0 + 1, source.Height - 1);
        return Bilinear(source.Get(x0, y0), source.Get(x1, y0), source.Get(x0, y1), source.Get(x1, y1), x - x0, y - y0);
    }

    private static Rgba32 Bilinear(Rgba32 topLeft, Rgba32 topRight, Rgba32 bottomLeft, Rgba32 bottomRight, double x, double y)
    {
        double red = 0, green = 0, blue = 0, alpha = 0;
        Accumulate(topLeft, (1 - x) * (1 - y));
        Accumulate(topRight, x * (1 - y));
        Accumulate(bottomLeft, (1 - x) * y);
        Accumulate(bottomRight, x * y);
        return alpha <= 0 ? Rgba32.Transparent : new(
            Rgba32.Byte(red / alpha), Rgba32.Byte(green / alpha), Rgba32.Byte(blue / alpha), Rgba32.Byte(alpha * 255));

        void Accumulate(Rgba32 color, double weight)
        {
            var a = color.A / 255.0 * weight;
            red += color.R * a;
            green += color.G * a;
            blue += color.B * a;
            alpha += a;
        }
    }

    private static Rgba32 Read(ReadOnlySpan<byte> row, int offset) => new(row[offset], row[offset + 1], row[offset + 2], row[offset + 3]);
    private static void Write(Span<byte> row, int offset, Rgba32 color)
    {
        if (color.A == 0)
        {
            row.Slice(offset, 4).Clear();
            return;
        }
        row[offset] = color.R;
        row[offset + 1] = color.G;
        row[offset + 2] = color.B;
        row[offset + 3] = color.A;
    }

    public static PixelSurface Crop(PixelSurface source, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new PixelSurface(width, height);
        var left = Math.Max(0L, x);
        var top = Math.Max(0L, y);
        var right = Math.Min(source.Width, (long)x + width);
        var bottom = Math.Min(source.Height, (long)y + height);
        if (left >= right || top >= bottom)
            return result;
        var buffer = ArrayPool<byte>.Shared.Rent((int)(right - left) * 4);
        try
        {
            var row = buffer.AsSpan(0, (int)(right - left) * 4);
            for (var sourceY = (int)top; sourceY < bottom; sourceY++)
            {
                source.CopyRowTo((int)left, sourceY, row);
                result.WriteRow((int)(left - x), sourceY - y, row);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
        return result;
    }

    public static PixelSurface Rotate90(PixelSurface source, bool clockwise = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new PixelSurface(source.Height, source.Width);
        foreach (var (tx, ty, bytes, _) in source.EnumerateTiles())
            for (var y = 0; y < Math.Min(PixelSurface.TileSize, source.Height - ty); y++)
                for (var x = 0; x < Math.Min(PixelSurface.TileSize, source.Width - tx); x++)
                {
                    var color = Read(bytes.Span, (y * PixelSurface.TileSize + x) * 4);
                    if (color.A == 0)
                        continue;
                    result.Set(clockwise ? source.Height - 1 - ty - y : ty + y,
                        clockwise ? tx + x : source.Width - 1 - tx - x, color);
                }
        return result;
    }

    public static PixelSurface Flip(PixelSurface source, bool horizontal)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new PixelSurface(source.Width, source.Height);
        var buffer = ArrayPool<byte>.Shared.Rent(source.Width * 4);
        try
        {
            var row = buffer.AsSpan(0, source.Width * 4);
            for (var y = 0; y < source.Height; y++)
            {
                source.CopyRowTo(0, horizontal ? y : source.Height - 1 - y, row);
                if (horizontal)
                    MemoryMarshal.Cast<byte, uint>(row).Reverse();
                result.WriteRow(0, y, row);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
        return result;
    }

    public static int[] Histogram(PixelSurface source, int channel = -1)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new int[256];
        foreach (var (tx, ty, pixels, _) in source.EnumerateTiles())
        {
            var bytes = pixels.Span;
            for (var y = 0; y < Math.Min(PixelSurface.TileSize, source.Height - ty); y++)
                for (var x = 0; x < Math.Min(PixelSurface.TileSize, source.Width - tx); x++)
                {
                    var i = (y * PixelSurface.TileSize + x) * 4;
                    if (bytes[i + 3] == 0)
                        continue;
                    var value = channel is >= 0 and <= 2 ? bytes[i + channel]
                        : Rgba32.Byte(bytes[i] * .2126 + bytes[i + 1] * .7152 + bytes[i + 2] * .0722);
                    result[value]++;
                }
        }
        return result;
    }

    public static void Fill(ImageDocument document, Layer layer, Rgba32 color, PixelSurface? region = null)
    {
        if (layer.Locked || PixelTarget.Get(document, layer) is null)
            return;
        PixelEdits.Fill(document, layer, color, region);
    }
}
