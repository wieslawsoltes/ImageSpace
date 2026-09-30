using System.Buffers;
using System.Text.Json;
using ImageSpace.Core;
using ImageSpace.Filters;

var tests = new List<(string Name, Func<Task> Body)>();
void Test(string name, Action body) => tests.Add((name, () => { body(); return Task.CompletedTask; }));
void AsyncTest(string name, Func<Task> body) => tests.Add((name, body));
void Check(bool condition, string message = "Assertion failed")
{
    if (!condition)
        throw new InvalidOperationException(message);
}
void Equal(PixelSurface expected, PixelSurface actual)
{
    Check(expected.Width == actual.Width && expected.Height == actual.Height, "Dimensions changed.");
    var a = expected.ToRgba();
    var b = actual.ToRgba();
    for (var i = 0; i < a.Length; i++)
        if (a[i] != b[i])
            throw new InvalidOperationException($"Byte {i}: expected {a[i]}, got {b[i]}.");
}
void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

(int Width, int Height)[] dimensions = [(1, 1), (1, 257), (257, 1), (2, 67), (33, 31), (129, 131), (257, 65), (7, 8192)];
float[] sigmas = [-2, 0, .01f, .3f, 1, 2, 7.25f, 32, 64];
foreach (var (width, height) in dimensions)
    foreach (var sigma in sigmas)
        foreach (var pattern in new[] { "mixed", "sparse", "hidden" })
            Test($"frozen RGBA equality: {width}x{height}, sigma={sigma}, {pattern}", () =>
            {
                var source = GaussianFixtures.Create(width, height, pattern);
                var before = source.ToRgba();
                var revision = source.Revision;
                var expected = FrozenGaussian.Apply(source, sigma);
                var actual = GaussianBlurProcessor.Apply(source, sigma);
                Equal(expected, actual);
                Check(before.SequenceEqual(source.ToRgba()), "Source bytes changed.");
                Check(revision == source.Revision, "Source revision changed.");
                actual.Set(0, 0, Rgba32.White);
                Check(before.SequenceEqual(source.ToRgba()), "Output mutation leaked into input.");
            });

foreach (var sigma in new[] { 0f, .3f, 2f, 32f })
    AsyncTest($"cooperative RGBA equality and synchronous input capture: sigma={sigma}", async () =>
    {
        var source = GaussianFixtures.Create(129, 67, "mixed");
        var captured = source.Snapshot();
        var pending = GaussianBlurProcessor.ApplyAsync(source, sigma);
        source.Fill(Rgba32.Black);
        var result = await pending;
        Equal(FrozenGaussian.Apply(captured, sigma), result);
    });

Test("wrapper preserves the established FilterEngine entry points", () =>
{
    var source = GaussianFixtures.Create(133, 29, "sparse");
    Equal(FrozenGaussian.Apply(source, 4), FilterEngine.Blur(source, 4));
    Equal(FrozenGaussian.Apply(source, 4), FilterEngine.Apply(source, FilterKind.GaussianBlur, 4));
});

Test("maximum-image workspace scales with kernel height rather than canvas height", () =>
{
    var plan = GaussianBlurProcessor.GetWorkspace(4096, 4096, 8);
    Check(plan.Radius == 24 && plan.CachedRows == 49);
    Check(plan.IntermediateBytes == 4096L * 49 * 16);
    Check(plan.RowBufferBytes == 4096L * 8 + 24 * 8);
    Check(plan.KernelBytes == 49 * 16);
    Check(plan.RequestedScratchBytes == plan.IntermediateBytes + plan.RowBufferBytes + plan.KernelBytes);
    Check(plan.IntermediateBytes * 80 < 4096L * 4096 * 16);
    var small = GaussianBlurProcessor.GetWorkspace(8192, 1, 32);
    Check(small.CachedRows == 1 && small.IntermediateBytes == 8192 * 16);
    Check(GaussianBlurProcessor.GetWorkspace(1, 8192, 32).CachedRows == 193);
    Check(GaussianBlurProcessor.GetWorkspace(4096, 4096, 0).RequestedScratchBytes == 0);
});

Test("invalid dimensions and sigma reject without processing", () =>
{
    foreach (var sigma in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
    {
        Throws<ArgumentOutOfRangeException>(() => GaussianBlurProcessor.GetWorkspace(1, 1, sigma));
        Throws<ArgumentOutOfRangeException>(() => GaussianBlurProcessor.Apply(new PixelSurface(1, 1), sigma));
        Throws<ArgumentOutOfRangeException>(() => GaussianBlurProcessor.ApplyAsync(new PixelSurface(1, 1), sigma));
    }
    Throws<ArgumentException>(() => GaussianBlurProcessor.GetWorkspace(0, 1, 1));
    Throws<ArgumentException>(() => GaussianBlurProcessor.GetWorkspace(8192, 8192, 1));
    Throws<ArgumentNullException>(() => GaussianBlurProcessor.Apply(null!, 1));
    Throws<ArgumentNullException>(() => GaussianBlurProcessor.ApplyAsync(null!, 1));
});

Test("pre-cancelled calls reject including identity blur", () =>
{
    var source = GaussianFixtures.Create(17, 31, "mixed");
    var token = new CancellationToken(true);
    foreach (var sigma in new[] { 0f, 3f })
    {
        Throws<OperationCanceledException>(() => GaussianBlurProcessor.Apply(source, sigma, token));
        Throws<OperationCanceledException>(() => GaussianBlurProcessor.ApplyAsync(source, sigma, token));
    }
});

AsyncTest("cooperative cancellation leaves source untouched and subsequent calls usable", async () =>
{
    var source = GaussianFixtures.Create(512, 512, "mixed");
    var before = source.ToRgba();
    using var cancellation = new CancellationTokenSource();
    var pending = GaussianBlurProcessor.ApplyAsync(source, 32, cancellation.Token);
    cancellation.Cancel();
    var rejected = false;
    try { await pending; }
    catch (OperationCanceledException error) { rejected = error.CancellationToken == cancellation.Token; }
    Check(rejected);
    Check(before.SequenceEqual(source.ToRgba()));
    var small = GaussianFixtures.Create(31, 257, "mixed");
    var result = await GaussianBlurProcessor.ApplyAsync(small, 2);
    Equal(FrozenGaussian.Apply(small, 2), result);
});

Test("dirty pooled buffers cannot leak into transparent output", () =>
{
    var plan = GaussianBlurProcessor.GetWorkspace(129, 131, 2);
    var floats = ArrayPool<float>.Shared.Rent((int)(plan.IntermediateBytes / 4));
    Array.Fill(floats, float.NaN);
    ArrayPool<float>.Shared.Return(floats);
    var bytes = ArrayPool<byte>.Shared.Rent((int)plan.RowBufferBytes);
    Array.Fill(bytes, (byte)255);
    ArrayPool<byte>.Shared.Return(bytes);
    var actual = GaussianBlurProcessor.Apply(new PixelSurface(129, 131), 2);
    Check(actual.ToRgba().All(value => value == 0));
    Equal(FrozenGaussian.Apply(GaussianFixtures.Create(129, 131, "sparse"), 2),
        GaussianBlurProcessor.Apply(GaussianFixtures.Create(129, 131, "sparse"), 2));
});

AsyncTest("independent evaluations can execute concurrently without sharing workspaces", async () =>
{
    var originals = Enumerable.Range(0, 4).Select(i => GaussianFixtures.Create(33 + i, 129 + i, "mixed")).ToArray();
    var actual = await Task.WhenAll(originals.Select((source, i) => Task.Run(() => GaussianBlurProcessor.Apply(source, i + 1))));
    for (var i = 0; i < originals.Length; i++)
        Equal(FrozenGaussian.Apply(originals[i], i + 1), actual[i]);
});

AsyncTest("CPU filter session Gaussian stages reset to immutable input", async () =>
{
    var source = GaussianFixtures.Create(129, 33, "mixed");
    await using var session = new CpuFilterSession(source);
    FilterOperation[] recipe = [new(FilterKind.GaussianBlur, 3), new(FilterKind.Invert), new(FilterKind.GaussianBlur, .3f)];
    var expected = FrozenGaussian.Apply(FilterEngine.Apply(FrozenGaussian.Apply(source, 3), FilterKind.Invert), .3f);
    var result = await session.ApplyAsync(recipe);
    Equal(expected, result);
    result.Fill(Rgba32.Black);
    var second = await session.ApplyAsync(recipe);
    Equal(expected, second);
});

var failures = 0;
var results = new List<object>();
foreach (var (name, body) in tests)
{
    try { await body(); results.Add(new { name, passed = true }); }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}\n{error}");
        results.Add(new { name, passed = false, error = error.ToString() });
    }
}
Directory.CreateDirectory("artifacts");
File.WriteAllText("artifacts/gaussian-tests.json", JsonSerializer.Serialize(new
{
    total = tests.Count, passed = tests.Count - failures, failed = failures, results
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"GAUSSIAN_TESTS total={tests.Count} passed={tests.Count - failures} failed={failures}");
if (failures != 0)
    return 1;
GaussianBenchmarks.Run();
return 0;
