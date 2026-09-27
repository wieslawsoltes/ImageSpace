using System.Collections.Immutable;

namespace ImageSpace.Core;

/// <summary>
/// Immutable, shape-preserving cubic tone curve. Interior extrema are allowed;
/// interpolation does not overshoot the output range of each segment.
/// </summary>
public sealed record ToneCurve
{
    public const int MaximumPoints = 16;
    public static ToneCurve Identity { get; } = new();
    public ImmutableArray<CurvePoint> Points { get; init; } = [new(0, 0), new(255, 255)];

    public void Validate()
    {
        if (Points.IsDefault || Points.Length is < 2 or > MaximumPoints ||
            Points[0].Input != 0 || Points[^1].Input != 255)
        {
            throw new InvalidDataException("A curve requires 2–16 points, including inputs 0 and 255.");
        }
        var previous = -1;
        foreach (var point in Points)
        {
            if (point.Input <= previous || point.Input > 255 || point.Output is < 0 or > 255)
            {
                throw new InvalidDataException("Curve inputs must increase strictly and outputs must be in 0–255.");
            }
            previous = point.Input;
        }
    }

    public byte[] CreateLookup()
    {
        Validate();
        var count = Points.Length;
        Span<double> widths = stackalloc double[count - 1];
        Span<double> secants = stackalloc double[count - 1];
        Span<double> tangents = stackalloc double[count];
        for (var i = 0; i < count - 1; i++)
        {
            widths[i] = Points[i + 1].Input - Points[i].Input;
            secants[i] = (Points[i + 1].Output - Points[i].Output) / widths[i];
        }
        if (count == 2)
        {
            tangents[0] = tangents[1] = secants[0];
        }
        else
        {
            tangents[0] = Endpoint(widths[0], widths[1], secants[0], secants[1]);
            tangents[count - 1] = Endpoint(widths[^1], widths[^2], secants[^1], secants[^2]);
            for (var i = 1; i < count - 1; i++)
            {
                if (secants[i - 1] * secants[i] <= 0)
                {
                    tangents[i] = 0;
                    continue;
                }
                var a = 2 * widths[i] + widths[i - 1];
                var b = widths[i] + 2 * widths[i - 1];
                tangents[i] = (a + b) / (a / secants[i - 1] + b / secants[i]);
            }
        }
        var table = new byte[256];
        var segment = 0;
        for (var input = 0; input < table.Length; input++)
        {
            while (segment < count - 2 && input > Points[segment + 1].Input)
            {
                segment++;
            }
            var t = (input - Points[segment].Input) / widths[segment];
            var t2 = t * t;
            var t3 = t2 * t;
            var start = Points[segment].Output;
            var end = Points[segment + 1].Output;
            var value = (2 * t3 - 3 * t2 + 1) * start + (t3 - 2 * t2 + t) * widths[segment] * tangents[segment]
                + (-2 * t3 + 3 * t2) * end + (t3 - t2) * widths[segment] * tangents[segment + 1];
            table[input] = Rgba32.Byte(Math.Clamp(value, Math.Min(start, end), Math.Max(start, end)));
        }
        return table;
    }

    private static double Endpoint(double h0, double h1, double d0, double d1)
    {
        var tangent = ((2 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
        if (Math.Sign(tangent) != Math.Sign(d0))
        {
            return 0;
        }
        return Math.Sign(d0) != Math.Sign(d1) && Math.Abs(tangent) > Math.Abs(3 * d0) ? 3 * d0 : tangent;
    }
}
