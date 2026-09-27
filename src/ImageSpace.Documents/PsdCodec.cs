using System.Buffers.Binary;
using System.Text;
using ImageSpace.Core;
namespace ImageSpace.Documents;

/// <summary>Bounded PSD v1, RGB/8, raw/PackBits raster interoperability. Never claims lossless Photoshop roundtripping.</summary>
public static class PsdCodec
{
    public sealed record ImportResult(ImageDocument Document, IReadOnlyList<string> Warnings);
    private sealed record Channel(short Id, uint Length);
    private sealed record Record(int X, int Y, int Width, int Height, string Name, byte Opacity, bool Visible, LayerBlend Blend, List<Channel> Channels);
    public static byte[] Save(ImageDocument document, Func<Layer, PixelSurface> rasterize, PixelSurface composite)
    {
        document.Validate();
        if (composite.Width != document.Width || composite.Height != document.Height)
            throw new ArgumentException("Composite dimensions differ.");
        // Editable application-specific text, transforms and effects are rasterized into standard PSD pixel layers.
        var layers = document.Layers.Where(l => l.Kind != LayerKind.Adjustment).Reverse().Select(l => (Layer: l, Pixels: rasterize(l))).ToList();
        if (layers.Count > 128)
            throw new InvalidDataException("Too many PSD layers.");
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.ASCII, true);
        ASCII(writer, "8BPS");
        U16(writer, 1);
        writer.Write(new byte[6]);
        U16(writer, 4);
        U32(writer, (uint)document.Height);
        U32(writer, (uint)document.Width);
        U16(writer, 8);
        U16(writer, 3);
        U32(writer, 0);
        U32(writer, 0);
        using var info = new MemoryStream();
        using (var w = new BinaryWriter(info, Encoding.ASCII, true))
        {
            I16(w, (short)-layers.Count);
            foreach (var item in layers)
            {
                var layer = item.Layer;
                var pixels = item.Pixels;
                I32(w, 0);
                I32(w, 0);
                I32(w, pixels.Height);
                I32(w, pixels.Width);
                U16(w, 4);
                foreach (short channel in new short[] { 0, 1, 2, -1 })
                {
                    I16(w, channel);
                    U32(w, (uint)(2 + pixels.Width * pixels.Height));
                }
                ASCII(w, "8BIM");
                ASCII(w, BlendKey(layer.Blend));
                w.Write(Rgba32.Byte(layer.Opacity * 255));
                w.Write((byte)0);
                w.Write((byte)(layer.Visible ? 0 : 2));
                w.Write((byte)0);
                using var extra = new MemoryStream();
                using (var e = new BinaryWriter(extra, Encoding.ASCII, true))
                {
                    U32(e, 0);
                    U32(e, 0);
                    var name = Encoding.ASCII.GetBytes(layer.Name);
                    var count = Math.Min(name.Length, 255);
                    e.Write((byte)count);
                    e.Write(name, 0, count);
                    while (extra.Length % 4 != 0)
                        e.Write((byte)0);
                }
                U32(w, (uint)extra.Length);
                w.Write(extra.ToArray());
            }
            foreach (var item in layers)
            foreach (var channel in new[] { 0, 1, 2, 3 })
            {
                U16(w, 0);
                WritePlane(w, item.Pixels, channel);
            }
            if (info.Length % 2 != 0)
                w.Write((byte)0);
        }
        U32(writer, (uint)(info.Length + 8));
        U32(writer, (uint)info.Length);
        writer.Write(info.ToArray());
        U32(writer, 0);
        U16(writer, 0);
        for (var channel = 0; channel < 4; channel++)
            WritePlane(writer, composite, channel);
        if (output.Length > DocumentArchive.MaximumArchiveBytes)
            throw new InvalidDataException("PSD exceeds 128 MiB.");
        return output.ToArray();
    }
    public static ImportResult Load(byte[] bytes, string name = "Imported PSD")
    {
        if (bytes.Length > DocumentArchive.MaximumArchiveBytes)
            throw new InvalidDataException("PSD exceeds 128 MiB.");
        using var stream = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(stream, Encoding.ASCII, true);
        if (Text(reader, 4) != "8BPS" || ReadU16(reader) != 1)
            throw new InvalidDataException("Only Photoshop PSD version 1 is supported; PSB is not.");
        Skip(reader, 6);
        var channels = ReadU16(reader);
        var height = checked((int)ReadU32(reader));
        var width = checked((int)ReadU32(reader));
        PixelSurface.ValidateSize(width, height);
        var depth = ReadU16(reader);
        var mode = ReadU16(reader);
        if (depth != 8 || mode != 3 || channels < 3 || channels > 16)
            throw new InvalidDataException("PSD import currently supports 8-bit RGB only.");
        Skip(reader, ReadU32(reader));
        Skip(reader, ReadU32(reader));
        var layerMaskLength = ReadU32(reader);
        var layerMaskEnd = End(reader, layerMaskLength);
        var document = new ImageDocument(width, height, name);
        long expanded = 0;
        if (layerMaskLength > 0)
        {
            var layerInfoLength = ReadU32(reader);
            var layerInfoEnd = End(reader, layerInfoLength);
            if (layerInfoEnd > layerMaskEnd)
                throw new InvalidDataException("Invalid layer section.");
            if (layerInfoLength > 0)
            {
                var count = Math.Abs((int)ReadI16(reader));
                if (count > 128)
                    throw new InvalidDataException("Too many PSD layers.");
                var records = new List<Record>();
                for (var i = 0; i < count; i++)
                {
                    var top = ReadI32(reader);
                    var left = ReadI32(reader);
                    var bottom = ReadI32(reader);
                    var right = ReadI32(reader);
                    var w = checked(right - left);
                    var h = checked(bottom - top);
                    if (w < 0 || h < 0 || w > 8192 || h > 8192 || (long)w * h > PixelSurface.MaximumPixels)
                        throw new InvalidDataException("Unsupported PSD layer dimensions.");
                    expanded += (long)Math.Max(w, 1) * Math.Max(h, 1) * 4;
                    if (expanded > 384L * 1024 * 1024)
                        throw new InvalidDataException("PSD decoded layers exceed memory limits.");
                    var channelCount = ReadU16(reader);
                    if (channelCount > 16)
                        throw new InvalidDataException("Too many layer channels.");
                    var list = new List<Channel>();
                    for (var c = 0; c < channelCount; c++)
                        list.Add(new(ReadI16(reader), ReadU32(reader)));
                    if (Text(reader, 4) != "8BIM")
                        throw new InvalidDataException("Invalid layer signature.");
                    var blend = ParseBlend(Text(reader, 4));
                    var opacity = reader.ReadByte();
                    reader.ReadByte();
                    var flags = reader.ReadByte();
                    reader.ReadByte();
                    var extraEnd = End(reader, ReadU32(reader));
                    Skip(reader, ReadU32(reader));
                    Skip(reader, ReadU32(reader));
                    var length = reader.ReadByte();
                    var layerName = Text(reader, length);
                    if (stream.Position > extraEnd)
                        throw new InvalidDataException("Invalid layer name length.");
                    stream.Position = extraEnd;
                    records.Add(new(left, top, w, h, layerName, opacity, (flags & 2) == 0, blend, list));
                }
                foreach (var record in records)
                {
                    var planes = new Dictionary<short, byte[]>();
                    foreach (var channel in record.Channels)
                    {
                        var end = End(reader, channel.Length);
                        if (end > layerInfoEnd)
                            throw new InvalidDataException("Channel exceeds layer section.");
                        if (record.Width > 0 && record.Height > 0 && channel.Id >= -1 && channel.Id <= 2)
                            planes[channel.Id] = ReadPlane(reader, record.Width, record.Height);
                        if (stream.Position > end)
                            throw new InvalidDataException("Channel data exceeds declared length.");
                        stream.Position = end;
                    }
                    if (record.Width == 0 || record.Height == 0)
                        continue;
                    var pixels = new PixelSurface(record.Width, record.Height);
                    for (var y = 0; y < record.Height; y++)
                    for (var x = 0; x < record.Width; x++)
                    {
                        var index = y * record.Width + x;
                        pixels.Set(x, y, new(Get(0, 0), Get(1, 0), Get(2, 0), Get(-1, 255)));
                        byte Get(short key, byte fallback) => planes.TryGetValue(key, out var plane) ? plane[index] : fallback;
                    }
                    var layer = Layer.Raster(record.Name, record.Width, record.Height);
                    layer.X = record.X;
                    layer.Y = record.Y;
                    layer.Opacity = record.Opacity / 255f;
                    layer.Visible = record.Visible;
                    layer.Blend = record.Blend;
                    layer.Pixels = pixels;
                    document.Layers.Insert(0, layer);
                }
            }
            stream.Position = layerMaskEnd;
        }
        if (document.Layers.Count == 0)
        {
            var compression = ReadU16(reader);
            var planeSize = checked(width * height);
            var planes = new byte[Math.Min(channels, (ushort)4)][];
            if (compression == 0)
            {
                for (var c = 0; c < channels; c++)
                {
                    var plane = reader.ReadBytes(planeSize);
                    if (plane.Length != planeSize)
                        throw new EndOfStreamException();
                    if (c < 4)
                        planes[c] = plane;
                }
            }
            else if (compression == 1)
            {
                var lengths = new ushort[channels * height];
                for (var i = 0; i < lengths.Length; i++)
                    lengths[i] = ReadU16(reader);
                for (var c = 0; c < channels; c++)
                {
                    var plane = new byte[planeSize];
                    for (var y = 0; y < height; y++)
                        DecodeRow(reader.ReadBytes(lengths[c * height + y]), plane.AsSpan(y * width, width));
                    if (c < 4)
                        planes[c] = plane;
                }
            }
            else
                throw new InvalidDataException("PSD ZIP/prediction compression is not supported; save with compatibility enabled or use PNG.");
            var pixels = new PixelSurface(width, height);
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                pixels.Set(x, y, new(planes[0][i], planes[1][i], planes[2][i], channels >= 4 ? planes[3][i] : (byte)255));
            }
            var layer = Layer.Raster("Composite", width, height);
            layer.Pixels = pixels;
            document.Layers.Add(layer);
        }
        document.ActiveLayerId = document.Layers[^1].Id;
        document.Validate();
        return new(document, ["PSD raster interchange: text, vectors, smart objects, adjustment metadata, layer masks, groups and embedded profiles are not retained. Keep the original PSD; use .imagespace for editable roundtrips."]);
    }
    private static byte[] ReadPlane(BinaryReader reader, int width, int height)
    {
        var compression = ReadU16(reader);
        var data = new byte[checked(width * height)];
        if (compression == 0)
        {
            reader.BaseStream.ReadExactly(data);
            return data;
        }
        if (compression != 1)
            throw new InvalidDataException("PSD layer ZIP/prediction compression is not supported.");
        var lengths = new ushort[height];
        for (var y = 0; y < height; y++)
            lengths[y] = ReadU16(reader);
        for (var y = 0; y < height; y++)
            DecodeRow(reader.ReadBytes(lengths[y]), data.AsSpan(y * width, width));
        return data;
    }
    public static void DecodeRow(ReadOnlySpan<byte> packed, Span<byte> row)
    {
        var source = 0;
        var dest = 0;
        while (source < packed.Length)
        {
            var n = (sbyte)packed[source++];
            if (n >= 0)
            {
                var count = n + 1;
                if (source + count > packed.Length || dest + count > row.Length)
                    throw new InvalidDataException("Invalid PackBits literal.");
                packed.Slice(source, count).CopyTo(row[dest..]);
                source += count;
                dest += count;
            }
            else if (n != -128)
            {
                var count = 1 - n;
                if (source >= packed.Length || dest + count > row.Length)
                    throw new InvalidDataException("Invalid PackBits run.");
                row.Slice(dest, count).Fill(packed[source++]);
                dest += count;
            }
        }
        if (dest != row.Length)
            throw new InvalidDataException("Incomplete PackBits row.");
    }
    private static void WritePlane(BinaryWriter w, PixelSurface pixels, int channel)
    {
        var row = new byte[pixels.Width];
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                var c = pixels.Get(x, y);
                row[x] = channel switch
                {
                    0 => c.R,
                    1 => c.G,
                    2 => c.B,
                    _ => c.A
                };
            }
            w.Write(row);
        }
    }
    private static string BlendKey(LayerBlend b) => b switch { LayerBlend.Multiply => "mul ", LayerBlend.Screen => "scrn", LayerBlend.Overlay => "over", LayerBlend.Darken => "dark", LayerBlend.Lighten => "lite", LayerBlend.Difference => "diff", LayerBlend.Exclusion => "smud", LayerBlend.ColorDodge => "div ", LayerBlend.ColorBurn => "idiv", LayerBlend.HardLight => "hLit", LayerBlend.SoftLight => "sLit", LayerBlend.Hue => "hue ", LayerBlend.Saturation => "sat ", LayerBlend.Color => "colr", LayerBlend.Luminosity => "lum ", _ => "norm" };
    private static LayerBlend ParseBlend(string key) => Enum.GetValues<LayerBlend>().FirstOrDefault(x => BlendKey(x) == key);
    private static long End(BinaryReader r, uint length)
    {
        var end = checked(r.BaseStream.Position + length);
        if (end > r.BaseStream.Length)
            throw new InvalidDataException("PSD section exceeds file length.");
        return end;
    }
    private static void Skip(BinaryReader r, long count)
    {
        if (count < 0 || count > r.BaseStream.Length - r.BaseStream.Position)
            throw new InvalidDataException("Invalid PSD section length.");
        r.BaseStream.Position += count;
    }
    private static string Text(BinaryReader r, int count)
    {
        var b = r.ReadBytes(count);
        if (b.Length != count)
            throw new EndOfStreamException();
        return Encoding.ASCII.GetString(b);
    }
    private static void ASCII(BinaryWriter w, string text) => w.Write(Encoding.ASCII.GetBytes(text));
    private static ushort ReadU16(BinaryReader r)
    {
        Span<byte> b = stackalloc byte[2];
        r.BaseStream.ReadExactly(b);
        return BinaryPrimitives.ReadUInt16BigEndian(b);
    }
    private static short ReadI16(BinaryReader r) => unchecked((short)ReadU16(r));
    private static uint ReadU32(BinaryReader r)
    {
        Span<byte> b = stackalloc byte[4];
        r.BaseStream.ReadExactly(b);
        return BinaryPrimitives.ReadUInt32BigEndian(b);
    }
    private static int ReadI32(BinaryReader r) => unchecked((int)ReadU32(r));
    private static void U16(BinaryWriter w, ushort value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, value);
        w.Write(b);
    }
    private static void I16(BinaryWriter w, short value) => U16(w, unchecked((ushort)value));
    private static void U32(BinaryWriter w, uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        w.Write(b);
    }
    private static void I32(BinaryWriter w, int value) => U32(w, unchecked((uint)value));
}
