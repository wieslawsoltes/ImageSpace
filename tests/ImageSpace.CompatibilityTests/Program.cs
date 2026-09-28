using System.Text.Json;
using ImageSpace.Core;
using ImageSpace.Imaging;

internal static partial class Program
{
    private static readonly List<(string Name, Action Body)> Tests = [];
    private static void Test(string name, Action body) => Tests.Add((name, body));
    private static void Check(bool condition, string message = "Assertion failed")
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
    private static void Throws<T>(Action body) where T : Exception
    {
        try
        {
            body();
        }
        catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private static void Equal(PixelSurface expected, PixelSurface actual)
    {
        Check(expected.Width == actual.Width && expected.Height == actual.Height, "Dimensions differ.");
        for (var y = 0; y < expected.Height; y++)
            for (var x = 0; x < expected.Width; x++)
            {
                var a = expected.Get(x, y);
                var b = actual.Get(x, y);
                Check(a.A == b.A && (a.A == 0 || a == b), $"Pixel {x},{y}: expected {a}, got {b}.");
            }
    }
    private static PixelSurface Seed(int width = 259, int height = 131)
    {
        var random = new Random(137);
        var bytes = new byte[width * height * 4];
        random.NextBytes(bytes);
        return PixelSurface.FromRgba(width, height, bytes);
    }
    public static int Main()
    {
        RegisterBulkTests();
        RegisterMorphologyTests();
        RegisterPsdTests();
        RegisterPlacementTests();
        RegisterContourTests();
        Directory.CreateDirectory("artifacts/fixtures");
        File.WriteAllBytes("artifacts/fixtures/user-mask-zip.psd", Fixture(ImageSpace.Documents.PsdCompression.ZipPrediction, flags: 16));
        var failed = 0;
        var results = new List<object>();
        foreach (var (name, body) in Tests)
        {
            try
            {
                body();
                results.Add(new
                {
                    name,
                    passed = true
                });
                Console.WriteLine("PASS " + name);
            }
            catch (Exception error) { failed++; results.Add(new { name, passed = false, error = error.ToString() }); Console.Error.WriteLine("FAIL " + name + ": " + error); }
        }
        Directory.CreateDirectory("artifacts");
        File.WriteAllText("artifacts/compatibility-tests.json", JsonSerializer.Serialize(new
        {
            total = Tests.Count,
            passed = Tests.Count - failed,
            failed,
            results
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{Tests.Count - failed}/{Tests.Count} compatibility tests passed");
        return failed == 0 ? 0 : 1;
    }
}
