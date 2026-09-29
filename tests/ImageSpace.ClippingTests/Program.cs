using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImageSpace.Core;
using ImageSpace.Documents;
using ImageSpace.Editing;
using ImageSpace.Imaging;
using System.Numerics;
using ImageSpace.Skia;
using SkiaSharp;

var tests = new List<(string Name, Action Body)>();
void Test(string name, Action body) => tests.Add((name, body));
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Reject(Action action)
{
    try
    {
        action();
    }
    catch (Exception e) when (e is InvalidDataException or InvalidOperationException or ArgumentException) { return; }
    throw new Exception("Expected rejection.");
}
Layer Solid(string name, Rgba32 color, int width = 4, int height = 3)
{
    var layer = Layer.Raster(name, width, height);
    layer.Pixels!.Fill(color);
    return layer;
}
ImageDocument Doc(params Layer[] layers) => new(4, 3) { Layers = layers.ToList(), ActiveLayerId = layers[^1].Id };
Rgba32 Pixel(ImageDocument document)
{
    using var renderer = new ImageRenderer();
    return renderer.Rasterize(document).Get(1, 1);
}
void Close(Rgba32 a, Rgba32 b, double tolerance = 2)
{
    // Compare premultiplied values, avoiding meaningless unpremultiplied color error at alpha≈0.
    Check(Math.Abs(a.A - b.A) <= 1 && Math.Abs(a.R * a.A / 255.0 - b.R * b.A / 255.0) <= tolerance &&
        Math.Abs(a.G * a.A / 255.0 - b.G * b.A / 255.0) <= tolerance &&
        Math.Abs(a.B * a.A / 255.0 - b.B * b.A / 255.0) <= tolerance, $"{a} differs from {b}");
}
foreach (var mode in Enum.GetValues<LayerBlend>())
    foreach (var coverage in new byte[] { 0, 1, 64, 128, 255 })
        foreach (var opacity in new[] { 1f, .37f })
        {
            var blend = mode;
            var alpha = coverage;
            var strength = opacity;
            Test($"alpha-locked {blend}, base alpha {alpha}, source opacity {strength}", () =>
            {
                var basis = Solid("Base", new(61, 117, 181, alpha));
                var clip = Solid("Clip", new(193, 43, 109, 173));
                clip.Opacity = strength;
                clip.Blend = blend;
                clip.IsClipped = true;
                var output = Pixel(Doc(basis, clip));
                var opaque = basis.Snapshot();
                opaque.Pixels = Solid("", new(61, 117, 181)).Pixels;
                var separate = clip.Snapshot();
                separate.IsClipped = false;
                // Near-zero base alpha can quantize its RGB before clipping. The expected
                // opaque backdrop is the actual decoded base color at that coverage.
                var quantized = Pixel(Doc(basis));
                opaque.Pixels!.Fill(new(quantized.R, quantized.G, quantized.B));
                var reference = Pixel(Doc(opaque, separate));
                Close(output, new(reference.R, reference.G, reference.B, alpha));
            });
        }
foreach (var blend in Enum.GetValues<LayerBlend>())
{
    var mode = blend;
    Test("base mode and opacity apply once to the group: " + mode, () =>
    {
        var background = Solid("Background", new(73, 119, 41));
        var basis = Solid("Base", new(39, 79, 131, 128));
        basis.Opacity = .5f;
        basis.Blend = mode;
        var clip = Solid("Clip", new(183, 53, 107));
        clip.IsClipped = true;
        var flattened = Solid("Expected", new(183, 53, 107, 128));
        flattened.Opacity = .5f;
        flattened.Blend = mode;
        Close(Pixel(Doc(background, basis, clip)), Pixel(Doc(background.Snapshot(), flattened)));
    });
}
Test("normal clipping preserves translucent coverage instead of squaring or growing it", () =>
{
    var basis = Solid("Base", new(0, 0, 255, 128));
    var clip = Solid("Clip", new(255, 0, 0));
    clip.IsClipped = true;
    Check(Pixel(Doc(basis, clip)) == new Rgba32(255, 0, 0, 128));
    clip.Pixels!.Fill(new(0, 255, 0, 128));
    Close(Pixel(Doc(basis, clip)), new(0, 128, 127, 128));
});
Test("multi-layer chain retains base holes and all layers remain editable", () =>
{
    var basis = Solid("Base", new(0, 0, 255));
    basis.Pixels!.Set(1, 1, Rgba32.Transparent);
    var first = Solid("First", new(255, 0, 0, 127));
    first.IsClipped = true;
    var last = Solid("Last", new(0, 255, 0, 127));
    last.IsClipped = true;
    var doc = Doc(basis, first, last);
    var before = doc.Layers.Select(l => l.Pixels!.Revision).ToArray();
    using var renderer = new ImageRenderer();
    var image = renderer.Rasterize(doc);
    Check(image.Get(1, 1).A == 0 && image.Get(2, 1).A == 255);
    Check(before.SequenceEqual(doc.Layers.Select(l => l.Pixels!.Revision)));
    var builds = renderer.ClippingBlenderBuilds;
    var uploads = renderer.TileUploads;
    renderer.Rasterize(doc);
    Check(renderer.ClippingBlenderBuilds == builds && renderer.TileUploads == uploads);
    Check(renderer.ClippingGroupDraws == 2);
});
Test("hidden base hides the complete chain; hidden clipped layer does not", () =>
{
    var basis = Solid("Base", new(0, 0, 255));
    var clip = Solid("Clip", new(255, 0, 0));
    clip.IsClipped = true;
    basis.Visible = false;
    Check(Pixel(Doc(basis, clip)).A == 0);
    basis.Visible = true;
    clip.Visible = false;
    Check(Pixel(Doc(basis, clip)) == new Rgba32(0, 0, 255));
});
Test("base masks affect coverage exactly once and clipped masks affect their own contribution", () =>
{
    var basis = Solid("Base", new(0, 0, 255));
    basis.Mask = Solid("", new(255, 255, 255, 128)).Pixels;
    var clip = Solid("Clip", new(255, 0, 0));
    clip.IsClipped = true;
    clip.Mask = basis.Mask!.Snapshot();
    Close(Pixel(Doc(basis, clip)), new(128, 0, 127, 128));
    basis.MaskDensity = 0;
    Close(Pixel(Doc(basis, clip)), new(128, 0, 127));
    clip.MaskEnabled = false;
    Check(Pixel(Doc(basis, clip)) == new Rgba32(255, 0, 0));
});
foreach (var adjustment in new[] { AdjustmentKind.Invert, AdjustmentKind.Grayscale, AdjustmentKind.GaussianBlur, AdjustmentKind.Curves, AdjustmentKind.Levels })
{
    var kind = adjustment;
    Test("clipped adjustment preserves holes and translucent base alpha: " + kind, () =>
    {
        var basis = Solid("Base", new(61, 117, 183, 128));
        basis.Pixels!.Set(1, 1, Rgba32.Transparent);
        var effect = new Layer { Kind = LayerKind.Adjustment, Adjustment = kind, Amount = 2, IsClipped = true, Width = 4, Height = 3 };
        var doc = Doc(basis, effect);
        using var renderer = new ImageRenderer();
        var output = renderer.Rasterize(doc);
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 4; x++)
            Check(Math.Abs(output.Get(x, y).A - basis.Pixels!.Get(x, y).A) <= 1, "Adjustment changed group coverage.");
    });
}
Test("global and clipped adjustments respect independent compositing scopes", () =>
{
    var background = Solid("Background", new(0, 255, 0));
    var basis = Solid("Base", new(0, 0, 255));
    basis.Width = 2;
    basis.Pixels = new PixelSurface(2, 3);
    basis.Pixels.Fill(new(0, 0, 255));
    var clip = new Layer { Kind = LayerKind.Adjustment, Adjustment = AdjustmentKind.Invert, IsClipped = true };
    var doc = Doc(background, basis, clip);
    using var renderer = new ImageRenderer();
    var image = renderer.Rasterize(doc);
    Check(image.Get(0, 1) == new Rgba32(255, 255, 0) && image.Get(3, 1) == new Rgba32(0, 255, 0));
    doc.Layers.Add(new Layer { Kind = LayerKind.Adjustment, Adjustment = AdjustmentKind.Invert });
    image = renderer.Rasterize(doc);
    Check(image.Get(0, 1) == new Rgba32(0, 0, 255) && image.Get(3, 1) == new Rgba32(255, 0, 255));
});
foreach (var kind in new[] { LayerKind.Rectangle, LayerKind.Ellipse, LayerKind.Text })
{
    var type = kind;
    Test("shape/type clipping bases retain their rendered coverage: " + type, () =>
    {
        var basis = new Layer
        {
            Kind = type,
            Width = 32,
            Height = 24,
            X = 8,
            Y = 10,
            ScaleX = .8f,
            ScaleY = 1.1f,
            Rotation = 13,
            Text = "A",
            FontSize = 20,
            Color = new(51, 91, 131, 180),
            StrokeWidth = 2
        };
        var clip = Solid("Clip", new(255, 0, 0), 64, 64);
        clip.IsClipped = true;
        var doc = new ImageDocument(64, 64) { Layers = [basis, clip] };
        using var renderer = new ImageRenderer();
        var source = renderer.RasterizeLayer(doc, basis);
        var actual = renderer.Rasterize(doc);
        for (var y = 0; y < 64; y++)
        for (var x = 0; x < 64; x++)
            Check(Math.Abs(source.Get(x, y).A - actual.Get(x, y).A) <= 1);
    });
}
Test("canvas stack, transform and clipping are restored", () =>
{
    using var target = SKSurface.Create(new SKImageInfo(32, 32));
    target.Canvas.Translate(3, 4);
    target.Canvas.Scale(2);
    target.Canvas.ClipRect(new(0, 0, 8, 8));
    var count = target.Canvas.SaveCount;
    var matrix = target.Canvas.TotalMatrix;
    var bounds = target.Canvas.LocalClipBounds;
    var basis = Solid("Base", new(31, 71, 131, 128));
    var clip = Solid("Clip", new(61, 21, 131));
    clip.IsClipped = true;
    clip.Blend = LayerBlend.Hue;
    using var renderer = new ImageRenderer();
    renderer.Draw(target.Canvas, Doc(basis, clip));
    Check(target.Canvas.SaveCount == count && target.Canvas.TotalMatrix == matrix && target.Canvas.LocalClipBounds == bounds);
});
Test("create/release clipping tail, undo/redo, locks and change notifications", () =>
{
    var a = Solid("A", Rgba32.White);
    var b = Solid("B", Rgba32.Black);
    var c = Solid("C", Rgba32.White);
    var session = new EditorSession(Doc(a, b, c));
    session.SelectLayer(b.Id);
    session.CreateClippingMask();
    session.SelectLayer(c.Id);
    session.CreateClippingMask();
    Check(session.History.Count == 2);
    session.SelectLayer(b.Id);
    c.Locked = true;
    Check(!session.CanReleaseClippingMask);
    Reject(session.ReleaseClippingMask);
    Check(b.IsClipped && c.IsClipped && session.History.Count == 2);
    c.Locked = false;
    session.ReleaseClippingMask();
    Check(!b.IsClipped && !c.IsClipped && session.History.Count == 3);
    session.Undo();
    Check(session.Document.Layers[1].IsClipped && session.Document.Layers[2].IsClipped);
    session.Redo();
    Check(!session.Document.Layers[1].IsClipped && !session.Document.Layers[2].IsClipped);
});
Test("model rejects orphan chains and adjustment bases", () =>
{
    var layer = Solid("Orphan", Rgba32.White);
    layer.IsClipped = true;
    Reject(() => Doc(layer).Validate());
    var adjustment = new Layer { Kind = LayerKind.Adjustment };
    Reject(() => Doc(adjustment, layer).Validate());
    var session = new EditorSession(Doc(Solid("Bottom", Rgba32.White)));
    Check(!session.CanCreateClippingMask);
    Reject(session.CreateClippingMask);
});
Test("deleting a base releases followers rather than retargeting to unrelated content", () =>
{
    var other = Solid("Other", Rgba32.White);
    var basis = Solid("Base", Rgba32.Black);
    var clip = Solid("Clip", Rgba32.White);
    clip.IsClipped = true;
    var session = new EditorSession(Doc(other, basis, clip));
    session.SelectLayer(basis.Id);
    session.DeleteLayer();
    Check(!session.Document.Layers[1].IsClipped);
    session.Undo();
    Check(session.Document.Layers[2].IsClipped);
});
Test("reorder treats complete clipping chains as units", () =>
{
    var a = Solid("A", Rgba32.White);
    var b = Solid("B", Rgba32.White);
    var c = Solid("C", Rgba32.White);
    c.IsClipped = true;
    var d = Solid("D", Rgba32.White);
    var session = new EditorSession(Doc(a, b, c, d));
    session.SelectLayer(c.Id);
    session.MoveLayer(-1);
    Check(session.Document.Layers.Select(l => l.Name).SequenceEqual(new[] { "B", "C", "A", "D" }));
    session.MoveLayer(int.MaxValue);
    Check(session.Document.Layers.Select(l => l.Name).SequenceEqual(new[] { "A", "D", "B", "C" }));
    session.Document.Validate();
    session.Undo();
    session.Document.Validate();
});
Test("duplicating a base clones its complete chain with independent pixel ownership", () =>
{
    var basis = Solid("Base", Rgba32.White);
    var clip = Solid("Clip", Rgba32.Black);
    clip.IsClipped = true;
    var session = new EditorSession(Doc(basis, clip));
    session.SelectLayer(basis.Id);
    session.DuplicateLayer();
    var layers = session.Document.Layers;
    Check(layers.Count == 4 && layers[1].IsClipped && layers[3].IsClipped && !layers[2].IsClipped);
    Check(layers.Select(l => l.Id).Distinct().Count() == 4);
    layers[3].Pixels!.Set(1, 1, Rgba32.White);
    Check(layers[1].Pixels!.Get(1, 1) == Rgba32.Black);
});
Test("preview stamps invalidate only for actual clipping changes", () =>
{
    var layer = Solid("Layer", Rgba32.White);
    var before = LayerRenderStamp.Capture(layer);
    layer.Name = "Renamed";
    Check(before == LayerRenderStamp.Capture(layer));
    layer.IsClipped = true;
    Check(before != LayerRenderStamp.Capture(layer));
});
Test("native version 5 roundtrips clipping and rejects forged legacy declaration", () =>
{
    var basis = Solid("Base", Rgba32.White);
    var clip = Solid("Clip", Rgba32.Black);
    clip.IsClipped = true;
    var bytes = DocumentArchive.Save(Doc(basis, clip));
    var loaded = DocumentArchive.Load(bytes);
    Check(loaded.Layers[1].IsClipped);
    using var input = new ZipArchive(new MemoryStream(bytes));
    var manifest = JsonNode.Parse(new StreamReader(input.GetEntry("manifest.json")!.Open()).ReadToEnd())!;
    Check(manifest["version"]!.GetValue<int>() == 5);
    manifest["version"] = 4;
    using var output = new MemoryStream();
    using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
    foreach (var entry in input.Entries)
    {
        using var target = zip.CreateEntry(entry.FullName).Open();
        if (entry.FullName == "manifest.json")
            target.Write(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        else
            entry.Open().CopyTo(target);
    }
    Reject(() => DocumentArchive.Load(output.ToArray()));
    Check(!DocumentArchive.Load(DocumentArchive.Save(Doc(basis))).Layers[0].IsClipped);
});
foreach (var compression in Enum.GetValues<PsdCompression>())
{
    var method = compression;
    Test("PSD clipping byte roundtrip with opacity/blends: " + method, () =>
    {
        var basis = Solid("Base", new(31, 71, 151, 128));
        basis.Opacity = .7f;
        basis.Blend = LayerBlend.Multiply;
        var clip = Solid("Clip", new(91, 41, 111, 180));
        clip.IsClipped = true;
        clip.Blend = LayerBlend.Screen;
        var doc = Doc(basis, clip);
        using var renderer = new ImageRenderer();
        var image = renderer.Rasterize(doc);
        var bytes = PsdCodec.Save(doc, l => renderer.RasterizeLayer(doc, l), image, method);
        var result = PsdCodec.Load(bytes);
        Check(!result.Document.Layers[0].IsClipped && result.Document.Layers[1].IsClipped);
        var output = renderer.Rasterize(result.Document);
        for (var y = 0; y < 3; y++)
        for (var x = 0; x < 4; x++)
            Close(image.Get(x, y), output.Get(x, y));
        Check(!result.Warnings.Any(w => w.Contains("Clipping-chain relationships")));
    });
}
Test("clipped adjustment cannot be silently dropped by layered PSD export", () =>
{
    var doc = Doc(Solid("Base", Rgba32.White), new Layer { Kind = LayerKind.Adjustment, Adjustment = AdjustmentKind.Invert, IsClipped = true });
    using var renderer = new ImageRenderer();
    Reject(() => PsdCodec.Save(doc, l => renderer.RasterizeLayer(doc, l), renderer.Rasterize(doc)));
});
Test("hit testing excludes clipped content outside base geometry without renderer work", () =>
{
    var backdrop = Solid("Backdrop", Rgba32.White);
    var basis = new Layer { Kind = LayerKind.Ellipse, Width = 2, Height = 2 };
    var clip = Solid("Clip", Rgba32.Black);
    clip.IsClipped = true;
    var doc = Doc(backdrop, basis, clip);
    Check(LayerHitTesting.Hit(doc, new(1, 1)) == clip);
    Check(LayerHitTesting.Hit(doc, new(3, 1)) == backdrop);
    Check(LayerHitTesting.Hit(doc, new(.05f, .05f)) == backdrop);
    basis.Visible = false;
    Check(LayerHitTesting.Hit(doc, new(1, 1)) == backdrop);
    Check(LayerHitTesting.Hit(doc, new(float.NaN, 0)) is null);
});
Test("clipping study creates independent editable content and warmed blender caches", () =>
{
    var doc = ClippingStudy.Create();
    doc.Validate();
    using var renderer = new ImageRenderer();
    renderer.RasterizePreview(doc, 160);
    var builds = renderer.ClippingBlenderBuilds;
    var uploads = renderer.TileUploads;
    renderer.RasterizePreview(doc, 160);
    Check(renderer.ClippingBlenderBuilds == builds && renderer.TileUploads == uploads);
    Check(doc.Layers.Count == 7 && doc.Layers.Count(l => l.IsClipped) == 3);
});
Test("native clipping change invalidates cached document preview", () =>
{
    var basis = Solid("Base", new(0, 0, 255, 128));
    var clip = Solid("Clip", new(255, 0, 0));
    var doc = Doc(basis, clip);
    using var renderer = new ImageRenderer();
    using var cache = new DocumentPreviewCache();
    var before = cache.Get(doc, 2, 64, renderer).Get(1, 1);
    clip.IsClipped = true;
    var after = cache.Get(doc, 2, 64, renderer).Get(1, 1);
    Check(before.A == 255 && after.A == 128 && cache.Builds == 2);
    cache.Get(doc, 2, 64, renderer);
    Check(cache.Builds == 2);
});
ClippingInsertionTests.Register(Test);

var failed = 0;
var results = new List<object>();
foreach (var (name, body) in tests)
{
    try
    {
        body();
        results.Add(new
        {
            name,
            passed = true
        });
    }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error}"); results.Add(new { name, passed = false, error = error.ToString() }); }
}
Directory.CreateDirectory("artifacts");
File.WriteAllText("artifacts/clipping-tests.json", JsonSerializer.Serialize(new { total = tests.Count, passed = tests.Count - failed, failed, results }));
Console.WriteLine($"CLIPPING_TESTS total={tests.Count} passed={tests.Count - failed} failed={failed}");
return failed == 0 ? 0 : 1;
