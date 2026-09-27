using ImageSpace.Core;
namespace ImageSpace.Imaging;

public static class RasterOperations
{
    public static PixelSurface Resize(PixelSurface source, int width, int height, bool nearest = false)
    {
        var result = new PixelSurface(width, height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sx = (x + 0.5) * source.Width / width - 0.5;
            var sy = (y + 0.5) * source.Height / height - 0.5;
            result.Set(x, y, nearest ? source.Get(Math.Clamp((int)Math.Round(sx), 0, source.Width - 1), Math.Clamp((int)Math.Round(sy), 0, source.Height - 1)) : Sample(source, sx, sy));
        }
        return result;
    }
    public static Rgba32 Sample(PixelSurface source, double x, double y)
    {
        x = Math.Clamp(x, 0, source.Width - 1);
        y = Math.Clamp(y, 0, source.Height - 1);
        var x0 = (int)x;
        var y0 = (int)y;
        var tx = x - x0;
        var ty = y - y0;
        var pixels = new[] { source.Get(x0, y0), source.Get(Math.Min(x0 + 1, source.Width - 1), y0), source.Get(x0, Math.Min(y0 + 1, source.Height - 1)), source.Get(Math.Min(x0 + 1, source.Width - 1), Math.Min(y0 + 1, source.Height - 1)) };
        var weights = new[] { (1 - tx) * (1 - ty), tx * (1 - ty), (1 - tx) * ty, tx * ty };
        double r = 0, g = 0, b = 0, a = 0;
        for (var i = 0; i < 4; i++)
        {
            var alpha = pixels[i].A / 255.0 * weights[i];
            r += pixels[i].R * alpha;
            g += pixels[i].G * alpha;
            b += pixels[i].B * alpha;
            a += alpha;
        }
        return a <= 0 ? Rgba32.Transparent : new(Rgba32.Byte(r / a), Rgba32.Byte(g / a), Rgba32.Byte(b / a), Rgba32.Byte(a * 255));
    }
    public static PixelSurface Crop(PixelSurface source, int x, int y, int width, int height)
    {
        var result = new PixelSurface(width, height);
        for (var py = 0; py < height; py++)
        for (var px = 0; px < width; px++)
            result.Set(px, py, source.Get(px + x, py + y));
        return result;
    }
    public static PixelSurface Rotate90(PixelSurface source, bool clockwise = true)
    {
        var result = new PixelSurface(source.Height, source.Width);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            result.Set(clockwise ? source.Height - 1 - y : y, clockwise ? x : source.Width - 1 - x, source.Get(x, y));
        return result;
    }
    public static PixelSurface Flip(PixelSurface source, bool horizontal)
    {
        var result = new PixelSurface(source.Width, source.Height);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
            result.Set(horizontal ? source.Width - 1 - x : x, horizontal ? y : source.Height - 1 - y, source.Get(x, y));
        return result;
    }
    public static int[] Histogram(PixelSurface source, int channel = -1)
    {
        var result = new int[256];
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var c = source.Get(x, y);
            if (c.A == 0)
                continue;
            var v = channel switch
            {
                0 => c.R,
                1 => c.G,
                2 => c.B,
                _ => Rgba32.Byte(c.R * 0.2126 + c.G * 0.7152 + c.B * 0.0722)
            };
            result[v]++;
        }
        return result;
    }
    public static void Fill(ImageDocument document, Layer layer, Rgba32 color, PixelSurface? region = null)
    {
        if (layer.Locked || layer.Pixels is null)
            return;
        for (var y = 0; y < layer.Pixels.Height; y++)
        for (var x = 0; x < layer.Pixels.Width; x++)
        {
            var p = layer.ToDocument(new(x, y));
            var coverage = document.Coverage((int)p.X, (int)p.Y) * (region?.Get(x, y).A / 255f ?? 1);
            if (coverage > 0)
                layer.Pixels.Set(x, y, Rgba32.Over(layer.Pixels.Get(x, y), color, coverage));
        }
    }
}
