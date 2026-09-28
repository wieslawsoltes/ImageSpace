using System.Diagnostics;
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
Directory.CreateDirectory("artifacts");
File.WriteAllText("artifacts/performance-results.json", JsonSerializer.Serialize(new
{
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
