using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ImageSpace.Core;
using ImageSpace.Imaging;
using Old = ImageSpace.Benchmarks.Baseline;

const int width = 1024, height = 1024;
var random = new Random(137);
var bytes = new byte[width * height * 4];
random.NextBytes(bytes);
for (var i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
var before = Old.PixelSurface.FromRgba(width, height, bytes);
var after = PixelSurface.FromRgba(width, height, bytes);
var output = new List<object>();
Compare("Import 1024x1024 RGBA", () => Old.PixelSurface.FromRgba(width, height, bytes), () => PixelSurface.FromRgba(width, height, bytes));
Compare("Fill 1024x1024", () => { var p = new Old.PixelSurface(width, height); p.Fill(new(80, 120, 160)); return p; },
    () => { var p = new PixelSurface(width, height); p.Fill(new(80, 120, 160)); return p; });
Compare("Luminance histogram 1024x1024", () => Old.RasterOperations.Histogram(before), () => RasterOperations.Histogram(after));
Compare("Bilinear resize 1024x1024 to 768x768", () => Old.RasterOperations.Resize(before, 768, 768), () => RasterOperations.Resize(after, 768, 768));
Compare("Crop 1024x1024 to 768x768", () => Old.RasterOperations.Crop(before, 100, 100, 768, 768), () => RasterOperations.Crop(after, 100, 100, 768, 768));
Compare("Flip horizontal 1024x1024", () => Old.RasterOperations.Flip(before, true), () => RasterOperations.Flip(after, true));
var selection = new PixelSurface(width, height);
selection.Fill(Rgba32.White);
Compare("Selection outline enumeration 1024x1024 rectangle", () => LegacyOutline(selection),
    () => SelectionContours.Trace(selection, static _ => { }).Segments);
var document = new ImageDocument(width, height);
var mappedLayer = Layer.Raster("Coverage", width, height);
mappedLayer.X = -20;
mappedLayer.Y = 15;
mappedLayer.Rotation = 23;
Compare("Rotated pixel coverage 1024x1024", () => LegacyCoverage(document, mappedLayer),
    () => PreparedCoverage(document, mappedLayer));
Directory.CreateDirectory("artifacts");
File.WriteAllText("artifacts/performance-results.json", JsonSerializer.Serialize(new
{
    interactionBaselineCommit = "c77d49c40ebd0d4cb5f5b6e9f9c4e30001059e80",
    baselineCommit = "494efd1ad31ce7323fde94839d385b24a82e1aef",
    framework = RuntimeInformation.FrameworkDescription,
    operatingSystem = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    processorCount = Environment.ProcessorCount,
    methodology = "Same-process Release, deterministic input, 2 warm-ups and 7 alternating samples; medians. CPU kernels only, not hardware GPU timing.",
    results = output
}, new JsonSerializerOptions { WriteIndented = true }));

void Compare(string name, Func<object> legacy, Func<object> optimized)
{
    for (var i = 0; i < 2; i++)
    {
        GC.KeepAlive(legacy());
        GC.KeepAlive(optimized());
    }
    var oldTimes = new List<double>();
    var newTimes = new List<double>();
    var oldAlloc = new List<long>();
    var newAlloc = new List<long>();
    for (var i = 0; i < 7; i++)
    {
        if ((i & 1) == 0)
        {
            Measure(legacy, oldTimes, oldAlloc);
            Measure(optimized, newTimes, newAlloc);
        }
        else
        {
            Measure(optimized, newTimes, newAlloc);
            Measure(legacy, oldTimes, oldAlloc);
        }
    }
    oldTimes.Sort();
    newTimes.Sort();
    oldAlloc.Sort();
    newAlloc.Sort();
    var speedup = oldTimes[3] / newTimes[3];
    Console.WriteLine($"{name}: {oldTimes[3]:0.###} -> {newTimes[3]:0.###} ms ({speedup:0.##}x), allocated {oldAlloc[3]:N0} -> {newAlloc[3]:N0} bytes");
    output.Add(new
    {
        name,
        baselineMilliseconds = oldTimes[3],
        optimizedMilliseconds = newTimes[3],
        speedup,
        baselineAllocatedBytes = oldAlloc[3],
        optimizedAllocatedBytes = newAlloc[3]
    });
}
static void Measure(Func<object> operation, List<double> elapsed, List<long> allocations)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    var bytes = GC.GetAllocatedBytesForCurrentThread();
    var begin = Stopwatch.GetTimestamp();
    var result = operation();
    var end = Stopwatch.GetTimestamp();
    allocations.Add(GC.GetAllocatedBytesForCurrentThread() - bytes);
    elapsed.Add(Stopwatch.GetElapsedTime(begin, end).TotalMilliseconds);
    GC.KeepAlive(result);
}


// Frozen interaction baselines from c77d49c: full-frame RGBA outline scan and per-pixel SRT construction.
static long LegacyOutline(PixelSurface surface)
{
    var data = surface.ToRgba();
    var w = surface.Width;
    var h = surface.Height;
    long edges = 0;
    for (var y = 0; y < h; y++)
    for (var x = 0; x < w; x++)
    {
        if (data[(y * w + x) * 4 + 3] < 128)
            continue;
        if (x == 0 || data[(y * w + x - 1) * 4 + 3] < 128)
            edges++;
        if (y == 0 || data[((y - 1) * w + x) * 4 + 3] < 128)
            edges++;
        if (x == w - 1 || data[(y * w + x + 1) * 4 + 3] < 128)
            edges++;
        if (y == h - 1 || data[((y + 1) * w + x) * 4 + 3] < 128)
            edges++;
    }
    return edges;
}
static float LegacyCoverage(ImageDocument document, Layer layer)
{
    float sum = 0;
    for (var y = 0; y < document.Height; y++)
    for (var x = 0; x < document.Width; x++)
    {
        var point = layer.ToDocument(new Vector2(x + .5f, y + .5f));
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || point.X < 0 || point.Y < 0 ||
            point.X >= document.Width || point.Y >= document.Height)
            continue;
        sum += document.Coverage((int)MathF.Floor(point.X), (int)MathF.Floor(point.Y));
    }
    return sum;
}
static float PreparedCoverage(ImageDocument document, Layer layer)
{
    var mapping = PixelTarget.Prepare(document, layer);
    float sum = 0;
    for (var y = 0; y < document.Height; y++)
    for (var x = 0; x < document.Width; x++)
        sum += mapping.Coverage(x, y);
    return sum;
}
