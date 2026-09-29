using ImageSpace.Core;
namespace ImageSpace.Filters;

public enum FilterKind
{
    Invert, Grayscale, Sepia, BrightnessContrast, Saturation, Gamma, Threshold, Posterize, GaussianBlur, Sharpen, Emboss, Edges, Pixelate, Noise
}
public static class FilterEngine
{
    public static PixelSurface Apply(PixelSurface source, FilterKind kind, float amount = 0, float secondary = 0, int seed = 42)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!float.IsFinite(amount) || !float.IsFinite(secondary))
            throw new ArgumentException("Filter parameters must be finite.");
        if (kind == FilterKind.GaussianBlur) return Blur(source, Math.Clamp(amount, 0, 32));
        if (kind is FilterKind.Sharpen or FilterKind.Emboss or FilterKind.Edges) return Convolve(source, kind, amount);
        if (kind == FilterKind.Pixelate) return Pixelate(source, Math.Clamp((int)amount, 2, 128));
        var output = new PixelSurface(source.Width, source.Height);
        var random = new Random(seed);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var c = source.Get(x, y);
            if (c.A == 0) continue;
            double r = c.R, g = c.G, b = c.B;
            var l = r * 0.2126 + g * 0.7152 + b * 0.0722;
            switch (kind)
            {
                case FilterKind.Invert: r = 255 - r; g = 255 - g; b = 255 - b; break;
                case FilterKind.Grayscale: r = g = b = l; break;
                case FilterKind.Sepia:
                    r = c.R * .393 + c.G * .769 + c.B * .189;
                    g = c.R * .349 + c.G * .686 + c.B * .168;
                    b = c.R * .272 + c.G * .534 + c.B * .131;
                    break;
                case FilterKind.BrightnessContrast:
                    var contrast = 1 + Math.Clamp(secondary, -99, 300) / 100.0;
                    r = (r - 127.5) * contrast + 127.5 + amount * 2.55;
                    g = (g - 127.5) * contrast + 127.5 + amount * 2.55;
                    b = (b - 127.5) * contrast + 127.5 + amount * 2.55;
                    break;
                case FilterKind.Saturation:
                    var sat = Math.Max(0, 1 + amount / 100);
                    r = l + (r - l) * sat; g = l + (g - l) * sat; b = l + (b - l) * sat;
                    break;
                case FilterKind.Gamma:
                    var gamma = 1 / Math.Clamp(amount, .1, 10);
                    r = 255 * Math.Pow(r / 255, gamma); g = 255 * Math.Pow(g / 255, gamma); b = 255 * Math.Pow(b / 255, gamma);
                    break;
                case FilterKind.Threshold: r = g = b = l >= Math.Clamp(amount, 0, 255) ? 255 : 0; break;
                case FilterKind.Posterize:
                    var levels = Math.Clamp((int)amount, 2, 256) - 1;
                    r = Math.Round(r / 255 * levels) * 255 / levels;
                    g = Math.Round(g / 255 * levels) * 255 / levels;
                    b = Math.Round(b / 255 * levels) * 255 / levels;
                    break;
                case FilterKind.Noise:
                    var n = (random.NextDouble() - .5) * Math.Clamp(amount, 0, 100) * 5.1;
                    r += n; g += n; b += n;
                    break;
            }
            output.Set(x, y, new(Rgba32.Byte(r), Rgba32.Byte(g), Rgba32.Byte(b), c.A));
        }
        return output;
    }

    public static PixelSurface Blur(PixelSurface source, float sigma)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!float.IsFinite(sigma)) throw new ArgumentOutOfRangeException(nameof(sigma));
        if (sigma <= 0) return source.Snapshot();
        sigma = Math.Clamp(sigma, .1f, 32);
        var radius = (int)Math.Ceiling(sigma * 3);
        var weights = new double[radius * 2 + 1]; double total = 0;
        for (var i = -radius; i <= radius; i++)
        {
            var w = Math.Exp(-i * i / (2 * sigma * sigma)); weights[i + radius] = w; total += w;
        }
        for (var i = 0; i < weights.Length; i++) weights[i] /= total;
        var middle = new float[checked(source.Width * source.Height * 4)];
        var output = new PixelSurface(source.Width, source.Height);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            var index = (y * source.Width + x) * 4;
            for (var i = -radius; i <= radius; i++)
            {
                var c = source.Get(Math.Clamp(x + i, 0, source.Width - 1), y);
                var w = (float)weights[i + radius]; var a = c.A / 255f;
                middle[index] += c.R * a * w; middle[index + 1] += c.G * a * w;
                middle[index + 2] += c.B * a * w; middle[index + 3] += a * w;
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
                    r += middle[index] * w; g += middle[index + 1] * w; b += middle[index + 2] * w; a += middle[index + 3] * w;
                }
                if (a > 0)
                {
                    var offset = x * 4;
                    row[offset] = Rgba32.Byte(r / a); row[offset + 1] = Rgba32.Byte(g / a);
                    row[offset + 2] = Rgba32.Byte(b / a); row[offset + 3] = Rgba32.Byte(a * 255);
                }
            }
            // Preserve the complete spatial result, including RGB where rounded alpha
            // becomes zero. It must not depend on which tile was allocated first.
            output.WriteRow(0, y, row);
        }
        return output;
    }

    private static PixelSurface Convolve(PixelSurface source, FilterKind kind, float amount)
    {
        var strength = Math.Clamp(amount == 0 ? 1 : amount, .1f, 5);
        float[] kernel = kind switch
        {
            FilterKind.Emboss => [-2, -1, 0, -1, 1, 1, 0, 1, 2],
            FilterKind.Edges => [-1, -1, -1, -1, 8, -1, -1, -1, -1],
            _ => [0, -strength, 0, -strength, 1 + 4 * strength, -strength, 0, -strength, 0]
        };
        var output = new PixelSurface(source.Width, source.Height);
        var row = new byte[source.Width * 4];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                double r = 0, g = 0, b = 0;
                for (var ky = -1; ky <= 1; ky++)
                for (var kx = -1; kx <= 1; kx++)
                {
                    var c = source.Get(Math.Clamp(x + kx, 0, source.Width - 1), Math.Clamp(y + ky, 0, source.Height - 1));
                    var w = kernel[(ky + 1) * 3 + kx + 1]; r += c.R * w; g += c.G * w; b += c.B * w;
                }
                var bias = kind == FilterKind.Emboss ? 128 : 0; var offset = x * 4;
                row[offset] = Rgba32.Byte(r + bias); row[offset + 1] = Rgba32.Byte(g + bias);
                row[offset + 2] = Rgba32.Byte(b + bias); row[offset + 3] = source.Get(x, y).A;
            }
            output.WriteRow(0, y, row);
        }
        return output;
    }

    private static PixelSurface Pixelate(PixelSurface source, int size)
    {
        var output = new PixelSurface(source.Width, source.Height);
        var blockRow = new byte[Math.Min(size, source.Width) * 4];
        for (var y = 0; y < source.Height; y += size)
        for (var x = 0; x < source.Width; x += size)
        {
            double r = 0, g = 0, b = 0, a = 0; var n = 0;
            var right = Math.Min(x + size, source.Width); var bottom = Math.Min(y + size, source.Height);
            for (var py = y; py < bottom; py++)
            for (var px = x; px < right; px++)
            {
                var c = source.Get(px, py); var alpha = c.A / 255.0;
                r += c.R * alpha; g += c.G * alpha; b += c.B * alpha; a += alpha; n++;
            }
            var color = a <= 0 ? Rgba32.Transparent : new Rgba32(Rgba32.Byte(r / a), Rgba32.Byte(g / a), Rgba32.Byte(b / a), Rgba32.Byte(a / n * 255));
            for (var px = 0; px < right - x; px++)
            {
                var at = px * 4;
                blockRow[at] = color.R; blockRow[at + 1] = color.G; blockRow[at + 2] = color.B; blockRow[at + 3] = color.A;
            }
            for (var py = y; py < bottom; py++) output.WriteRow(x, py, blockRow.AsSpan(0, (right - x) * 4));
        }
        return output;
    }
}
