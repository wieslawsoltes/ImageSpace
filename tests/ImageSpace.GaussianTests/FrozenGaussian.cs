using ImageSpace.Core;

/// <summary>
/// Frozen pre-streaming implementation from a59eca8078e553d266c4bdd8aef8dea792b22ad3.
/// Intentionally retains full-image float scratch, per-tap Get/clamp and arithmetic order.
/// Do not route this reference through the optimized processor.
/// </summary>
internal static class FrozenGaussian
{
    public static PixelSurface Apply(PixelSurface source, float sigma)
    {
        if (sigma <= 0)
            return source.Snapshot();
        sigma = Math.Clamp(sigma, .1f, 32);
        var radius = (int)Math.Ceiling(sigma * 3);
        var weights = new double[radius * 2 + 1];
        double total = 0;
        for (var i = -radius; i <= radius; i++)
        {
            var w = Math.Exp(-i * i / (2 * sigma * sigma));
            weights[i + radius] = w;
            total += w;
        }
        for (var i = 0; i < weights.Length; i++)
            weights[i] /= total;
        var middle = new float[checked(source.Width * source.Height * 4)];
        var output = new PixelSurface(source.Width, source.Height);
        for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
            {
                var index = (y * source.Width + x) * 4;
                for (var i = -radius; i <= radius; i++)
                {
                    var c = source.Get(Math.Clamp(x + i, 0, source.Width - 1), y);
                    var w = (float)weights[i + radius];
                    var a = c.A / 255f;
                    middle[index] += c.R * a * w;
                    middle[index + 1] += c.G * a * w;
                    middle[index + 2] += c.B * a * w;
                    middle[index + 3] += a * w;
                }
            }
        var row = new byte[source.Width * 4];
        for (var y = 0; y < source.Height; y++)
        {
            Array.Clear(row);
            for (var x = 0; x < source.Width; x++)
            {
                double r = 0, g = 0, b = 0, a = 0;
                for (var i = -radius; i <= radius; i++)
                {
                    var index = (Math.Clamp(y + i, 0, source.Height - 1) * source.Width + x) * 4;
                    var w = weights[i + radius];
                    r += middle[index] * w;
                    g += middle[index + 1] * w;
                    b += middle[index + 2] * w;
                    a += middle[index + 3] * w;
                }
                if (a > 0)
                {
                    var offset = x * 4;
                    row[offset] = Rgba32.Byte(r / a);
                    row[offset + 1] = Rgba32.Byte(g / a);
                    row[offset + 2] = Rgba32.Byte(b / a);
                    row[offset + 3] = Rgba32.Byte(a * 255);
                }
            }
            output.WriteRow(0, y, row);
        }
        return output;
    }
}
