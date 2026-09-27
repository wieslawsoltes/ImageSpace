using System.Text.Json;
using ImageSpace.Core;
using ImageSpace.Imaging;
using ImageSpace.Skia;
using ImageSpace.Editing;
using ImageSpace.Documents;
using SkiaSharp;

var tests = new List<(string Name, Action Test)>();
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Test(string name, Action test) => tests.Add((name, test));
ImageDocument Document(int width = 260, int height = 130)
{
    var document = new ImageDocument(width, height);
    var layer = Layer.Raster("Pixels", width, height);
    layer.Pixels!.Set(2, 2, Rgba32.White);
    layer.Pixels.Set(width - 1, height - 1, new Rgba32(30, 90, 140));
    document.Layers.Add(layer);
    document.ActiveLayerId = layer.Id;
    return document;
}
Test("renderer refreshes a mutated tile without requiring a snapshot", () =>
{
    var document = Document();
    using var renderer = new ImageRenderer();
    Check(renderer.Rasterize(document).Get(2, 2) == Rgba32.White);
    document.ActiveLayer!.Pixels!.Set(2, 2, new Rgba32(255, 0, 0));
    Check(renderer.Rasterize(document).Get(2, 2) == new Rgba32(255, 0, 0));
});
Test("renderer retains unchanged tile uploads", () =>
{
    var document = Document();
    using var renderer = new ImageRenderer();
    renderer.Rasterize(document);
    var uploads = renderer.TileUploads;
    renderer.Rasterize(document);
    Check(renderer.TileUploads == uploads);
    document.ActiveLayer!.Pixels!.Set(2, 2, Rgba32.Black);
    renderer.Rasterize(document);
    Check(renderer.TileUploads == uploads + 1);
});
Test("renderer restores old tile generations on undo", () =>
{
    var session = new EditorSession(Document());
    using var renderer = new ImageRenderer();
    renderer.Rasterize(session.Document);
    session.Execute("Paint", document => document.ActiveLayer!.Pixels!.Set(2, 2, Rgba32.Black));
    Check(renderer.Rasterize(session.Document).Get(2, 2) == Rgba32.Black);
    session.Undo();
    Check(renderer.Rasterize(session.Document).Get(2, 2) == Rgba32.White);
    session.Redo();
    Check(renderer.Rasterize(session.Document).Get(2, 2) == Rgba32.Black);
});
Test("mask updates invalidate their own cached image", () =>
{
    var document = Document(16, 16);
    document.ActiveLayer!.Mask = new PixelSurface(16, 16);
    document.ActiveLayer.Mask.Fill(Rgba32.White);
    using var renderer = new ImageRenderer();
    Check(renderer.Rasterize(document).Get(2, 2).A == 255);
    document.ActiveLayer.Mask.Set(2, 2, Rgba32.Transparent);
    Check(renderer.Rasterize(document).Get(2, 2).A == 0);
});
Test("tile revisions remain isolated across snapshots", () =>
{
    var surface = new PixelSurface(8, 8);
    surface.Set(0, 0, Rgba32.White);
    var clone = surface.Snapshot();
    var revision = clone.GetTileRevision(0, 0);
    surface.Set(0, 0, Rgba32.Black);
    Check(clone.GetTileRevision(0, 0) == revision && surface.GetTileRevision(0, 0) > revision);
});
Test("setting identical bytes avoids redundant revisions", () =>
{
    var surface = new PixelSurface(8, 8);
    surface.Set(0, 0, Rgba32.White);
    var revision = surface.Revision;
    surface.Set(0, 0, Rgba32.White);
    Check(surface.Revision == revision);
});
Test("mask outside extent clears the layer", () =>
{
    var document = Document(16, 16);
    document.ActiveLayer!.Pixels!.Fill(Rgba32.White);
    document.ActiveLayer.Mask = new PixelSurface(4, 4);
    document.ActiveLayer.Mask.Fill(Rgba32.White);
    using var renderer = new ImageRenderer();
    var pixels = renderer.Rasterize(document);
    Check(pixels.Get(2, 2).A == 255 && pixels.Get(8, 8).A == 0);
});
Test("adjustment opacity zero is identity", () =>
{
    var document = Document(16, 16);
    document.Layers.Add(new Layer { Kind = LayerKind.Adjustment, Adjustment = AdjustmentKind.Invert, Opacity = 0 });
    using var renderer = new ImageRenderer();
    Check(renderer.Rasterize(document).Get(2, 2) == Rgba32.White);
});
Test("export does not mutate the editable document", () =>
{
    var document = SampleDocument.Create();
    using var renderer = new ImageRenderer();
    var archive = DocumentArchive.Save(document);
    renderer.Export(document, SKEncodedImageFormat.Png);
    Check(DocumentArchive.Load(archive).Layers.Count == document.Layers.Count && document.ActiveLayer!.Kind == LayerKind.Text);
});
Test("sample composes visible artwork", () =>
{
    var document = SampleDocument.Create();
    using var renderer = new ImageRenderer();
    var pixels = renderer.Rasterize(document);
    Check(pixels.Get(500, 650).A == 255 && pixels.Get(10, 10) != pixels.Get(900, 500));
});
ToneRegressionTests.Register(Test);
var failed = 0;
var results = new List<object>();
foreach (var (name, test) in tests)
{
    try { test(); Console.WriteLine("PASS " + name); results.Add(new { name, passed = true }); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error); results.Add(new { name, passed = false, error = error.ToString() }); }
}
Directory.CreateDirectory("artifacts");
File.WriteAllText("artifacts/regression-tests.json", JsonSerializer.Serialize(new
{
    total = tests.Count, passed = tests.Count - failed, failed, results
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{tests.Count - failed}/{tests.Count} regression tests passed");
return failed == 0 ? 0 : 1;
