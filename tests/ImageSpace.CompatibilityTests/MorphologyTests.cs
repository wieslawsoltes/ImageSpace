using ImageSpace.Core;
using ImageSpace.Imaging;

internal static partial class Program
{
    private static void RegisterMorphologyTests()
    {
        foreach (var radius in new[] { 0, 1, 3, 18, 256 })
            foreach (var modification in Enum.GetValues<SelectionModification>())
            {
                Test($"selection {modification}, radius {radius}, agrees with scalar morphology", () =>
                {
                    var source = Seed(17, 11);
                    var before = source.ToRgba();
                    var a = Scalar(source, radius, true);
                    var b = Scalar(source, radius, false);
                    PixelSurface expected;
                    if (modification == SelectionModification.Expand)
                        expected = a;
                    else if (modification == SelectionModification.Contract)
                        expected = b;
                    else if (modification == SelectionModification.Border)
                    {
                        expected = new(source.Width, source.Height);
                        for (var y = 0; y < source.Height; y++)
                        for (var x = 0; x < source.Width; x++)
                        {
                            var alpha = (byte)Math.Max(0, a.Get(x, y).A - b.Get(x, y).A);
                            expected.Set(x, y, new(255, 255, 255, alpha));
                        }
                    }
                    else
                        expected = Scalar(Scalar(Scalar(b, radius, true), radius, true), radius, false);
                    var actual = SelectionMorphology.Apply(source, modification, radius);
                    for (var y = 0; y < source.Height; y++)
                    for (var x = 0; x < source.Width; x++)
                        Check(expected.Get(x, y).A == actual.Get(x, y).A, $"Coverage differs at {x},{y}.");
                    Check(before.SequenceEqual(source.ToRgba()));
                });
            }
        Test("morphology cancellation and limits", () =>
        {
            var source = Seed(17, 11);
            Throws<OperationCanceledException>(() => SelectionMorphology.Apply(source, SelectionModification.Expand, 4, new CancellationToken(true)));
            Throws<ArgumentOutOfRangeException>(() => SelectionMorphology.Apply(source, SelectionModification.Expand, -1));
            Throws<ArgumentOutOfRangeException>(() => SelectionMorphology.Apply(source, SelectionModification.Expand, 257));
            Throws<ArgumentOutOfRangeException>(() => SelectionMorphology.Apply(source, (SelectionModification)42, 1));
        });
        Test("morphology preserves soft coverage without binarizing it", () =>
        {
            var source = new PixelSurface(7, 7);
            source.Set(3, 3, new(255, 255, 255, 91));
            var expanded = SelectionMorphology.Apply(source, SelectionModification.Expand, 1);
            Check(expanded.Get(2, 2).A == 91 && expanded.Get(1, 1).A == 0);
            var contracted = SelectionMorphology.Apply(expanded, SelectionModification.Contract, 1);
            Check(contracted.Get(3, 3).A == 91 && contracted.Get(2, 2).A == 0);
        });
    }

    private static PixelSurface Scalar(PixelSurface source, int radius, bool maximum)
    {
        if (radius == 0)
            return source.Snapshot();
        var result = new PixelSurface(source.Width, source.Height);
        for (var y = 0; y < source.Height; y++)
        for (var x = 0; x < source.Width; x++)
        {
            // Out-of-canvas zero is the minimum and cannot increase the maximum.
            var outside = x < radius || y < radius || x + radius >= source.Width || y + radius >= source.Height;
            var value = maximum || outside ? 0 : 255;
            if (maximum || !outside)
                for (var sy = Math.Max(0, y - radius); sy <= Math.Min(source.Height - 1, y + radius); sy++)
                    for (var sx = Math.Max(0, x - radius); sx <= Math.Min(source.Width - 1, x + radius); sx++)
                        value = maximum ? Math.Max(value, source.Get(sx, sy).A) : Math.Min(value, source.Get(sx, sy).A);
            result.Set(x, y, new(255, 255, 255, (byte)value));
        }
        return result;
    }
}
