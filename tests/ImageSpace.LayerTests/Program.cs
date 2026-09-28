using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using ImageSpace.Core;
using ImageSpace.Documents;
using ImageSpace.Editing;
using ImageSpace.Skia;
using SkiaSharp;

var tests = new List<(string Name, Action Body)>();
void Test(string name, Action body) => tests.Add((name, body));
void Check(bool condition, string message = "Assertion failed")
{
    if (!condition) throw new InvalidOperationException(message);
}
void Throws(Action action)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    catch (ArgumentException) { return; }
    throw new Exception("Expected rejection.");
}
void SamePixels(PixelSurface expected, PixelSurface actual)
{
    Check(expected.Width == actual.Width && expected.Height == actual.Height, "Different source dimensions.");
    for (var y = 0; y < expected.Height; y++)
    for (var x = 0; x < expected.Width; x++)
        Check(expected.Get(x, y) == actual.Get(x, y), $"Pixels differ at {x},{y}.");
}
void ClosePixels(PixelSurface expected, PixelSurface actual, int tolerance = 2)
{
    Check(expected.Width == actual.Width && expected.Height == actual.Height);
    for (var y = 0; y < expected.Height; y++)
    for (var x = 0; x < expected.Width; x++)
    {
        var a = expected.Get(x, y);
        var b = actual.Get(x, y);
        Check(Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance &&
            Math.Abs(a.B - b.B) <= tolerance && Math.Abs(a.A - b.A) <= tolerance,
            $"RGBA differs at {x},{y}: {a} versus {b}; tolerance {tolerance}.");
    }
}
Layer Raster(int width = 33, int height = 25)
{
    var layer = Layer.Raster("Source", width, height);
    byte[] alpha = [0, 1, 32, 127, 128, 254, 255];
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
        layer.Pixels!.Set(x, y, new Rgba32((byte)(x * 7 + 1), (byte)(y * 9 + 3), (byte)(x * 3 + y * 5), alpha[(x + y) % alpha.Length]));
    return layer;
}
PixelSurface Mask(int width, int height)
{
    var mask = new PixelSurface(width, height);
    byte[] alpha = [0, 64, 128, 192, 255];
    for (var y = 0; y < height; y++)
    for (var x = 0; x < width; x++)
        mask.Set(x, y, new Rgba32(255, 255, 255, alpha[(x + y) % alpha.Length]));
    return mask;
}
ImageDocument Scene(Layer layer, int width = 64, int height = 48)
{
    var document = new ImageDocument(width, height);
    var background = Layer.Raster("Background", width, height);
    background.Pixels!.Fill(new Rgba32(61, 117, 173));
    document.Layers.AddRange([background, layer]);
    document.ActiveLayerId = layer.Id;
    return document;
}
EditorSession MaskedSession()
{
    var layer = Raster();
    layer.Mask = Mask(33, 25);
    layer.X = -8;
    layer.Y = -4;
    var document = Scene(layer, 8, 6);
    document.EditMask = true;
    document.Selection = new PixelSurface(8, 6); // Intentionally selects nothing.
    return new EditorSession(document);
}

foreach (var kind in new[] { LayerKind.Raster, LayerKind.Text, LayerKind.Rectangle, LayerKind.Ellipse })
foreach (var blend in Enum.GetValues<LayerBlend>())
foreach (var placement in new[] { 0, 1, 2 })
{
    var k = kind;
    var b = blend;
    var p = placement;
    Test($"isolated differential: {k}, {b}, placement {p}", () =>
    {
        var layer = k == LayerKind.Raster ? Raster() : new Layer
        {
            Kind = k, Width = 33, Height = 25, FontSize = 15, Text = "Aa\nMask",
            Color = new Rgba32(231, 91, 43, 157), StrokeColor = new Rgba32(21, 161, 211, 133),
            StrokeWidth = k == LayerKind.Text ? 0 : 3, CornerRadius = 6
        };
        layer.Blend = b;
        layer.X = p == 2 ? 45 : 8;
        layer.Y = 7;
        layer.Rotation = p == 1 ? 23 : p == 2 ? -17 : 0;
        layer.ScaleX = p == 2 ? -1.1f : 1;
        layer.ScaleY = p == 0 ? 1 : .8f;
        var scene = Scene(layer);
        using var optimized = new ImageRenderer();
        using var reference = new ImageRenderer { EnableDirectLayerDrawing = false };
        ClosePixels(reference.Rasterize(scene), optimized.Rasterize(scene));
        Check(optimized.DirectLayerDraws == (b == LayerBlend.Normal ? 2 : 1));
        Check(optimized.IsolatedLayerDraws == (b == LayerBlend.Normal ? 0 : 1));
        var uploads = optimized.TileUploads;
        optimized.Rasterize(scene);
        Check(optimized.TileUploads == uploads, "Warm rendering reuploaded unchanged tiles.");
    });
}

foreach (var opacity in new[] { 0f, .25f, .5f, .99f, 1f })
{
    var value = opacity;
    Test($"group opacity preserves fill/stroke overlap: {value}", () =>
    {
        var layer = new Layer
        {
            Kind = LayerKind.Rectangle, Width = 30, Height = 24, X = 5, Y = 6,
            Color = new Rgba32(220, 20, 40, 170), StrokeColor = new Rgba32(20, 220, 40, 190),
            StrokeWidth = 12, Opacity = value
        };
        using var optimized = new ImageRenderer();
        using var reference = new ImageRenderer { EnableDirectLayerDrawing = false };
        var scene = Scene(layer);
        ClosePixels(reference.Rasterize(scene), optimized.Rasterize(scene));
        Check(optimized.IsolatedLayerDraws == (value == 1 ? 0 : 1));
    });
}

foreach (var maskMode in new[] { 0, 1, 2, 3 })
{
    var mode = maskMode;
    Test($"effective-mask fast-path eligibility {mode}", () =>
    {
        var layer = Raster();
        layer.Mask = Mask(33, 25);
        layer.MaskEnabled = mode != 0;
        layer.MaskDensity = mode == 1 ? 0 : .7f;
        layer.MaskFeather = mode == 3 ? 2 : 0;
        using var optimized = new ImageRenderer();
        using var reference = new ImageRenderer { EnableDirectLayerDrawing = false };
        var scene = Scene(layer);
        ClosePixels(reference.Rasterize(scene), optimized.Rasterize(scene));
        Check(optimized.IsolatedLayerDraws == (mode < 2 ? 0 : 1));
    });
}

Test("ignoreOpacity keeps standalone rasterization independent of blend and visibility", () =>
{
    var layer = Raster();
    layer.Visible = false;
    layer.Opacity = .2f;
    layer.Blend = LayerBlend.ColorDodge;
    var scene = Scene(layer);
    using var optimized = new ImageRenderer();
    using var reference = new ImageRenderer { EnableDirectLayerDrawing = false };
    SamePixels(reference.RasterizeLayer(scene, layer), optimized.RasterizeLayer(scene, layer));
    Check(optimized.DirectLayerDraws == 1 && optimized.IsolatedLayerDraws == 0);
});

Test("canvas save stack restored on direct and isolated layer paths", () =>
{
    using var surface = SKSurface.Create(new SKImageInfo(64, 48));
    using var renderer = new ImageRenderer();
    var layer = Raster();
    var count = surface.Canvas.SaveCount;
    renderer.DrawLayer(surface.Canvas, layer);
    Check(surface.Canvas.SaveCount == count);
    layer.Mask = Mask(33, 25);
    renderer.DrawLayer(surface.Canvas, layer);
    Check(surface.Canvas.SaveCount == count);
});

Test("apply mask preserves hidden RGB, extent, source and mask revisions, selection and metadata", () =>
{
    var session = MaskedSession();
    var layer = session.Document.ActiveLayer!;
    var pixels = layer.Pixels!.Snapshot();
    var mask = layer.Mask!;
    var pixelRevision = layer.Pixels.Revision;
    var maskRevision = mask.Revision;
    layer.Opacity = .4f;
    layer.Blend = LayerBlend.Screen;
    using var renderer = new ImageRenderer();
    session.ApplyLayerMask(renderer.BakeLayerMask);
    Check(session.History.Count == 1 && layer.Mask is null && !session.Document.EditMask);
    Check(layer.X == -8 && layer.Y == -4 && layer.Opacity == .4f && layer.Blend == LayerBlend.Screen);
    Check(layer.Pixels!.Width == 33 && layer.Pixels.Height == 25);
    Check(session.Document.Selection is { Width: 8, Height: 6 });
    Check(pixels.Revision == pixelRevision && mask.Revision == maskRevision);
    for (var y = 0; y < 25; y++)
    for (var x = 0; x < 33; x++)
    {
        var source = pixels.Get(x, y);
        var result = layer.Pixels.Get(x, y);
        Check(result.R == source.R && result.G == source.G && result.B == source.B, "Authored RGB changed.");
        Check(result.A == (source.A * mask.Get(x, y).A + 127) / 255, "Mask alpha was not applied exactly once.");
    }
    var applied = layer.Pixels.Snapshot();
    session.Undo();
    Check(session.Document.ActiveLayer!.Mask is not null && session.Document.EditMask);
    SamePixels(pixels, session.Document.ActiveLayer.Pixels!);
    session.Redo();
    Check(session.Document.ActiveLayer!.Mask is null);
    SamePixels(applied, session.Document.ActiveLayer.Pixels!);
    var loaded = DocumentArchive.Load(DocumentArchive.Save(session.Document));
    SamePixels(applied, loaded.ActiveLayer!.Pixels!);
    Check(loaded.ActiveLayer.Mask is null && loaded.ActiveLayer.X == -8 && loaded.ActiveLayer.Y == -4);
});

foreach (var linked in new[] { true, false })
foreach (var density in new[] { 0f, .25f, 1f })
foreach (var feather in new[] { 0f, 2f })
{
    var isLinked = linked;
    var d = density;
    var f = feather;
    Test($"mask application agrees with local compositor: linked={isLinked}, density={d}, feather={f}", () =>
    {
        var layer = Raster();
        layer.Mask = Mask(17, 13);
        layer.ScaleX = -1.2f;
        layer.ScaleY = .85f;
        layer.Rotation = 23;
        layer.X = 14;
        layer.Y = -3;
        layer.Opacity = .3f;
        layer.Blend = LayerBlend.Multiply;
        layer.MaskDensity = d;
        layer.MaskFeather = f;
        var localFrame = new Matrix3x2(.7f, .2f, -.1f, .9f, 2, -1);
        layer.MaskLinked = isLinked;
        layer.MaskPlacement = AffinePlacement.FromMatrix(isLinked ? localFrame : localFrame * layer.Transform);
        using var renderer = new ImageRenderer();
        var output = renderer.BakeLayerMask(layer);
        var local = layer.Snapshot();
        var frame = local.MaskLocalTransform;
        local.X = local.Y = local.Rotation = 0;
        local.ScaleX = local.ScaleY = 1;
        local.MaskLinked = true;
        local.MaskPlacement = AffinePlacement.FromMatrix(frame);
        var expected = renderer.RasterizeLayer(new ImageDocument(33, 25), local);
        for (var y = 0; y < 25; y++)
        for (var x = 0; x < 33; x++)
        {
            var source = layer.Pixels!.Get(x, y);
            var actual = output.Get(x, y);
            Check(Math.Abs(actual.A - expected.Get(x, y).A) <= 1, "Coverage differs from the local renderer.");
            Check(actual.R == source.R && actual.G == source.G && actual.B == source.B);
        }
        Check(layer.Mask is not null, "Renderer mutated the input model.");
    });
}

foreach (var reason in new[] { "locked", "disabled", "no-mask", "adjustment", "transaction" })
{
    var why = reason;
    Test($"mask application rejects {why} without invoking renderer", () =>
    {
        var session = MaskedSession();
        var layer = session.Document.ActiveLayer!;
        if (why == "locked") layer.Locked = true;
        if (why == "disabled") layer.MaskEnabled = false;
        if (why == "no-mask") layer.Mask = null;
        if (why == "adjustment") layer.Kind = LayerKind.Adjustment;
        if (why == "transaction") session.Begin("Existing gesture");
        var invoked = false;
        Check(!session.CanApplyLayerMask);
        Throws(() => session.ApplyLayerMask(_ => { invoked = true; return new PixelSurface(33, 25); }));
        Check(!invoked && session.History.Count == 0);
        Check(session.IsInTransaction == (why == "transaction"));
    });
}

Test("renderer failure cannot mutate authored input or create a history entry", () =>
{
    var session = MaskedSession();
    var pixels = session.Document.ActiveLayer!.Pixels!.Snapshot();
    Throws(() => session.ApplyLayerMask(input =>
    {
        input.Pixels!.Fill(Rgba32.White);
        input.Mask = null;
        throw new InvalidOperationException("Simulated renderer failure.");
    }));
    Check(session.Document.ActiveLayer!.Mask is not null && session.History.Count == 0 && !session.IsInTransaction);
    SamePixels(pixels, session.Document.ActiveLayer.Pixels!);
});

Test("wrong-size and null outputs are rejected before opening a transaction", () =>
{
    var session = MaskedSession();
    Throws(() => session.ApplyLayerMask(_ => new PixelSurface(8, 6)));
    Throws(() => session.ApplyLayerMask(_ => null!));
    Throws(() => session.ApplyLayerMask(null!));
    Check(session.History.Count == 0 && !session.IsInTransaction && session.Document.ActiveLayer!.Mask is not null);
});

Test("reentrant session changes reject stale mask output", () =>
{
    var session = MaskedSession();
    Throws(() => session.ApplyLayerMask(input => { session.Notify(); return input.Pixels!; }));
    Check(session.History.Count == 0 && session.Document.ActiveLayer!.Mask is not null);
});

Test("committed output has independent copy-on-write ownership", () =>
{
    var session = MaskedSession();
    var output = new PixelSurface(33, 25);
    output.Fill(new Rgba32(17, 31, 47, 73));
    session.ApplyLayerMask(_ => output);
    output.Set(2, 3, Rgba32.White);
    Check(session.Document.ActiveLayer!.Pixels!.Get(2, 3) == new Rgba32(17, 31, 47, 73));
});

var results = new List<object>();
var failures = 0;
foreach (var (name, body) in tests)
{
    try { body(); results.Add(new { name, passed = true }); }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}\n{error}");
        results.Add(new { name, passed = false, error = error.ToString() });
    }
}
Directory.CreateDirectory("artifacts");
File.WriteAllText("artifacts/layer-tests.json", JsonSerializer.Serialize(new
{
    total = tests.Count, passed = tests.Count - failures, failed = failures, results
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"LAYER_TESTS total={tests.Count} passed={tests.Count - failures} failed={failures}");
if (failures != 0) return 1;

// Same-process raster CPU comparison, not GPU timing or a whole-application FPS claim.
var benchmark = new ImageDocument(1024, 768);
for (var i = 0; i < 8; i++)
{
    var layer = Layer.Raster($"Layer {i}", benchmark.Width, benchmark.Height);
    layer.Pixels!.Fill(new Rgba32((byte)(23 + i * 27), (byte)(173 - i * 17), (byte)(71 + i * 13), 127));
    benchmark.Layers.Add(layer);
}
using var target = SKSurface.Create(new SKImageInfo(benchmark.Width, benchmark.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
using var fast = new ImageRenderer();
using var slow = new ImageRenderer { EnableDirectLayerDrawing = false };
void Draw(ImageRenderer renderer)
{
    target.Canvas.Clear(SKColors.Transparent);
    renderer.Draw(target.Canvas, benchmark);
    target.Canvas.Flush();
}
for (var i = 0; i < 3; i++) { Draw(slow); Draw(fast); }
var baseline = new List<double>();
var optimized = new List<double>();
var baselineAllocation = new List<long>();
var optimizedAllocation = new List<long>();
void Measure(ImageRenderer renderer, List<double> times, List<long> allocations)
{
    var allocation = GC.GetAllocatedBytesForCurrentThread();
    var time = Stopwatch.GetTimestamp();
    Draw(renderer);
    times.Add(Stopwatch.GetElapsedTime(time).TotalMilliseconds);
    allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocation);
}
for (var i = 0; i < 9; i++)
{
    if (i % 2 == 0) { Measure(slow, baseline, baselineAllocation); Measure(fast, optimized, optimizedAllocation); }
    else { Measure(fast, optimized, optimizedAllocation); Measure(slow, baseline, baselineAllocation); }
}
double Median(List<double> values) => values.Order().ElementAt(values.Count / 2);
var summary = new
{
    name = "Eight-layer 1024x768 warm raster compositing",
    runtime = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    warmups = 3, samples = 9,
    baselineMilliseconds = Median(baseline), optimizedMilliseconds = Median(optimized),
    ratio = Median(baseline) / Median(optimized),
    baselineManagedBytes = baselineAllocation.Order().ElementAt(4),
    optimizedManagedBytes = optimizedAllocation.Order().ElementAt(4),
    directLayerDraws = fast.DirectLayerDraws, isolatedReferenceDraws = slow.IsolatedLayerDraws,
    baseline, optimized,
    scope = "CPU raster render, warmed tile cache, same process; not GPU/frame-rate/application-wide timing."
};
File.WriteAllText("artifacts/layer-performance-results.json", JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("LAYER_BENCHMARK " + JsonSerializer.Serialize(summary));
return 0;
