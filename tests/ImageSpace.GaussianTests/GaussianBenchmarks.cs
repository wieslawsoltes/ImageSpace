using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ImageSpace.Core;
using ImageSpace.Filters;

internal static class GaussianBenchmarks
{
    public static void Run()
    {
        var results = new List<object>();
        foreach (var (width, height, sigma) in new[] { (512, 512, 2f), (512, 512, 8f), (257, 129, 32f) })
        {
            var source = GaussianFixtures.Create(width, height, "mixed");
            for (var i = 0; i < 2; i++)
            {
                FrozenGaussian.Apply(source, sigma);
                GaussianBlurProcessor.Apply(source, sigma);
            }
            var baseline = new List<double>();
            var streaming = new List<double>();
            var baselineBytes = new List<long>();
            var streamingBytes = new List<long>();
            void Measure(Func<PixelSurface> work, List<double> times, List<long> allocations)
            {
                var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                var result = work();
                times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                allocations.Add(GC.GetAllocatedBytesForCurrentThread() - beforeBytes);
                GC.KeepAlive(result);
            }
            for (var i = 0; i < 7; i++)
            {
                if ((i & 1) == 0)
                {
                    Measure(() => FrozenGaussian.Apply(source, sigma), baseline, baselineBytes);
                    Measure(() => GaussianBlurProcessor.Apply(source, sigma), streaming, streamingBytes);
                }
                else
                {
                    Measure(() => GaussianBlurProcessor.Apply(source, sigma), streaming, streamingBytes);
                    Measure(() => FrozenGaussian.Apply(source, sigma), baseline, baselineBytes);
                }
            }
            var a = baseline.Order().ElementAt(3);
            var b = streaming.Order().ElementAt(3);
            var row = new
            {
                width, height, sigma, baselineMedian = a, streamingMedian = b, ratio = a / b,
                baselineManagedBytes = baselineBytes.Order().ElementAt(3),
                streamingManagedBytes = streamingBytes.Order().ElementAt(3),
                baselineIntermediateBytes = (long)width * height * 16,
                workspace = GaussianBlurProcessor.GetWorkspace(width, height, sigma),
                baselineMilliseconds = baseline, streamingMilliseconds = streaming
            };
            results.Add(row);
            Console.WriteLine("STREAMING_GAUSSIAN_BENCHMARK " + JsonSerializer.Serialize(row));
        }
        File.WriteAllText("artifacts/gaussian-performance-results.json", JsonSerializer.Serialize(new
        {
            runtime = RuntimeInformation.FrameworkDescription,
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            baselineCommit = "a59eca8078e553d266c4bdd8aef8dea792b22ad3",
            warmups = 2, samples = 7, results,
            scope = "Same-process synchronous CPU Release evaluation; warmed shared pools. Managed allocations include output tiles, exclude retained pool capacity and native memory. Requested scratch is not process RSS or a GPU measurement."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
