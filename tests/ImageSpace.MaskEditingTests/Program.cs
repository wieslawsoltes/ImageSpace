using System.Numerics;
using System.Text.Json;
using ImageSpace.Core;
using ImageSpace.Documents;
using ImageSpace.Editing;
using ImageSpace.Filters;
using ImageSpace.Imaging;
using ImageSpace.Skia;
using SkiaSharp;

var tests = new List<(string Name, Action Body)>();
void Test(string name, Action body) => tests.Add((name, body));
void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
}
void Throws(Action action)
{
    try { action(); } catch (InvalidOperationException) { return; } catch (ArgumentException) { return; }
    throw new Exception("Expected a validation exception.");
}
EditorSession Create(LayerKind kind = LayerKind.Adjustment)
{
    var document = new ImageDocument(16, 12);
    var background = Layer.Raster("Background", 16, 12);
    background.Pixels!.Fill(new Rgba32(80, 120, 160));
    var layer = kind == LayerKind.Raster ? Layer.Raster("Target", 16, 12) : new Layer
    {
        Name = "Target", Kind = kind, Width = 16, Height = 12, Adjustment = AdjustmentKind.Invert
    };
    if (layer.Pixels is not null) layer.Pixels.Fill(new Rgba32(30, 60, 90));
    document.Layers.AddRange([background, layer]);
    document.ActiveLayerId = layer.Id;
    return new EditorSession(document);
}
void Paint(EditorSession session, PaintMode mode, Rgba32 ink, Vector2? position = null, Vector2? offset = null)
{
    var layer = session.Document.ActiveLayer!;
    var brush = new BrushEngine();
    brush.Begin(PixelTarget.RequireEditable(session.Document, layer), offset ?? Vector2.Zero);
    brush.Paint(session.Document, layer, position ?? new Vector2(8.5f, 6.5f), 1,
        new BrushSettings(4, 1, 1, 1, .1f, false), ink, mode);
    brush.End();
}
PixelSurface Preview(ImageRenderer renderer, MaskPreviewRenderer preview, ImageDocument document, MaskPreviewMode mode)
{
    using var surface = SKSurface.Create(new SKImageInfo(document.Width, document.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
    surface.Canvas.Clear(new SKColor(80, 120, 160));
    preview.Draw(surface.Canvas, renderer, document, document.ActiveLayer!, mode);
    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return ImageRenderer.Decode(data.ToArray());
}
foreach (var kind in Enum.GetValues<LayerKind>())
{
    var k = kind;
    Test($"create mask for {kind} without rasterizing content", () =>
    {
        var session = Create(k); session.AddMask();
        Equal(k, session.Document.ActiveLayer!.Kind);
        Equal((byte)255, session.Document.ActiveLayer.Mask!.Get(8, 6).A);
        Check(session.Document.EditMask);
        if (k != LayerKind.Raster) Check(session.Document.ActiveLayer.Pixels is null);
    });
}
Test("adding a second mask never silently replaces existing authored coverage", () =>
{
    var s = Create(); s.AddMask(); s.Document.ActiveLayer!.Mask!.Set(4, 3, Rgba32.Transparent);
    var history = s.History.Count; s.AddMask(); Equal(history, s.History.Count);
    Equal((byte)0, s.Document.ActiveLayer.Mask.Get(4, 3).A);
});
Test("Reveal All includes off-canvas pixels", () =>
{
    var s = Create(); s.Document.ActiveLayer!.X = -8; s.AddMask();
    Equal((byte)255, s.Document.ActiveLayer.Mask!.Get(0, 4).A);
});
Test("an explicitly empty selection creates an empty mask", () =>
{
    var s = Create(); s.Document.Selection = new PixelSurface(16, 12); s.AddMask();
    Equal(0, s.Document.ActiveLayer!.Mask!.AllocatedTiles);
});
Test("mask creation uses transformed pixel centers and floors negative coordinates", () =>
{
    var s = Create(); s.Document.ActiveLayer!.X = -.75f;
    s.Document.Selection = Selections.Rectangle(16, 12, new(0, 0), new(1, 12));
    s.AddMask(); Equal((byte)0, s.Document.ActiveLayer.Mask!.Get(0, 3).A);
    Equal((byte)255, s.Document.ActiveLayer.Mask.Get(1, 3).A);
});
Test("mask fill changes coverage without changing source pixels", () =>
{
    var s = Create(LayerKind.Raster); var before = s.Document.ActiveLayer!.Pixels!.ToRgba();
    s.AddMask(); s.FillPixels(Rgba32.Black);
    Equal((byte)0, s.Document.ActiveLayer!.Mask!.Get(8, 6).A);
    Check(before.SequenceEqual(s.Document.ActiveLayer.Pixels!.ToRgba()));
});
Test("mask fill respects fractional selection alpha", () =>
{
    var s = Create(); s.AddMask();
    s.Document.Selection = new PixelSurface(16, 12); s.Document.Selection.Fill(new Rgba32(255, 255, 255, 128));
    s.FillPixels(Rgba32.Black); Equal((byte)127, s.Document.ActiveLayer!.Mask!.Get(8, 6).A);
});
Test("mask gradient reaches black and white without alpha-over bias", () =>
{
    var s = Create(); s.AddMask();
    s.Execute("Gradient", d => PixelEdits.Gradient(d, d.ActiveLayer!, new(.5f, .5f), new(15.5f, .5f), Rgba32.Black, Rgba32.White));
    Equal((byte)0, s.Document.ActiveLayer!.Mask!.Get(0, 6).A);
    Equal((byte)255, s.Document.ActiveLayer.Mask.Get(15, 6).A);
    Equal((byte)136, s.Document.ActiveLayer.Mask.Get(8, 6).A);
});
Test("zero-length gradient is a no-op", () =>
{
    var s = Create(); s.AddMask(); var revision = s.Document.ActiveLayer!.Mask!.Revision;
    PixelEdits.Gradient(s.Document, s.Document.ActiveLayer, new(1, 1), new(1, 1), Rgba32.Black, Rgba32.White);
    Equal(revision, s.Document.ActiveLayer.Mask.Revision);
});
Test("eraser reduces mask coverage even with a white foreground", () =>
{
    var s = Create(); s.AddMask(); Paint(s, PaintMode.Eraser, Rgba32.White);
    Equal((byte)0, s.Document.ActiveLayer!.Mask!.Get(8, 6).A);
});
Test("mask clone samples source alpha, not foreground luminance", () =>
{
    var s = Create(); s.AddMask(); s.Document.ActiveLayer!.Mask!.Set(4, 6, new Rgba32(255, 255, 255, 64));
    Paint(s, PaintMode.Clone, Rgba32.White, offset: new(-4, 0));
    Equal((byte)64, s.Document.ActiveLayer.Mask.Get(8, 6).A);
});
Test("black brush modifies an adjustment mask without a raster layer", () =>
{
    var s = Create(); s.AddMask(); Paint(s, PaintMode.Brush, Rgba32.Black);
    Equal((byte)0, s.Document.ActiveLayer!.Mask!.Get(8, 6).A);
    Check(s.Document.ActiveLayer.Pixels is null);
});
Test("transparent mask brush ink does not paint", () =>
{
    var s = Create(); s.AddMask(); Paint(s, PaintMode.Brush, new Rgba32(0, 0, 0, 0));
    Equal((byte)255, s.Document.ActiveLayer!.Mask!.Get(8, 6).A);
});
Test("mask clear respects selection and leaves other coverage intact", () =>
{
    var s = Create(LayerKind.Text); s.AddMask();
    s.Document.Selection = Selections.Rectangle(16, 12, new(8, 0), new(16, 12)); s.ClearPixels();
    Equal((byte)255, s.Document.ActiveLayer!.Mask!.Get(2, 6).A); Equal((byte)0, s.Document.ActiveLayer.Mask.Get(12, 6).A);
});
Test("Ctrl+I mask filter respects selection", () =>
{
    var s = Create(); s.AddMask(); s.Document.Selection = Selections.Rectangle(16, 12, new(8, 0), new(16, 12));
    s.ApplyFilter(FilterKind.Invert);
    Equal((byte)255, s.Document.ActiveLayer!.Mask!.Get(2, 6).A); Equal((byte)0, s.Document.ActiveLayer.Mask.Get(12, 6).A);
});
Test("properties inversion intentionally operates on the whole mask", () =>
{
    var s = Create(); s.AddMask(); s.Document.Selection = new PixelSurface(16, 12); s.InvertMask();
    Equal((byte)0, s.Document.ActiveLayer!.Mask!.Get(2, 6).A);
});
Test("gamma processes coverage and preserves non-destructive mask parameters", () =>
{
    var s = Create(); s.AddMask(); s.Document.ActiveLayer!.Mask!.Fill(new Rgba32(10, 100, 220, 128));
    s.SetMaskProperties(.42f, 2.5f); s.ApplyFilter(FilterKind.Gamma, 2);
    Equal((byte)181, s.Document.ActiveLayer!.Mask!.Get(8, 6).A);
    Equal(.42f, s.Document.ActiveLayer.MaskDensity); Equal(2.5f, s.Document.ActiveLayer.MaskFeather);
});
Test("gamma identity never mistakes mask RGB for coverage", () =>
{
    var mask = new PixelSurface(4, 4); mask.Fill(new Rgba32(10, 200, 30, 137));
    var result = PixelFilterPipeline.Apply(mask, true, FilterKind.Gamma, 1);
    Equal((byte)137, result.Get(2, 2).A);
});
Test("mask filters reject invalid parameters before computing coverage", () =>
{
    var mask = new PixelSurface(4, 4);
    Throws(() => PixelFilterPipeline.Apply(mask, true, FilterKind.Invert, float.NaN));
    Throws(() => PixelFilterPipeline.Apply(mask, true, (FilterKind)999));
});
Test("fully hidden pixels participate in mask invert", () =>
{
    var result = PixelFilterPipeline.Apply(new PixelSurface(4, 4), true, FilterKind.Invert);
    Equal((byte)255, result.Get(2, 2).A);
});
Test("filter output cannot silently change target dimensions", () =>
{
    var s = Create(); s.AddMask();
    Throws(() => PixelTarget.Replace(s.Document, s.Document.ActiveLayer!, new PixelSurface(3, 3)));
});
Test("pixel edits ignore off-canvas content without truncation to pixel zero", () =>
{
    var s = Create(); s.Document.ActiveLayer!.X = -.75f; s.AddMask(); s.FillPixels(Rgba32.Black);
    Equal((byte)255, s.Document.ActiveLayer.Mask!.Get(0, 4).A); Equal((byte)0, s.Document.ActiveLayer.Mask.Get(1, 4).A);
});
Test("locked masks cannot be painted, inverted, cleared, filled, disabled or deleted", () =>
{
    var s = Create(); s.AddMask(); s.Document.ActiveLayer!.Locked = true;
    var history = s.History.Count; var bytes = s.Document.ActiveLayer.Mask!.ToRgba();
    s.InvertMask(); s.FillPixels(Rgba32.Black); s.SetMaskEnabled(false); s.DeleteMask(); s.ClearPixels(); s.SetMaskProperties(.4f, 2);
    var brush = new BrushEngine(); brush.Begin(s.Document.ActiveLayer.Mask, Vector2.Zero);
    brush.Paint(s.Document, s.Document.ActiveLayer, new(8, 6), 1, new(), Rgba32.Black, PaintMode.Brush); brush.End();
    Check(bytes.SequenceEqual(s.Document.ActiveLayer.Mask.ToRgba())); Equal(history, s.History.Count);
    Check(s.Document.ActiveLayer.MaskEnabled); Equal(1f, s.Document.ActiveLayer.MaskDensity);
});
Test("invalid mask density and feather do not start a transaction", () =>
{
    var s = Create(); s.AddMask(); var history = s.History.Count;
    Throws(() => s.SetMaskProperties(float.NaN, 0)); Throws(() => s.SetMaskProperties(1, 33));
    Throws(() => s.SetMaskProperties(-.1f, 0)); Throws(() => s.SetMaskProperties(1, float.PositiveInfinity));
    Equal(history, s.History.Count); Check(!s.IsInTransaction);
});
Test("unchanged mask properties do not add history entries", () =>
{
    var s = Create(); s.AddMask(); var history = s.History.Count;
    s.SetMaskProperties(1, 0); s.SetMaskEnabled(true); Equal(history, s.History.Count);
});
Test("undo redo preserve authored coverage and mask parameters", () =>
{
    var s = Create(); s.AddMask(); var bytes = s.Document.ActiveLayer!.Mask!.ToRgba();
    s.SetMaskProperties(.4f, 3); s.Undo(); Equal(1f, s.Document.ActiveLayer!.MaskDensity);
    s.Redo(); Equal(.4f, s.Document.ActiveLayer!.MaskDensity); Equal(3f, s.Document.ActiveLayer.MaskFeather);
    Check(bytes.SequenceEqual(s.Document.ActiveLayer.Mask!.ToRgba()));
});
Test("cancelled mask strokes restore exact coverage", () =>
{
    var s = Create(); s.AddMask(); var before = s.Document.ActiveLayer!.Mask!.ToRgba();
    var history = s.History.Count; s.Begin("Mask stroke"); Paint(s, PaintMode.Brush, Rgba32.Black); s.Cancel();
    Check(before.SequenceEqual(s.Document.ActiveLayer!.Mask!.ToRgba())); Equal(history, s.History.Count);
});
Test("native archive retains adjustment mask settings and authored pixels", () =>
{
    var s = Create(); s.AddMask(); s.SetMaskProperties(.35f, 2); s.SetMaskEnabled(false); s.InvertMask();
    var restored = DocumentArchive.Load(DocumentArchive.Save(s.Document));
    Equal(.35f, restored.ActiveLayer!.MaskDensity); Equal(2f, restored.ActiveLayer.MaskFeather);
    Check(!restored.ActiveLayer.MaskEnabled); Equal((byte)0, restored.ActiveLayer.Mask!.Get(8, 6).A);
});
Test("mask selection uses inverse layer transform", () =>
{
    var s = Create(); s.AddMask(); s.Document.ActiveLayer!.Mask!.Fill(Rgba32.Transparent);
    s.Document.ActiveLayer.Mask.Set(2, 4, Rgba32.White); s.Document.ActiveLayer.X = 4;
    s.LoadMaskSelection(); Equal((byte)255, s.Document.Selection!.Get(6, 4).A);
    Equal((byte)0, s.Document.Selection.Get(2, 4).A);
});
Test("mask selection does not dirty a saved document merely by switching edit channel", () =>
{
    var s = Create(); s.AddMask(); s.MarkSaved(); s.SelectLayer(s.Document.ActiveLayerId); s.SelectMask(s.Document.ActiveLayerId);
    Check(!s.IsDirty); Check(s.Document.EditMask);
});
Test("delete mask exits mask editing and undo restores it", () =>
{
    var s = Create(); s.AddMask(); s.SetMaskProperties(.25f, 3); s.DeleteMask();
    Check(!s.Document.EditMask && s.Document.ActiveLayer!.Mask is null);
    s.Undo(); Check(s.Document.EditMask && s.Document.ActiveLayer!.Mask is not null); Equal(.25f, s.Document.ActiveLayer!.MaskDensity);
});
Test("mask inspection maps alpha to opaque grayscale", () =>
{
    var s = Create(); s.AddMask(); s.Document.ActiveLayer!.Mask!.Fill(new Rgba32(30, 70, 250, 128));
    using var renderer = new ImageRenderer(); using var view = new MaskPreviewRenderer();
    var pixel = Preview(renderer, view, s.Document, MaskPreviewMode.Grayscale).Get(8, 6);
    Equal((byte)255, pixel.A); Check(Math.Abs(pixel.R - 128) <= 1); Equal(pixel.R, pixel.G); Equal(pixel.R, pixel.B);
});
Test("inspection composite mode neither builds filters nor changes visible pixels", () =>
{
    var s = Create(); s.AddMask(); using var renderer = new ImageRenderer(); using var view = new MaskPreviewRenderer();
    Equal(new Rgba32(80, 120, 160), Preview(renderer, view, s.Document, MaskPreviewMode.Composite).Get(8, 6));
    Equal(0L, view.FilterBuilds);
});
Test("red overlay marks hidden rather than revealed mask areas", () =>
{
    var s = Create(); s.AddMask(); s.Document.ActiveLayer!.Mask!.Set(4, 6, Rgba32.Transparent);
    using var renderer = new ImageRenderer(); using var view = new MaskPreviewRenderer();
    var pixels = Preview(renderer, view, s.Document, MaskPreviewMode.Overlay);
    Equal(new Rgba32(80, 120, 160), pixels.Get(8, 6)); Check(pixels.Get(4, 6).R > 150 && pixels.Get(4, 6).G < 100);
});
Test("inspection caches stable graphs without mutating authored pixels or history", () =>
{
    var s = Create(); s.AddMask(); var bytes = s.Document.ActiveLayer!.Mask!.ToRgba();
    var history = s.History.Count; using var renderer = new ImageRenderer(); using var view = new MaskPreviewRenderer();
    Preview(renderer, view, s.Document, MaskPreviewMode.Grayscale); var builds = view.FilterBuilds;
    Preview(renderer, view, s.Document, MaskPreviewMode.Overlay); Equal(builds, view.FilterBuilds);
    Equal(history, s.History.Count); Check(bytes.SequenceEqual(s.Document.ActiveLayer.Mask.ToRgba()));
    s.SetMaskProperties(.5f, 0); Preview(renderer, view, s.Document, MaskPreviewMode.Grayscale); Equal(builds + 1, view.FilterBuilds);
});
int ArchiveVersion(byte[] bytes)
{
    using var stream = new MemoryStream(bytes);
    using var zip = new System.IO.Compression.ZipArchive(stream);
    using var manifest = zip.GetEntry("manifest.json")!.Open();
    using var json = JsonDocument.Parse(manifest);
    return json.RootElement.GetProperty("version").GetInt32();
}
Test("masked adjustment requires version 3 for older-reader safety", () =>
{
    var s = Create(); s.AddMask(); Equal(3, ArchiveVersion(DocumentArchive.Save(s.Document)));
});
Test("non-default raster mask properties require version 3", () =>
{
    var s = Create(LayerKind.Raster); s.AddMask(); Equal(1, ArchiveVersion(DocumentArchive.Save(s.Document)));
    s.SetMaskProperties(.5f, 0); Equal(3, ArchiveVersion(DocumentArchive.Save(s.Document)));
    s.SetMaskProperties(1, 2); Equal(3, ArchiveVersion(DocumentArchive.Save(s.Document)));
});
Test("legacy unmasked and tone-only archive versions remain readable", () =>
{
    var s = Create(); var bytes = DocumentArchive.Save(s.Document); Equal(1, ArchiveVersion(bytes));
    Equal(AdjustmentKind.Invert, DocumentArchive.Load(bytes).ActiveLayer!.Adjustment);
    s.Document.ActiveLayer!.Adjustment = AdjustmentKind.Curves;
    bytes = DocumentArchive.Save(s.Document); Equal(2, ArchiveVersion(bytes));
    Equal(AdjustmentKind.Curves, DocumentArchive.Load(bytes).ActiveLayer!.Adjustment);
});
Test("fractional adjustment output crossfade requires version 3", () =>
{
    var s = Create(); s.Document.ActiveLayer!.Opacity = .5f;
    Equal(3, ArchiveVersion(DocumentArchive.Save(s.Document)));
});
var failed = 0;
var results = new List<object>();
foreach (var (name, body) in tests)
{
    try { body(); Console.WriteLine("PASS " + name); results.Add(new { name, passed = true }); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error); results.Add(new { name, passed = false, error = error.ToString() }); }
}
Directory.CreateDirectory("artifacts");
File.WriteAllText("artifacts/mask-editing-tests.json", JsonSerializer.Serialize(new
{
    total = tests.Count, passed = tests.Count - failed, failed, results
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{tests.Count - failed}/{tests.Count} mask editing tests passed");
return failed == 0 ? 0 : 1;
