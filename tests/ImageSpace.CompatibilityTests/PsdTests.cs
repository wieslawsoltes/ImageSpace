using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using ImageSpace.Core;
using ImageSpace.Documents;
using ImageSpace.Editing;
using ImageSpace.Imaging;
using ImageSpace.Skia;

internal static partial class Program
{
    private static void RegisterPsdTests()
    {
        foreach (var compression in Enum.GetValues<PsdCompression>())
        {
            var code = compression;
            Test($"PSD: {code} export roundtrips RGBA and Unicode names", () =>
            {
                var pixels = Seed(129, 17);
                var layer = Layer.Raster("Zażółć gęślą · 東京 🎨", 129, 17);
                layer.Pixels = pixels;
                layer.Locked = true;
                layer.Opacity = .6f;
                layer.Blend = LayerBlend.Screen;
                var document = new ImageDocument(129, 17) { Dpi = 299.5, Layers = [layer], ActiveLayerId = layer.Id };
                var loaded = PsdCodec.Load(PsdCodec.Save(document, item => item.Pixels!, pixels, code)).Document;
                Check(loaded.Dpi == 299.5);
                Check(loaded.ActiveLayer!.Name == layer.Name);
                Check(loaded.ActiveLayer.Locked && loaded.ActiveLayer.Blend == LayerBlend.Screen);
                Check(Math.Abs(loaded.ActiveLayer.Opacity - .6f) < .001);
                Equal(pixels, loaded.ActiveLayer.Pixels!);
                Equal(pixels, layer.Pixels!);
            });
            Test($"PSD: independent {code} fixture imports placed user mask", () =>
            {
                var result = PsdCodec.Load(Fixture(code));
                var layer = result.Document.ActiveLayer!;
                Check(layer.X == 10 && layer.Y == 20 && layer.Name == "Mask Ω 🎨");
                Check(layer.Mask is not null && layer.MaskEnabled);
                Check(layer.Mask!.Get(0, 0).A == 255 && layer.Mask.Get(2, 1).A == 0 && layer.Mask.Get(3, 1).A == 128);
                Check(layer.Mask.Get(5, 3).A == 255);
                Check(layer.Pixels!.Get(0, 0) == new Rgba32(80, 120, 160, 128));
                Check(result.Warnings.All(warning => !warning.Contains("Unsupported PSD compression")));
            });
            Test($"PSD: independent {code} composite channels are not mistaken for transparency", () =>
            {
                var document = PsdCodec.Load(CompositeFixture(code)).Document;
                Check(document.ActiveLayer!.Pixels!.Get(2, 1) == new Rgba32(80, 120, 160, 255));
            });
        }
        Test("PSD: relative mask offset is not translated twice", () =>
        {
            var layer = PsdCodec.Load(Fixture(PsdCompression.Raw, flags: 1)).Document.ActiveLayer!;
            Check(layer.Mask!.Get(2, 1).A == 0 && layer.Mask.Get(3, 1).A == 128);
        });
        Test("PSD: disabled mask remains editable but is bypassed", () =>
        {
            var layer = PsdCodec.Load(Fixture(PsdCompression.Zip, flags: 2)).Document.ActiveLayer!;
            Check(!layer.MaskEnabled && layer.Mask!.Get(2, 1).A == 0);
        });
        Test("PSD: inverted mask changes coverage and default background", () =>
        {
            var layer = PsdCodec.Load(Fixture(PsdCompression.ZipPrediction, flags: 4)).Document.ActiveLayer!;
            Check(layer.Mask!.Get(0, 0).A == 0 && layer.Mask.Get(2, 1).A == 255 && layer.Mask.Get(3, 1).A == 127);
        });
        Test("PSD: declared mask density and feather survive native archive", () =>
        {
            var document = PsdCodec.Load(Fixture(PsdCompression.Raw, flags: 16)).Document;
            var layer = document.ActiveLayer!;
            Check(layer.MaskDensity == 128 / 255f && layer.MaskFeather == 2);
            var roundtrip = DocumentArchive.Load(DocumentArchive.Save(document));
            Check(roundtrip.ActiveLayer!.MaskDensity == layer.MaskDensity && roundtrip.ActiveLayer.MaskFeather == 2);
            Equal(layer.Mask!, roundtrip.ActiveLayer.Mask!);
        });
        Test("PSD: channel ZIP expansion beyond declared dimensions is rejected", () =>
            Throws<InvalidDataException>(() => PsdCodec.Load(Fixture(PsdCompression.Zip, overflow: true))));
        Test("PSD: duplicate channel IDs are rejected before decode", () =>
            Throws<InvalidDataException>(() => PsdCodec.Load(Fixture(PsdCompression.Raw, duplicate: true))));
        Test("PSD: nested mask section cannot escape layer extra data", () =>
        {
            var bytes = Fixture(PsdCompression.Raw);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(104, 4), uint.MaxValue);
            Throws<InvalidDataException>(() => PsdCodec.Load(bytes));
        });
        Test("PSD: invalid UTF16 name data is rejected", () =>
        {
            var bytes = Fixture(PsdCompression.Raw);
            var offset = Find(bytes, Encoding.ASCII.GetBytes("luni"));
            bytes[offset + 12] = 0xd8;
            bytes[offset + 13] = 0;
            Throws<DecoderFallbackException>(() => PsdCodec.Load(bytes));
        });
        Test("PSD: truncated header and nested sections are rejected", () =>
        {
            var bytes = Fixture(PsdCompression.Raw);
            foreach (var length in new[] { 0, 4, 12, 25, 34, 42, 74, 100, 120, 160, 210 })
                Throws<InvalidDataException>(() => PsdCodec.Load(bytes.AsSpan(0, length).ToArray()));
        });
        Test("PSD: more than 128 records is rejected", () =>
        {
            var bytes = Fixture(PsdCompression.Raw);
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(38), 129);
            Throws<InvalidDataException>(() => PsdCodec.Load(bytes));
        });
        Test("PSD: invalid export compression rejects before invoking rasterizer", () =>
        {
            var called = false;
            var doc = new ImageDocument(2, 2);
            Throws<ArgumentOutOfRangeException>(() => PsdCodec.Save(doc, _ => { called = true; return new(2, 2); }, new(2, 2), (PsdCompression)99));
            Check(!called);
        });
        Test("PSD: flat empty document export retains merged alpha", () =>
        {
            var pixels = new PixelSurface(2, 2);
            pixels.Set(1, 1, new(255, 100, 50, 64));
            var bytes = PsdCodec.Save(new ImageDocument(2, 2), _ => throw new Exception(), pixels);
            Equal(pixels, PsdCodec.Load(bytes).Document.ActiveLayer!.Pixels!);
        });
        Test("bulk: aliased row source has memmove semantics across tile boundaries", () =>
        {
            var surface = Seed();
            var borrowed = surface.EnumerateTiles().First(tile => tile.X == 0 && tile.Y == 0).Pixels.Span[..520];
            var expected = borrowed.ToArray();
            surface.WriteRow(1, 0, borrowed);
            var actual = new byte[520];
            surface.CopyRowTo(1, 0, actual);
            Check(actual.SequenceEqual(expected));
        });
        Test("native: streaming import handles a sliced archive without copying unrelated bytes", () =>
        {
            var doc = new ImageDocument(129, 17);
            var layer = Layer.Raster("one", 129, 17);
            layer.Pixels = Seed(129, 17);
            doc.Layers.Add(layer);
            var archive = DocumentArchive.Save(doc);
            var padded = new byte[archive.Length + 41];
            archive.CopyTo(padded, 19);
            Equal(layer.Pixels, DocumentArchive.Load(padded.AsMemory(19, archive.Length)).ActiveLayer!.Pixels!);
        });
        Test("selection modifiers are transactional and undoable", () =>
        {
            var doc = new ImageDocument(32, 16) { Selection = new PixelSurface(32, 16) };
            doc.Selection.Set(16, 8, Rgba32.White);
            var session = new EditorSession(doc);
            session.ModifySelection(SelectionModification.Expand, 3);
            Check(session.History.Count == 1 && session.Document.Selection!.Get(13, 8).A == 255);
            session.Undo();
            Check(session.Document.Selection!.Get(13, 8).A == 0);
            session.Redo();
            Check(session.Document.Selection!.Get(13, 8).A == 255);
            var count = session.History.Count;
            Throws<ArgumentOutOfRangeException>(() => session.ModifySelection(SelectionModification.Border, -1));
            Check(count == session.History.Count && !session.IsInTransaction);
        });
        Test("selection modifiers reject absent selection and cancelled edits", () =>
        {
            var session = new EditorSession(new ImageDocument(8, 8));
            Throws<InvalidOperationException>(() => session.ModifySelection(SelectionModification.Expand, 3));
            session.Document.Selection = new PixelSurface(8, 8);
            Throws<OperationCanceledException>(() => session.ModifySelection(SelectionModification.Expand, 3, new CancellationToken(true)));
            Check(!session.CanUndo);
        });
    }

    // Fixtures are independently laid out from the public PSD specification. They do not call
    // PsdCodec.Save or its channel encoder, so reader/writer bugs cannot cancel one another.
    private static byte[] Fixture(PsdCompression code, byte flags = 0, bool overflow = false, bool duplicate = false)
    {
        var planeData = new List<byte[]>();
        foreach (var color in new byte[] { 80, 120, 160, 128 })
            planeData.Add(EncodeFixture(Enumerable.Repeat(color, overflow ? 25 : 24).ToArray(), 6, 4, code));
        planeData.Add(EncodeFixture([0, 128, 255, 64], 2, 2, code));
        using var info = new MemoryStream();
        using var w = new BinaryWriter(info);
        B16(w, -1);
        B32(w, 20);
        B32(w, 10);
        B32(w, 24);
        B32(w, 16);
        B16(w, 5);
        for (var c = 0; c < 5; c++)
        {
            B16(w, c == 4 ? -2 : c == 3 ? -1 : duplicate && c == 1 ? 0 : c);
            B32(w, planeData[c].Length);
        }
        Tag(w, "8BIMnorm");
        w.Write((byte)255);
        w.Write(new byte[3]);
        using var extra = new MemoryStream();
        using var e = new BinaryWriter(extra);
        var parameterMask = (flags & 16) != 0;
        B32(e, parameterMask ? 46 : 20);
        var relative = (flags & 1) != 0;
        B32(e, relative ? 1 : 21);
        B32(e, relative ? 2 : 12);
        B32(e, relative ? 3 : 23);
        B32(e, relative ? 4 : 14);
        e.Write((byte)255);
        e.Write(flags);
        if (parameterMask)
        {
            e.Write((byte)3);
            e.Write((byte)128);
            Span<byte> real = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(real, BitConverter.DoubleToInt64Bits(2));
            e.Write(real);
            e.Write(new byte[18]);
        }
        else
            B16(e, 0);
        B32(e, 0);
        e.Write(new byte[] { 1, (byte)'M', 0, 0 });
        Tag(e, "8BIMluni");
        var name = "Mask Ω 🎨";
        var unicode = Encoding.BigEndianUnicode.GetBytes(name);
        B32(e, unicode.Length + 4);
        B32(e, name.Length);
        e.Write(unicode);
        B32(w, (int)extra.Length);
        w.Write(extra.ToArray());
        foreach (var plane in planeData)
            w.Write(plane);
        if ((info.Length & 1) != 0)
            w.Write((byte)0);
        using var output = new MemoryStream();
        using var result = new BinaryWriter(output);
        Header(result, 32, 32, 4);
        B32(result, (int)info.Length + 8);
        B32(result, (int)info.Length);
        result.Write(info.ToArray());
        B32(result, 0);
        B16(result, 0);
        result.Write(new byte[32 * 32 * 4]);
        return output.ToArray();
    }
    private static byte[] CompositeFixture(PsdCompression code)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        Header(writer, 3, 2, 4);
        B32(writer, 0);
        var source = new byte[24];
        source.AsSpan(0, 6).Fill(80);
        source.AsSpan(6, 6).Fill(120);
        source.AsSpan(12, 6).Fill(160);
        writer.Write(EncodeFixture(source, 3, 8, code));
        return output.ToArray();
    }
    private static byte[] EncodeFixture(byte[] source, int width, int rows, PsdCompression code)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        B16(writer, (int)code);
        if (code == PsdCompression.Raw)
            writer.Write(source);
        else if (code == PsdCompression.PackBits)
        {
            for (var y = 0; y < rows; y++)
                B16(writer, width + 1);
            for (var y = 0; y < rows; y++)
            {
                writer.Write((byte)(width - 1));
                writer.Write(source, y * width, width);
            }
        }
        else
        {
            var delta = (byte[])source.Clone();
            if (code == PsdCompression.ZipPrediction)
                for (var y = 0; y < rows; y++)
                for (var x = 1; x < width; x++)
                    delta[y * width + x] = unchecked((byte)(source[y * width + x] - source[y * width + x - 1]));
            using (var zip = new ZLibStream(output, CompressionLevel.SmallestSize, true))
                zip.Write(delta);
        }
        return output.ToArray();
    }
    private static void Header(BinaryWriter writer, int width, int height, int channels)
    {
        Tag(writer, "8BPS");
        B16(writer, 1);
        writer.Write(new byte[6]);
        B16(writer, channels);
        B32(writer, height);
        B32(writer, width);
        B16(writer, 8);
        B16(writer, 3);
        B32(writer, 0);
        B32(writer, 0);
    }
    private static void B16(BinaryWriter w, int value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, unchecked((short)value));
        w.Write(bytes);
    }
    private static void B32(BinaryWriter w, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        w.Write(bytes);
    }
    private static void Tag(BinaryWriter w, string value) => w.Write(Encoding.ASCII.GetBytes(value));
    private static int Find(byte[] source, byte[] target) => source.AsSpan().IndexOf(target);
}
