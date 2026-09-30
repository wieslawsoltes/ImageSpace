using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImageSpace.Core;
using ImageSpace.Documents;
using ImageSpace.Editing;
using ImageSpace.Skia;

var tests = new List<(string Name, Action Body)>();
void Test(string name, Action body) => tests.Add((name, body));
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new InvalidOperationException(message); }
void Throws(Action action)
{
    try { action(); }
    catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException) { return; }
    throw new Exception("Expected a rejected operation.");
}
void EqualPixels(PixelSurface a, PixelSurface b) => Check(a.ToRgba().SequenceEqual(b.ToRgba()), "RGBA byte mismatch.");
ImageDocument Document(AdjustmentKind kind)
{
    var source = Layer.Raster("Source", 256, 1);
    for (var x = 0; x < 256; x++) source.Pixels!.Set(x, 0, new Rgba32((byte)x, (byte)(255 - x), (byte)((x * 71) % 256), 255));
    var effect = new Layer { Kind = LayerKind.Adjustment, Adjustment = kind, Width = 256, Height = 1 };
    return new ImageDocument(256, 1) { Layers = [source, effect], ActiveLayerId = effect.Id };
}
int Version(byte[] bytes)
{
    using var memory = new MemoryStream(bytes);
    using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
    using var json = JsonDocument.Parse(zip.GetEntry("manifest.json")!.Open());
    return json.RootElement.GetProperty("version").GetInt32();
}
byte[] RewriteManifest(byte[] bytes, Action<JsonObject> change)
{
    using var memory = new MemoryStream(); memory.Write(bytes); memory.Position = 0;
    using (var zip = new ZipArchive(memory, ZipArchiveMode.Update, true))
    {
        var entry = zip.GetEntry("manifest.json")!;
        JsonObject node;
        using (var stream = entry.Open()) node = JsonNode.Parse(stream)!.AsObject();
        change(node); entry.Delete();
        using var output = zip.CreateEntry("manifest.json").Open();
        JsonSerializer.Serialize(output, node);
    }
    return memory.ToArray();
}

Test("settings identity, matrix ownership and unclamped negative source contributions", () =>
{
    var identity = new ChannelMixerAdjustment();
    for (var i = 0; i < 256; i++)
    {
        var color = new Rgba32((byte)i, (byte)(255 - i), 71, (byte)i);
        Check(identity.Transform(color) == color);
        Check(new ExposureAdjustment().Transform(color) == color);
    }
    var m = identity.CreateMatrix(); m[0] = 2;
    Check(identity.CreateMatrix()[0] == 1);
    var mixed = identity with { Red = new ChannelMix(200, -100, 0) };
    Check(mixed.Transform(new(200, 200, 30)).R == 200, "Contributions were clamped before being summed.");
});

foreach (var output in Enum.GetValues<ToneChannel>())
foreach (var coefficient in new double[] { -200, -100, 0, 34, 100, 200 })
{
    var channel = output; var value = coefficient;
    Test($"channel {channel} coefficient {value} and monochrome isolation", () =>
    {
        var settings = new ChannelMixerAdjustment().WithChannel(channel, new ChannelMix(value, 100 - Math.Clamp(value, -100, 100), 0));
        settings.Validate();
        var copy = settings with { Monochrome = true };
        Check((copy with { Monochrome = false }) == settings);
        var color = new Rgba32(17, 91, 203, 73);
        var gray = copy.Transform(color);
        Check(gray.R == gray.G && gray.G == gray.B && gray.A == 73);
        Check(settings.GetChannel(channel).Red == value);
    });
}
foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -200.001, 200.001 })
{
    var invalid = value;
    Test("invalid mixer input " + value, () =>
    {
        Throws(() => new ChannelMix(invalid).Validate());
        Throws(() => new ChannelMix(0, invalid).Validate());
        Throws(() => new ChannelMix(0, 0, invalid).Validate());
        Throws(() => new ChannelMix(0, 0, 0, invalid).Validate());
    });
}
Test("missing channels and invalid channel enum are rejected", () =>
{
    Throws(() => (new ChannelMixerAdjustment { Gray = null! }).Validate());
    Throws(() => new ChannelMixerAdjustment().GetChannel((ToneChannel)99));
    Throws(() => new ChannelMixerAdjustment().WithChannel(ToneChannel.Red, null!));
});

foreach (var ev in new double[] { -20, -2, -1, 0, 1, 2, 20 })
foreach (var offset in new double[] { -.5, 0, .5 })
foreach (var gamma in new double[] { .01, 1, 9.99 })
{
    var exposure = new ExposureAdjustment { Exposure = ev, Offset = offset, Gamma = gamma };
    Test($"exposure LUT/reference/monotonicity {ev}, {offset}, {gamma}", () =>
    {
        var table = exposure.CreateLookup();
        for (var i = 0; i < table.Length; i++)
        {
            var output = exposure.Transform(new((byte)i, (byte)i, (byte)i, 73));
            Check(output.R == table[i] && output.G == table[i] && output.B == table[i] && output.A == 73);
            if (i != 0) Check(table[i] >= table[i - 1]);
        }
        table[0] ^= 255;
        Check(exposure.CreateLookup()[0] != table[0]);
    });
}
Test("exposure operates in linear rather than encoded sRGB space", () =>
{
    var table = new ExposureAdjustment { Exposure = 1 }.CreateLookup();
    Check(table[128] is >= 175 and <= 176, "One stop must not double the encoded sRGB byte.");
    Check(new ExposureAdjustment { Offset = -.5 }.CreateLookup()[128] == 0);
    Check(new ExposureAdjustment { Gamma = 2 }.CreateLookup()[128] > 128);
});
Test("exposure validation and null setting payloads", () =>
{
    foreach (var invalid in new ExposureAdjustment[] { new() { Exposure = 20.01 }, new() { Exposure = double.NaN },
        new() { Offset = .501 }, new() { Gamma = 0 }, new() { Gamma = 10 } }) Throws(invalid.Validate);
    var document = Document(AdjustmentKind.Exposure); document.ActiveLayer!.Exposure = null!;
    Throws(document.Validate);
});

var mixers = new[]
{
    new ChannelMixerAdjustment(),
    new ChannelMixerAdjustment { Red = new(0, 0, 100), Blue = new(100, 0, 0) },
    new ChannelMixerAdjustment { Red = new(200, -100, 0, 25), Green = new(-20, 130, -10, -5) },
    new ChannelMixerAdjustment { Monochrome = true, Gray = new(-70, 200, -30) },
    new ChannelMixerAdjustment { Monochrome = true, Gray = new(34, 66, 0) }
};
foreach (var mixer in mixers)
{
    var settings = mixer;
    Test("Skia matrix agrees with scalar RGB reference " + settings, () =>
    {
        var document = Document(AdjustmentKind.ChannelMixer); document.ActiveLayer!.ChannelMixer = settings;
        using var renderer = new ImageRenderer();
        var output = renderer.Rasterize(document);
        for (var x = 0; x < 256; x++)
        {
            var expected = settings.Transform(document.Layers[0].Pixels!.Get(x, 0)); var actual = output.Get(x, 0);
            Check(Math.Abs(expected.R - actual.R) <= 1 && Math.Abs(expected.G - actual.G) <= 1 && Math.Abs(expected.B - actual.B) <= 1 && actual.A == 255,
                $"Mixer mismatch at {x}: {expected} versus {actual}");
        }
    });
}
foreach (var ev in new double[] { -2, 0, 1, 3 })
{
    var stops = ev;
    Test("Skia exposure table agrees at " + stops + " stops", () =>
    {
        var document = Document(AdjustmentKind.Exposure);
        document.ActiveLayer!.Exposure = new() { Exposure = stops, Offset = .02, Gamma = 1.1 };
        using var renderer = new ImageRenderer(); var output = renderer.Rasterize(document);
        for (var x = 0; x < 256; x++)
        {
            var expected = document.ActiveLayer.Exposure.Transform(document.Layers[0].Pixels!.Get(x, 0));
            var actual = output.Get(x, 0);
            Check(Math.Abs(expected.R - actual.R) <= 1 && Math.Abs(expected.G - actual.G) <= 1 && Math.Abs(expected.B - actual.B) <= 1 && actual.A == 255);
        }
    });
}
foreach (var kind in new[] { AdjustmentKind.ChannelMixer, AdjustmentKind.Exposure })
foreach (var alpha in new byte[] { 0, 1, 17, 64, 128, 255 })
foreach (var opacity in new float[] { 0, .5f, 1 })
{
    var type = kind; var a = alpha; var strength = opacity;
    Test($"{type} preserves alpha {a}, opacity {strength}, mask and clipping", () =>
    {
        var document = Document(type); document.Layers[0].Pixels!.Fill(new Rgba32(54, 145, 230, a));
        var effect = document.ActiveLayer!; effect.Opacity = strength;
        effect.Exposure = new() { Exposure = 1 };
        effect.ChannelMixer = new() { Monochrome = true, Gray = new(0, 100, 0) };
        effect.Mask = new PixelSurface(256, 1);
        for (var x = 128; x < 256; x++) effect.Mask.Set(x, 0, Rgba32.White);
        using var renderer = new ImageRenderer();
        var unfiltered = renderer.RasterizeLayer(document, document.Layers[0]);
        var output = renderer.Rasterize(document);
        for (var x = 0; x < 256; x++)
        {
            Check(Math.Abs(output.Get(x, 0).A - a) <= 1);
            if (x < 128 || strength == 0) Check(output.Get(x, 0) == unfiltered.Get(x, 0));
        }
        effect.IsClipped = true; document.Validate();
        var clipped = renderer.Rasterize(document);
        for (var x = 0; x < 256; x++) Check(Math.Abs(clipped.Get(x, 0).A - a) <= 1);
    });
}

foreach (var kind in new[] { AdjustmentKind.ChannelMixer, AdjustmentKind.Exposure })
{
    var type = kind;
    Test(type + " cache, undo ownership, native v6 and render stamps", () =>
    {
        var session = new EditorSession(Document(type)); var effect = session.Document.ActiveLayer!;
        var originalSource = session.Document.Layers[0].Pixels!.Snapshot();
        using var renderer = new ImageRenderer(); using var preview = new DocumentPreviewCache();
        renderer.Rasterize(session.Document); var builds = renderer.ColorAdjustmentFilterBuilds;
        var stamp = LayerRenderStamp.Capture(effect);
        for (var i = 0; i < 10; i++) renderer.Rasterize(session.Document);
        Check(renderer.ColorAdjustmentFilterBuilds == builds);
        effect.Name = "Rename only"; effect.Locked = true;
        Check(stamp == LayerRenderStamp.Capture(effect)); effect.Locked = false;
        preview.Get(session.Document, 2, 192, renderer); var previews = preview.Builds;
        using (var gesture = session.BeginColorAdjustmentEdit())
        {
            for (var i = 0; i < 100; i++)
            {
                if (type == AdjustmentKind.Exposure) gesture.Set(new ExposureAdjustment { Exposure = i / 100.0 });
                else gesture.Set(new ChannelMixerAdjustment { Red = new(100, 0, 0, i / 100.0) });
            }
            Check(session.History.Count == 0);
            gesture.Commit();
        }
        Check(session.History.Count == 1);
        Check(stamp != LayerRenderStamp.Capture(effect));
        renderer.Rasterize(session.Document); Check(renderer.ColorAdjustmentFilterBuilds == builds + 1);
        preview.Get(session.Document, 2, 192, renderer); Check(preview.Builds == previews + 1);
        var settings = effect.ChannelMixer; var exposure = effect.Exposure;
        var encoded = DocumentArchive.Save(session.Document); Check(Version(encoded) == 6);
        var loaded = DocumentArchive.Load(encoded);
        Check(loaded.ActiveLayer!.ChannelMixer == settings && loaded.ActiveLayer.Exposure == exposure);
        EqualPixels(renderer.Rasterize(session.Document), renderer.Rasterize(loaded));
        EqualPixels(originalSource, loaded.Layers[0].Pixels!);
        session.Undo(); Check(session.Document.ActiveLayer!.ChannelMixer == new ChannelMixerAdjustment());
        Check(session.Document.ActiveLayer.Exposure == new ExposureAdjustment());
        session.Redo(); Check(session.Document.ActiveLayer!.ChannelMixer == settings && session.Document.ActiveLayer.Exposure == exposure);
        var count = renderer.ColorAdjustmentFilterBuilds; effect.Opacity = .5f; renderer.Rasterize(loaded);
        Check(renderer.ColorAdjustmentFilterBuilds == count);
        renderer.Prune(new ImageDocument(1, 1)); Check(renderer.CachedColorAdjustments == 0);
        Throws(() => DocumentArchive.Load(RewriteManifest(encoded, node => node["version"] = 5)));
    });
}
Test("gesture cancellation and stale ownership never affect replacement transactions", () =>
{
    var session = new EditorSession(Document(AdjustmentKind.Exposure));
    var before = DocumentArchive.Save(session.Document);
    var gesture = session.BeginColorAdjustmentEdit(); gesture.Set(new ExposureAdjustment { Exposure = 2 });
    session.Undo(); Check(!gesture.IsActive && session.History.Count == 0);
    session.Begin("Another gesture");
    gesture.Commit(); gesture.Dispose();
    Check(session.IsInTransaction, "A stale gesture canceled another owner's transaction.");
    Throws(() => gesture.Set(new ExposureAdjustment { Exposure = 3 }));
    session.Cancel();
    Check(session.Document.ActiveLayer!.Exposure == new ExposureAdjustment());
    using (var canceled = session.BeginColorAdjustmentEdit()) canceled.Set(new ExposureAdjustment { Exposure = 5 });
    Check(session.History.Count == 0 && !session.IsInTransaction && !session.IsDirty);
    using (var same = session.BeginColorAdjustmentEdit()) same.Commit();
    Check(session.History.Count == 0 && !session.IsDirty);
});
Test("commands reject locks/channel/invalid settings without opening history", () =>
{
    var session = new EditorSession(Document(AdjustmentKind.ChannelMixer));
    session.SetChannelMixer(new()); Check(session.History.Count == 0);
    Throws(() => session.SetChannelMixer(new() { Red = new(201) }));
    session.Document.ActiveLayer!.Locked = true;
    Throws(() => session.SetChannelMixer(new() { Monochrome = true }));
    Throws(() => session.BeginColorAdjustmentEdit());
    session.Document.ActiveLayer.Locked = false; session.Document.EditMask = true;
    Throws(() => session.SetChannelMixer(new() { Monochrome = true }));
    Check(session.History.Count == 0 && !session.IsInTransaction);
});
Test("adding above a clipping chain preserves relationships and uses an editable selection mask", () =>
{
    var source = Layer.Raster("Base", 4, 4);
    var child = Layer.Raster("Clipped", 4, 4); child.IsClipped = true;
    var doc = new ImageDocument(4, 4) { Layers = [source, child], ActiveLayerId = source.Id, Selection = new PixelSurface(4, 4) };
    doc.Selection.Set(2, 2, Rgba32.White);
    var session = new EditorSession(doc); var layer = session.AddColorAdjustment(AdjustmentKind.Exposure);
    Check(session.Document.Layers[2] == layer && child.IsClipped && !layer.IsClipped);
    Check(layer.Mask is not null && layer.Mask.Get(2, 2).A == 255 && layer.Mask.Get(0, 0).A == 0);
    Check(session.History.Count == 1 && !doc.EditMask);
    session.Undo(); Check(session.Document.Layers.Count == 2 && session.Document.ActiveLayerId == source.Id);
    session.Redo(); session.Document.Validate();
});
Test("old manifests still use minimal version and absent settings default to identity", () =>
{
    var layer = Layer.Raster("Plain", 1, 1);
    var doc = new ImageDocument(1, 1) { Layers = [layer], ActiveLayerId = layer.Id };
    var encoded = DocumentArchive.Save(doc); Check(Version(encoded) == 1);
    var old = RewriteManifest(encoded, node =>
    {
        var metadata = node["layers"]![0]!["metadata"]!.AsObject();
        metadata.Remove("channelMixer"); metadata.Remove("exposure");
    });
    var loaded = DocumentArchive.Load(old);
    Check(loaded.ActiveLayer!.ChannelMixer == new ChannelMixerAdjustment());
    Check(loaded.ActiveLayer.Exposure == new ExposureAdjustment());
    layer.ChannelMixer = new() { Gray = new(30, 59, 11) };
    Check(Version(DocumentArchive.Save(doc)) == 6, "Inactive authored settings were not version guarded.");
    Throws(() => DocumentArchive.Load(RewriteManifest(encoded, node => node["version"] = 999)));
});

var failures = 0; var results = new List<object>();
foreach (var (name, body) in tests)
{
    try { body(); results.Add(new { name, passed = true }); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}\n{error}"); results.Add(new { name, passed = false, error = error.ToString() }); }
}
Directory.CreateDirectory("artifacts");
File.WriteAllText("artifacts/color-adjustment-tests.json", JsonSerializer.Serialize(new
{
    total = tests.Count, passed = tests.Count - failures, failed = failures, results
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"COLOR_ADJUSTMENT_TESTS total={tests.Count} passed={tests.Count - failures} failed={failures}");
return failures == 0 ? 0 : 1;
