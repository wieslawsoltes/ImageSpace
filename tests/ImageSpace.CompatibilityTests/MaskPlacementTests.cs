using System.Numerics;
using System.IO.Compression;
using System.Text.Json.Nodes;
using ImageSpace.Core;
using ImageSpace.Documents;
using ImageSpace.Editing;
using ImageSpace.Imaging;
using ImageSpace.Skia;
using SkiaSharp;

internal static partial class Program
{
    private static ImageDocument PlacementDocument(int width = 32, int height = 16)
    {
        var layer = Layer.Raster("Pixels", width, height);
        layer.Pixels!.Fill(new Rgba32(80, 120, 160));
        layer.Mask = new PixelSurface(width, height);
        for (var y = 0; y < height; y++)
            for (var x = width / 2; x < width; x++)
                layer.Mask.Set(x, y, Rgba32.White);
        return new ImageDocument(width, height) { Layers = [layer], ActiveLayerId = layer.Id };
    }

    private static void NearPoint(Vector2 actual, Vector2 expected, float tolerance = .002f) =>
        Check(Vector2.Distance(actual, expected) <= tolerance, $"Expected {expected}; got {actual}");

    private static void NearMatrix(Matrix3x2 actual, Matrix3x2 expected)
    {
        foreach (var p in new[] { Vector2.Zero, Vector2.One, new Vector2(127, 63) })
            NearPoint(Vector2.Transform(p, actual), Vector2.Transform(p, expected));
    }

    private static byte[] RewritePlacementVersion(byte[] archive, int version)
    {
        using var source = new MemoryStream(archive);
        using var input = new ZipArchive(source, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var entry in input.Entries)
            {
                using var from = entry.Open();
                using var to = zip.CreateEntry(entry.FullName).Open();
                if (entry.FullName != "manifest.json")
                    from.CopyTo(to);
                else
                {
                    var json = JsonNode.Parse(from)!;
                    json["version"] = version;
                    using var writer = new StreamWriter(to, leaveOpen: true);
                    writer.Write(json.ToJsonString());
                }
            }
        return output.ToArray();
    }

    private static void RegisterPlacementTests()
    {
        foreach (var scale in new[] { new Vector2(1, 1), new Vector2(2, .5f), new Vector2(-1.5f, 2) })
        {
            var value = scale;
            Test($"placement: relinking retains rotated {value} world coordinates", () =>
            {
                var d = PlacementDocument();
                var l = d.ActiveLayer!;
                l.X = 71;
                l.Y = -9;
                l.ScaleX = value.X;
                l.ScaleY = value.Y;
                l.Rotation = 37;
                l.MaskPlacement = new AffinePlacement(1, .1f, .2f, 1, 3, 4);
                var matrix = l.MaskDocumentTransform;
                var bytes = l.Mask!.ToRgba();
                MaskGeometry.SetLinked(l, false);
                NearMatrix(l.MaskDocumentTransform, matrix);
                MaskGeometry.SetLinked(l, true);
                NearMatrix(l.MaskDocumentTransform, matrix);
                Check(bytes.SequenceEqual(l.Mask.ToRgba()));
            });
        }
        Test("placement: linked movement moves content and mask together", () =>
        {
            var s = new EditorSession(PlacementDocument());
            s.Document.EditMask = true;
            s.NudgeActiveTarget(4, 2);
            Check(s.Document.ActiveLayer!.X == 4);
            NearPoint(Vector2.Transform(Vector2.Zero, s.Document.ActiveLayer.MaskDocumentTransform), new(4, 2));
        });
        Test("placement: unlinked content movement leaves mask in document space", () =>
        {
            var s = new EditorSession(PlacementDocument());
            s.SetMaskLinked(false);
            var before = s.Document.ActiveLayer!.MaskDocumentTransform;
            s.NudgeActiveTarget(4, 2);
            Check(s.Document.ActiveLayer.X == 4);
            NearMatrix(s.Document.ActiveLayer.MaskDocumentTransform, before);
        });
        Test("placement: unlinked mask movement does not move content", () =>
        {
            var s = new EditorSession(PlacementDocument());
            s.SetMaskLinked(false);
            s.Document.EditMask = true;
            s.NudgeActiveTarget(4, 2);
            Check(s.Document.ActiveLayer!.X == 0);
            NearPoint(Vector2.Transform(Vector2.Zero, s.Document.ActiveLayer.MaskDocumentTransform), new(4, 2));
        });
        Test("placement: link and transform undo redo are exact metadata transactions", () =>
        {
            var s = new EditorSession(PlacementDocument());
            s.SetMaskLinked(false);
            s.SetMaskDocumentPlacement(new AffinePlacement(1, .2f, .1f, 1, 5, 6));
            s.MarkSaved();
            var expected = s.Document.ActiveLayer!.MaskPlacement;
            s.Undo();
            Check(s.Document.ActiveLayer!.MaskPlacement != expected && s.IsDirty);
            s.Redo();
            Check(s.Document.ActiveLayer!.MaskPlacement == expected && !s.IsDirty);
            s.Undo();
            s.Undo();
            Check(s.Document.ActiveLayer!.MaskLinked);
        });
        Test("placement: locked layer blocks all placement commands", () =>
        {
            var s = new EditorSession(PlacementDocument());
            s.Document.ActiveLayer!.Locked = true;
            s.SetMaskLinked(false);
            s.NudgeActiveTarget(3, 4);
            s.SetMaskDocumentPlacement(new AffinePlacement(X: 8));
            s.AlignMaskToLayer();
            Check(s.History.Count == 0 && s.Document.ActiveLayer.MaskLinked);
        });
        Test("placement: no-op commands do not create history", () =>
        {
            var s = new EditorSession(PlacementDocument());
            s.SetMaskLinked(true);
            s.AlignMaskToLayer();
            s.NudgeActiveTarget(0, 0);
            Check(s.History.Count == 0);
        });
        Test("placement: linked independent setter fails without opening a transaction", () =>
        {
            var s = new EditorSession(PlacementDocument());
            Throws<InvalidOperationException>(() => s.SetMaskDocumentPlacement(new AffinePlacement(X: 4)));
            Check(!s.IsInTransaction && s.History.Count == 0);
        });
        foreach (var invalid in new[] {
            new AffinePlacement(M11: float.NaN), new AffinePlacement(M12: float.PositiveInfinity),
            new AffinePlacement(M11: 0), new AffinePlacement(M11: 1,M12: 1,M21: 1,M22: 1),
            new AffinePlacement(X: 1_000_001), new AffinePlacement(M22: 10001)
        })
        {
            var placement = invalid;
            Test($"placement: rejects invalid matrix {placement}", () =>
            {
                Throws<InvalidDataException>(placement.Validate);
                var d = PlacementDocument();
                d.ActiveLayer!.MaskPlacement = placement;
                Throws<InvalidDataException>(d.Validate);
            });
        }
        Test("placement: null placement rejected", () =>
        {
            var d = PlacementDocument();
            d.ActiveLayer!.MaskPlacement = null!;
            Throws<InvalidDataException>(d.Validate);
        });
        Test("placement: delete and re-add resets link and authored placement", () =>
        {
            var s = new EditorSession(PlacementDocument());
            s.SetMaskLinked(false);
            s.SetMaskDocumentPlacement(new AffinePlacement(X: 6));
            s.DeleteMask();
            s.AddMask();
            Check(s.Document.ActiveLayer!.MaskLinked && s.Document.ActiveLayer.MaskPlacement == AffinePlacement.Identity);
        });
        Test("placement: duplicate retains world placement but independent metadata", () =>
        {
            var s = new EditorSession(PlacementDocument());
            s.SetMaskLinked(false);
            s.SetMaskDocumentPlacement(new AffinePlacement(X: 3));
            s.DuplicateLayer();
            s.SetMaskDocumentPlacement(new AffinePlacement(X: 9));
            Check(s.Document.Layers[0].MaskPlacement.X == 3 && s.Document.Layers[1].MaskPlacement.X == 9);
        });
        Test("placement: native version four roundtrips every affine component", () =>
        {
            var s = new EditorSession(PlacementDocument());
            s.SetMaskLinked(false);
            var matrix = new AffinePlacement(.8f, .2f, -.1f, 1.4f, 3, 7);
            s.SetMaskDocumentPlacement(matrix);
            var bytes = DocumentArchive.Save(s.Document);
            using (var z = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
                Check(JsonNode.Parse(z.GetEntry("manifest.json")!.Open())!["version"]!.GetValue<int>() == 4);
            var restored = DocumentArchive.Load(bytes);
            Check(!restored.ActiveLayer!.MaskLinked && restored.ActiveLayer.MaskPlacement == matrix);
            Equal(s.Document.ActiveLayer!.Mask!, restored.ActiveLayer.Mask!);
        });
        Test("placement: nonidentity linked placement also requires version four", () =>
        {
            var d = PlacementDocument();
            d.ActiveLayer!.MaskPlacement = new AffinePlacement(X: 3);
            var bytes = DocumentArchive.Save(d);
            Throws<InvalidDataException>(() => DocumentArchive.Load(RewritePlacementVersion(bytes, 3)));
            NearMatrix(DocumentArchive.Load(bytes).ActiveLayer!.MaskDocumentTransform, d.ActiveLayer.MaskDocumentTransform);
        });
        Test("placement: unlinked metadata in an older archive is rejected", () =>
        {
            var s = new EditorSession(PlacementDocument());
            s.SetMaskLinked(false);
            var bytes = DocumentArchive.Save(s.Document);
            foreach (var version in new[] { 1, 2, 3 })
                Throws<InvalidDataException>(() => DocumentArchive.Load(RewritePlacementVersion(bytes, version)));
        });
        Test("placement: crop translates unlinked mask with the canvas", () =>
        {
            var s = new EditorSession(PlacementDocument());
            s.SetMaskLinked(false);
            s.Crop(4, 2, 20, 10);
            NearPoint(Vector2.Transform(Vector2.Zero, s.Document.ActiveLayer!.MaskDocumentTransform), new(-4, -2));
        });
        Test("placement: image sizing transforms the unlinked mask frame", () =>
        {
            var s = new EditorSession(PlacementDocument());
            s.SetMaskLinked(false);
            s.SetMaskDocumentPlacement(new AffinePlacement(X: 4, Y: 2));
            s.ResizeImage(64, 8);
            NearMatrix(s.Document.ActiveLayer!.MaskDocumentTransform, new Matrix3x2(2, 0, 0, .5f, 8, 1));
        });
        foreach (var clockwise in new[] { false, true })
        {
            var value = clockwise;
            Test($"placement: canvas quarter-turn follows unlinked masks {value}", () =>
            {
                var s = new EditorSession(PlacementDocument());
                s.SetMaskLinked(false);
                s.RotateCanvas(value);
                var expected = value ? new Matrix3x2(0, 1, -1, 0, 16, 0) : new Matrix3x2(0, -1, 1, 0, 0, 32);
                NearMatrix(s.Document.ActiveLayer!.MaskDocumentTransform, expected);
            });
        }
        foreach (var handle in new[] {new Vector2(0,0),new Vector2(.5f,0),new Vector2(1,0),new Vector2(1,.5f),
            new Vector2(1,1),new Vector2(.5f,1),new Vector2(0,1),new Vector2(0,.5f)})
        {
            var h = handle;
            Test($"placement: affine resize anchors opposite handle {h}", () =>
            {
                var old = new Matrix3x2(-1.2f, .4f, .3f, 1.8f, 70, 15);
                var dim = new Vector2(32, 16);
                var opposite = (Vector2.One - h) * dim;
                var point = Vector2.Transform((h - Vector2.One / 2) * dim * 1.4f + dim / 2, old);
                var result = MaskGeometry.Resize(old, dim, h, point, false);
                NearPoint(Vector2.Transform(opposite, result), Vector2.Transform(opposite, old));
                Check(Math.Sign(result.GetDeterminant()) == Math.Sign(old.GetDeterminant()));
            });
        }
        Test("placement: uniform side-handle resize can shrink", () =>
        {
            var result = MaskGeometry.Resize(Matrix3x2.Identity, new Vector2(32, 16),
                new Vector2(1, .5f), new Vector2(16, 8), true);
            Check(Math.Abs(result.M11 - .5f) < .001 && Math.Abs(result.M22 - .5f) < .001);
            NearPoint(Vector2.Transform(new Vector2(0, 8), result), new Vector2(0, 8));
        });
        Test("placement: invalid nudge rolls back the complete transaction", () =>
        {
            var session = new EditorSession(PlacementDocument());
            session.SetMaskLinked(false);
            session.Document.EditMask = true;
            var before = session.Document.ActiveLayer!.MaskPlacement;
            Throws<InvalidDataException>(() => session.NudgeActiveTarget(1_000_001, 0));
            Check(!session.IsInTransaction && session.Document.ActiveLayer!.MaskPlacement == before);
        });
        Test("placement: detached mask metadata remains a readable native roundtrip", () =>
        {
            var document = PlacementDocument();
            MaskGeometry.SetLinked(document.ActiveLayer!, false);
            document.ActiveLayer!.MaskPlacement = new AffinePlacement(X: 5);
            document.ActiveLayer.Mask = null;
            var result = DocumentArchive.Load(DocumentArchive.Save(document));
            Check(result.ActiveLayer!.Mask is null && !result.ActiveLayer.MaskLinked &&
                result.ActiveLayer.MaskPlacement.X == 5);
        });
        Test("placement: affine rotation preserves center and basis lengths", () =>
        {
            var old = new Matrix3x2(1, .3f, .2f, 2, 9, 12);
            var dim = new Vector2(32, 16);
            var center = Vector2.Transform(dim / 2, old);
            var next = MaskGeometry.Rotate(old, dim, center + new Vector2(30, 0), center + new Vector2(0, 30), false);
            NearPoint(Vector2.Transform(dim / 2, next), center);
            Check(Math.Abs(new Vector2(next.M11, next.M12).Length() - new Vector2(old.M11, old.M12).Length()) < .001);
        });
        Test("placement: prepared coverage matches pixel-center reference through affine frames", () =>
        {
            var d = PlacementDocument(259, 131);
            d.Selection = Seed(259, 131);
            var l = d.ActiveLayer!;
            l.X = -4;
            l.Y = 6;
            l.Rotation = 19;
            l.ScaleX = .8f;
            l.ScaleY = 1.2f;
            foreach (var mask in new[] { false, true })
            {
                d.EditMask = mask;
                if (mask)
                {
                    MaskGeometry.SetLinked(l, false);
                    l.MaskPlacement = new AffinePlacement(1, .1f, -.2f, 1, 3, 5);
                }
                var mapping = PixelTarget.Prepare(d, l);
                for (var y = 0; y < 131; y++)
                for (var x = 0; x < 259; x++)
                {
                    var p = Vector2.Transform(new Vector2(x + .5f, y + .5f), mask ? l.MaskDocumentTransform : l.Transform);
                    var expected = p.X < 0 || p.Y < 0 || p.X >= d.Width || p.Y >= d.Height ? 0 : d.Coverage((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y));
                    Check(mapping.Coverage(x, y) == expected);
                }
            }
        });
        Test("placement: brush resolves an unlinked mask in document space", () =>
        {
            var d = PlacementDocument();
            var l = d.ActiveLayer!;
            d.EditMask = true;
            MaskGeometry.SetLinked(l, false);
            l.MaskPlacement = new AffinePlacement(X: 4, Y: 2);
            l.Mask!.Fill(Rgba32.White);
            var brush = new BrushEngine();
            brush.Begin(l.Mask, Vector2.Zero);
            brush.Paint(d, l, new Vector2(10.5f, 6.5f), 1, new BrushSettings(Size: 1, Hardness: 1, Opacity: 1, Flow: 1, Pressure: false), Rgba32.Black, PaintMode.Brush);
            brush.End();
            Check(l.Mask.Get(6, 4).A == 0 && l.Mask.Get(10, 6).A == 255);
        });
        Test("placement: fill clear and filter use translated selection coverage", () =>
        {
            var d = PlacementDocument();
            var l = d.ActiveLayer!;
            d.EditMask = true;
            MaskGeometry.SetLinked(l, false);
            l.MaskPlacement = new AffinePlacement(X: 4, Y: 2);
            l.Mask!.Fill(Rgba32.White);
            d.Selection = Selections.Rectangle(32, 16, new Vector2(9, 5), new Vector2(11, 7));
            PixelEdits.Clear(d, l);
            Check(l.Mask.Get(5, 3).A == 0 && l.Mask.Get(9, 5).A == 255);
            PixelEdits.Fill(d, l, Rgba32.White);
            Check(l.Mask.Get(5, 3).A == 255);
            new EditorSession(d).ApplyFilter(ImageSpace.Filters.FilterKind.Invert);
            Check(l.Mask.Get(5, 3).A == 0 && l.Mask.Get(9, 5).A == 255);
        });
        Test("placement: loaded mask selection follows its independent frame", () =>
        {
            var d = PlacementDocument();
            var l = d.ActiveLayer!;
            MaskGeometry.SetLinked(l, false);
            l.MaskPlacement = new AffinePlacement(X: -8);
            var selection = MaskOperations.ToDocumentSelection(d, l);
            Check(selection.Get(4, 8).A == 0 && selection.Get(12, 8).A == 255 && selection.Get(27, 8).A == 0);
        });
        Test("placement: raster mask is independently translated in the compositor", () =>
        {
            var d = PlacementDocument();
            var l = d.ActiveLayer!;
            MaskGeometry.SetLinked(l, false);
            l.MaskPlacement = new AffinePlacement(X: -8);
            using var renderer = new ImageRenderer();
            var p = renderer.Rasterize(d);
            Check(p.Get(4, 8).A == 0 && p.Get(12, 8).A == 255 && p.Get(27, 8).A == 0);
            l.X = 4;
            p = renderer.Rasterize(d);
            Check(p.Get(6, 8).A == 0 && p.Get(12, 8).A == 255 && p.Get(27, 8).A == 0);
        });
        Test("placement: translated adjustment mask affects only its placed coverage", () =>
        {
            var d = PlacementDocument();
            var l = d.ActiveLayer!;
            var adjustment = new Layer { Name = "Invert", Kind = LayerKind.Adjustment, Adjustment = AdjustmentKind.Invert, Width = 32, Height = 16, Mask = l.Mask };
            l.Mask = null;
            d.Layers.Add(adjustment);
            MaskGeometry.SetLinked(adjustment, false);
            adjustment.MaskPlacement = new AffinePlacement(X: -8);
            using var renderer = new ImageRenderer();
            var p = renderer.Rasterize(d);
            Check(p.Get(4, 8) == new Rgba32(80, 120, 160) && p.Get(12, 8) == new Rgba32(175, 135, 95));
        });
        Test("placement: relink preserves actual rendered pixels", () =>
        {
            var d = PlacementDocument();
            var l = d.ActiveLayer!;
            l.X = 3;
            l.ScaleX = 1.25f;
            l.Rotation = 20;
            MaskGeometry.SetLinked(l, false);
            l.MaskPlacement = new AffinePlacement(X: 4, Y: 1);
            using var renderer = new ImageRenderer();
            var before = renderer.Rasterize(d);
            MaskGeometry.SetLinked(l, true);
            Equal(before, renderer.Rasterize(d));
        });
        Test("placement: density and movement reuse authored mask graph", () =>
        {
            var d = PlacementDocument();
            var l = d.ActiveLayer!;
            MaskGeometry.SetLinked(l, false);
            using var renderer = new ImageRenderer();
            renderer.Rasterize(d);
            var source = renderer.MaskSourceBuilds;
            var graph = renderer.MaskFilterBuilds;
            for (var i = 0; i < 5; i++)
            {
                l.MaskPlacement = new AffinePlacement(X: i + 1);
                l.MaskDensity = .7f;
                renderer.Rasterize(d);
            }
            Check(renderer.MaskSourceBuilds == source && renderer.MaskFilterBuilds > graph);
            l.MaskFeather = 1;
            renderer.Rasterize(d);
            Check(renderer.MaskSourceBuilds == source + 1);
            l.Mask!.Set(18, 8, Rgba32.Transparent);
            renderer.Rasterize(d);
            Check(renderer.MaskSourceBuilds == source + 2);
        });
    }
}
