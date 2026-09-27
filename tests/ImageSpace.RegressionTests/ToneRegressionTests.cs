using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json.Nodes;
using ImageSpace.Core;
using ImageSpace.Documents;
using ImageSpace.Editing;
using ImageSpace.Filters;
using ImageSpace.Skia;

internal static class ToneRegressionTests
{
    private static void Check(bool condition, string message = "Tone assertion failed")
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or System.Text.Json.JsonException) { return; }
        throw new InvalidOperationException("Invalid tonal data was accepted.");
    }
    private static ToneCurve Curve(params CurvePoint[] points) => new() { Points = points.ToImmutableArray() };
    private static readonly ToneCurve Negative = Curve(new(0, 255), new(255, 0));
    private static ImageDocument Document(AdjustmentKind kind = AdjustmentKind.Curves)
    {
        var document = new ImageDocument(256, 2, "Tone test");
        var raster = Layer.Raster("Input ramp", 256, 2);
        for (var x = 0; x < 256; x++)
        {
            raster.Pixels!.Set(x, 0, new Rgba32((byte)x, (byte)(255 - x), (byte)(x / 2)));
            raster.Pixels.Set(x, 1, new Rgba32((byte)x, (byte)(255 - x), (byte)(x / 2), 128));
        }
        var tone = new Layer { Name = kind.ToString(), Kind = LayerKind.Adjustment, Adjustment = kind, Width = 256, Height = 2 };
        document.Layers.AddRange([raster, tone]);
        document.ActiveLayerId = tone.Id;
        return document;
    }
    private static byte[] Rewrite(byte[] archive, Action<JsonObject> edit)
    {
        using var input = new MemoryStream(archive);
        using var original = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var entry in original.Entries)
            {
                using var source = entry.Open();
                using var destination = target.CreateEntry(entry.FullName).Open();
                if (entry.FullName == "manifest.json")
                {
                    var json = JsonNode.Parse(source)!.AsObject();
                    edit(json);
                    using var writer = new System.Text.Json.Utf8JsonWriter(destination);
                    json.WriteTo(writer);
                }
                else source.CopyTo(destination);
            }
        }
        return output.ToArray();
    }
    private static int Version(byte[] bytes)
    {
        using var memory = new MemoryStream(bytes);
        using var zip = new ZipArchive(memory, ZipArchiveMode.Read);
        using var stream = zip.GetEntry("manifest.json")!.Open();
        return JsonNode.Parse(stream)!["version"]!.GetValue<int>();
    }

    public static void Register(Action<string, Action> add)
    {
        add("curves: exact identity for all byte inputs", () => Check(ToneCurve.Identity.CreateLookup().Select((v, i) => v == i).All(v => v)));
        add("curves: negative endpoints map every input", () => Check(Negative.CreateLookup().Select((v, i) => v == 255 - i).All(v => v)));
        add("curves: interpolation passes through every knot", () =>
        {
            var curve = Curve(new(0, 10), new(64, 90), new(128, 170), new(192, 210), new(255, 240));
            var table = curve.CreateLookup();
            foreach (var point in curve.Points) Check(table[point.Input] == point.Output);
        });
        add("curves: seeded monotone ramps never overshoot", () =>
        {
            var random = new Random(8271);
            for (var test = 0; test < 100; test++)
            {
                var values = Enumerable.Range(0, 6).Select(_ => random.Next(256)).Order().ToArray();
                var curve = Curve(Enumerable.Range(0, 6).Select(i => new CurvePoint(i * 51, values[i])).ToArray());
                var table = curve.CreateLookup();
                Check(table.Zip(table.Skip(1)).All(pair => pair.First <= pair.Second));
                Check(table.All(value => value >= values[0] && value <= values[^1]));
            }
        });
        add("curves: nonmonotone curves preserve local extrema", () =>
        {
            var table = Curve(new(0, 20), new(80, 230), new(160, 30), new(255, 200)).CreateLookup();
            Check(table.Take(81).All(value => value is >= 20 and <= 230));
            Check(table.Skip(80).Take(81).All(value => value is >= 30 and <= 230));
            Check(table.Skip(160).All(value => value is >= 30 and <= 200));
        });
        add("curves: duplicate inputs rejected", () => Reject(() => Curve(new(0, 0), new(0, 40), new(255, 255)).Validate()));
        add("curves: missing endpoints rejected", () => Reject(() => Curve(new(1, 0), new(255, 255)).Validate()));
        add("curves: invalid output rejected", () => Reject(() => Curve(new(0, -1), new(255, 255)).Validate()));
        add("curves: point count bounded", () => Reject(() => Curve(Enumerable.Range(0, 17).Select(i => new CurvePoint(i * 15, i * 15)).ToArray()).Validate()));
        add("curves: default immutable array rejected", () => Reject(() => new ToneCurve { Points = default }.Validate()));
        add("curves: immutable points cannot alias a caller array", () =>
        {
            var points = new[] { new CurvePoint(0, 0), new CurvePoint(255, 255) };
            var curve = Curve(points);
            points[0] = new CurvePoint(0, 255);
            Check(curve.Points[0].Output == 0);
        });
        add("levels: exact identity", () => Check(new LevelsChannel().CreateLookup().Select((v, i) => v == i).All(v => v)));
        add("levels: input endpoints clip and normalize", () =>
        {
            var table = new LevelsChannel { InputBlack = 20, InputWhite = 200 }.CreateLookup();
            Check(table[0] == 0 && table[20] == 0 && table[110] == 128 && table[200] == 255 && table[255] == 255);
        });
        add("levels: gamma above one lightens midtones", () => Check(new LevelsChannel { Gamma = 1.4 }.CreateLookup()[128] > 128));
        add("levels: gamma below one darkens midtones", () => Check(new LevelsChannel { Gamma = 0.7 }.CreateLookup()[128] < 128));
        add("levels: output endpoints remap the range", () =>
        {
            var table = new LevelsChannel { OutputBlack = 30, OutputWhite = 230 }.CreateLookup();
            Check(table[0] == 30 && table[255] == 230);
        });
        add("levels: inverted output is explicitly supported", () => Check(new LevelsChannel { OutputBlack = 255, OutputWhite = 0 }.CreateLookup()[64] == 191));
        add("levels: nonfinite gamma rejected", () => Reject(() => new LevelsChannel { Gamma = double.NaN }.Validate()));
        add("levels: empty input range rejected", () => Reject(() => new LevelsChannel { InputBlack = 100, InputWhite = 100 }.Validate()));
        add("levels: negative input rejected", () => Reject(() => new LevelsChannel { InputBlack = -1 }.Validate()));
        add("levels: gamma bounded", () => Reject(() => new LevelsChannel { Gamma = 0 }.Validate()));
        add("tone lookup: channel first, composite second", () =>
        {
            var tables = RgbLookupTables.FromCurves(new CurvesAdjustment { Rgb = Negative, Red = Negative });
            Check(tables.Map(new Rgba32(40, 50, 60)) == new Rgba32(40, 205, 195));
        });
        add("tone lookup: zero strength is identity", () =>
        {
            var tables = RgbLookupTables.FromCurves(new CurvesAdjustment { Rgb = Negative }, 0);
            Check(tables.Map(new Rgba32(40, 50, 60, 80)) == new Rgba32(40, 50, 60, 80));
        });
        add("tone lookup: half negative is neutral", () =>
        {
            var value = RgbLookupTables.FromCurves(new CurvesAdjustment { Rgb = Negative }, .5f).Map(new Rgba32(10, 50, 210));
            Check(value.R == 128 && value.G == 128 && value.B == 128);
        });
        add("tone lookup: rejects incomplete tables", () => Reject(() => new RgbLookupTables(new byte[1], new byte[256], new byte[256])));
        add("tone lookup: rejects nonfinite strength", () => Reject(() => RgbLookupTables.FromCurves(new(), float.NaN)));
        add("tone lookup: alpha is preserved", () => Check(RgbLookupTables.FromCurves(new() { Rgb = Negative }).Map(new Rgba32(5, 10, 15, 64)).A == 64));
        add("tone lookup: transparent pixels remain transparent", () => Check(RgbLookupTables.FromCurves(new() { Rgb = Negative }).Map(Rgba32.Transparent) == Rgba32.Transparent));
        add("tone CPU: sparse edges and source immutability", () =>
        {
            var source = new PixelSurface(260, 129);
            source.Set(259, 128, new Rgba32(10, 20, 30, 99));
            var output = ToneFilterEngine.Apply(source, RgbLookupTables.FromCurves(new() { Rgb = Negative }));
            Check(output.AllocatedTiles == 1 && output.Get(259, 128) == new Rgba32(245, 235, 225, 99));
            Check(source.Get(259, 128) == new Rgba32(10, 20, 30, 99));
        });
        add("tone snapshots: settings remain isolated", () =>
        {
            var document = Document();
            var before = document.Snapshot();
            document.ActiveLayer!.Curves = new CurvesAdjustment { Rgb = Negative };
            Check(before.ActiveLayer!.Curves.Rgb.CreateLookup()[0] == 0);
            document.ActiveLayer.Levels = new LevelsAdjustment { Red = new() { Gamma = 2 } };
            Check(before.ActiveLayer.Levels.Red.Gamma == 1);
        });
        add("tone history: undo and redo restore exact settings", () =>
        {
            var session = new EditorSession(Document());
            session.Execute("Curves", document => document.ActiveLayer!.Curves = new() { Rgb = Negative });
            session.Undo();
            Check(session.Document.ActiveLayer!.Curves.Rgb.CreateLookup()[40] == 40);
            session.Redo();
            Check(session.Document.ActiveLayer!.Curves.Rgb.CreateLookup()[40] == 215);
        });
        add("tone history: invalid curve edit rolls back atomically", () =>
        {
            var session = new EditorSession(Document());
            Reject(() => session.Execute("Invalid", document => document.ActiveLayer!.Curves = new() { Rgb = Curve(new(0, 0), new(0, 255)) }));
            Check(!session.CanUndo && session.Document.ActiveLayer!.Curves.Rgb.Points.Length == 2);
        });
        add("tone archive: Curves requires version two and roundtrips points", () =>
        {
            var document = Document();
            document.ActiveLayer!.Curves = new() { Rgb = Curve(new(0, 10), new(88, 120), new(255, 245)), Blue = Negative };
            var bytes = DocumentArchive.Save(document);
            Check(Version(bytes) == 2);
            var restored = DocumentArchive.Load(bytes).ActiveLayer!;
            Check(restored.Curves.Rgb.Points.SequenceEqual(document.ActiveLayer.Curves.Rgb.Points));
            Check(restored.Curves.Blue.CreateLookup()[40] == 215);
        });
        add("tone archive: Levels retains independent RGB channels", () =>
        {
            var document = Document(AdjustmentKind.Levels);
            document.ActiveLayer!.Levels = new() { Rgb = new() { Gamma = 1.5, OutputBlack = 10 }, Red = new() { Gamma = 0.75 }, Blue = new() { InputWhite = 222 } };
            var result = DocumentArchive.Load(DocumentArchive.Save(document)).ActiveLayer!.Levels;
            Check(result.Rgb.Gamma == 1.5 && result.Red.Gamma == .75 && result.Blue.InputWhite == 222 && result.Rgb.OutputBlack == 10);
        });
        add("tone archive: legacy version one remains readable", () =>
        {
            var document = Document(AdjustmentKind.Invert);
            var bytes = DocumentArchive.Save(document);
            Check(Version(bytes) == 1);
            var legacy = Rewrite(bytes, json =>
            {
                foreach (var item in json["layers"]!.AsArray())
                {
                    var metadata = item!["metadata"]!.AsObject();
                    metadata.Remove("curves");
                    metadata.Remove("levels");
                }
            });
            Check(DocumentArchive.Load(legacy).ActiveLayer!.Curves.Rgb.CreateLookup()[80] == 80);
        });
        add("tone archive: future versions rejected", () => Reject(() => DocumentArchive.Load(Rewrite(DocumentArchive.Save(Document()), json => json["version"] = 999))));
        add("tone archive: duplicate curve input rejected on load", () => Reject(() => DocumentArchive.Load(Rewrite(DocumentArchive.Save(Document()), json =>
            json["layers"]![1]!["metadata"]!["curves"]!["rgb"]!["points"]![1]!["input"] = 0))));
        add("tone archive: unknown adjustment kind rejected", () => Reject(() => DocumentArchive.Load(Rewrite(DocumentArchive.Save(Document()), json =>
            json["layers"]![1]!["metadata"]!["adjustment"] = 999))));
        add("tone Skia: all opaque ramp samples match CPU within one byte", () =>
        {
            var document = Document();
            document.ActiveLayer!.Curves = new() { Rgb = Curve(new(0, 10), new(80, 130), new(255, 245)), Red = Negative };
            using var renderer = new ImageRenderer();
            var output = renderer.Rasterize(document);
            var tables = RgbLookupTables.FromCurves(document.ActiveLayer.Curves);
            for (var x = 0; x < 256; x++)
            {
                var expected = tables.Map(document.Layers[0].Pixels!.Get(x, 0));
                var actual = output.Get(x, 0);
                Check(Math.Abs(expected.R - actual.R) <= 1 && Math.Abs(expected.G - actual.G) <= 1 && Math.Abs(expected.B - actual.B) <= 1);
                Check(output.Get(x, 1).A == 128);
            }
        });
        add("tone Skia: Levels maps endpoints and gamma", () =>
        {
            var document = Document(AdjustmentKind.Levels);
            document.ActiveLayer!.Levels = new() { Rgb = new() { InputBlack = 20, InputWhite = 220, Gamma = 1.5 } };
            using var renderer = new ImageRenderer();
            var output = renderer.Rasterize(document);
            var tables = RgbLookupTables.FromLevels(document.ActiveLayer.Levels);
            for (var x = 0; x < 256; x++) Check(Math.Abs(output.Get(x, 0).R - tables.Map(document.Layers[0].Pixels!.Get(x, 0)).R) <= 1);
        });
        add("tone Skia: unchanged immutable settings reuse cached filters", () =>
        {
            var document = Document();
            using var renderer = new ImageRenderer();
            renderer.Rasterize(document);
            var before = renderer.ToneFilterBuilds;
            renderer.Rasterize(document);
            Check(renderer.ToneFilterBuilds == before);
            document.ActiveLayer!.Curves = new() { Rgb = Negative };
            renderer.Rasterize(document);
            Check(renderer.ToneFilterBuilds == before + 1);
        });
        add("tone Skia: opacity change invalidates cached table", () =>
        {
            var document = Document();
            document.ActiveLayer!.Curves = new() { Rgb = Negative };
            using var renderer = new ImageRenderer();
            renderer.Rasterize(document);
            document.ActiveLayer.Opacity = 0;
            Check(renderer.Rasterize(document).Get(80, 0) == document.Layers[0].Pixels!.Get(80, 0));
            Check(renderer.ToneFilterBuilds == 2);
        });
        add("tone Skia: layers above the adjustment remain unaffected", () =>
        {
            var document = Document();
            document.ActiveLayer!.Curves = new() { Rgb = Negative };
            var top = Layer.Raster("Above", 256, 2);
            top.Pixels!.Set(40, 0, new Rgba32(10, 20, 30));
            document.Layers.Add(top);
            using var renderer = new ImageRenderer();
            Check(renderer.Rasterize(document).Get(40, 0) == new Rgba32(10, 20, 30));
        });
        add("tone Skia: bounded preview preserves aspect ratio", () =>
        {
            var document = Document();
            using var renderer = new ImageRenderer();
            var preview = renderer.RasterizePreview(document, 64);
            Check(preview.Width == 64 && preview.Height == 1 && document.Width == 256 && document.Height == 2);
        });
        add("tone validation: invalid stroke widths rejected", () =>
        {
            var document = Document();
            document.Layers[0].StrokeWidth = float.NaN;
            Reject(document.Validate);
        });
    }
}
