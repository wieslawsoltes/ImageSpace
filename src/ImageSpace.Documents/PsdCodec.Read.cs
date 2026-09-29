using System.Text;
using ImageSpace.Core;

namespace ImageSpace.Documents;

public static partial class PsdCodec
{
    private sealed record Channel(short Id, uint Length);
    private sealed record MaskRecord(int X, int Y, int Width, int Height, byte Default, byte Flags, float Density, float Feather);
    private sealed record LayerRecord(int X, int Y, int Width, int Height, string Name, byte Opacity,
        bool Visible, bool Locked, bool Clipped, LayerBlend Blend, List<Channel> Channels, MaskRecord? Mask);

    public static ImportResult Load(byte[] bytes, string name = "Imported PSD")
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.LongLength > DocumentArchive.MaximumArchiveBytes)
            throw new InvalidDataException("PSD exceeds 128 MiB.");
        var reader = new PsdReader(bytes);
        if (reader.Text(4) != "8BPS" || reader.UInt16() != 1)
            throw new InvalidDataException("Only PSD version 1 is supported; PSB is not.");
        reader.Take(6);
        var channels = reader.UInt16();
        var height = checked((int)reader.UInt32());
        var width = checked((int)reader.UInt32());
        PixelSurface.ValidateSize(width, height);
        if (reader.UInt16() != 8 || reader.UInt16() != 3 || channels is < 3 or > 16)
            throw new InvalidDataException("PSD import supports eight-bit RGB documents only.");
        reader.Section(); // RGB color-mode data has no palette to retain.
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var document = new ImageDocument(width, height, name);
        ReadResources(reader.Section(), document, warnings);
        var layerMask = reader.Section();
        var mergedTransparency = false;
        long expanded = 0;
        if (layerMask.Remaining > 0)
        {
            var info = layerMask.Section();
            if (info.Remaining > 0)
            {
                var signedCount = info.Int16();
                mergedTransparency = signedCount < 0;
                var count = Math.Abs((int)signedCount);
                if (count > 128)
                    throw new InvalidDataException("Too many PSD layers.");
                var records = new List<LayerRecord>(count);
                for (var i = 0; i < count; i++)
                    records.Add(ReadLayerRecord(info, warnings));
                foreach (var record in records)
                {
                    var planes = new Dictionary<short, byte[]>();
                    foreach (var channel in record.Channels)
                    {
                        var data = info.Section(channel.Length);
                        var isMask = channel.Id == -2 && record.Mask is not null;
                        var w = isMask ? record.Mask!.Width : record.Width;
                        var h = isMask ? record.Mask!.Height : record.Height;
                        if ((channel.Id is >= -1 and <= 2 || isMask) && w > 0 && h > 0)
                        {
                            Reserve(ref expanded, (long)w * h);
                            planes.Add(channel.Id, PsdChannelCodec.Decode(data, w, h, 1)[0]);
                        }
                        else if (channel.Id == -3)
                            warnings.Add("Combined vector/real-user mask metadata is not retained; the rendered user mask is used when present.");
                    }
                    if (record.Width == 0 || record.Height == 0)
                        continue;
                    Reserve(ref expanded, (long)record.Width * record.Height * (record.Mask is null ? 4 : 8));
                    var layer = Layer.Raster(record.Name, record.Width, record.Height);
                    layer.Pixels = Assemble(planes, record.Width, record.Height);
                    layer.X = record.X;
                    layer.Y = record.Y;
                    layer.Opacity = record.Opacity / 255f;
                    layer.Visible = record.Visible;
                    layer.Locked = record.Locked;
                    layer.Blend = record.Blend;
                    layer.IsClipped = record.Clipped;
                    if (record.Mask is { } mask)
                    {
                        layer.Mask = PlaceMask(record, mask, planes.GetValueOrDefault((short)-2));
                        layer.MaskEnabled = (mask.Flags & 2) == 0;
                        layer.MaskDensity = mask.Density;
                        layer.MaskFeather = mask.Feather;
                    }
                    document.Layers.Insert(0, layer);
                }
                info.PaddingOnly();
            }
        }
        if (document.Layers.Count == 0)
        {
            Reserve(ref expanded, (long)width * height * (channels + 4));
            var channelsData = PsdChannelCodec.Decode(reader, width, height, channels);
            var planes = new Dictionary<short, byte[]> { [0] = channelsData[0], [1] = channelsData[1], [2] = channelsData[2] };
            if (mergedTransparency && channels > 3)
                planes[-1] = channelsData[3];
            else if (channels > 3)
                warnings.Add("Extra composite channels without the merged-transparency marker are not interpreted as image alpha.");
            var layer = Layer.Raster("Composite", width, height);
            layer.Pixels = Assemble(planes, width, height);
            document.Layers.Add(layer);
        }
        // Pixel layers are authoritative for this importer. Photoshop's merged preview is not
        // silently substituted for its editable raster layers when unsupported metadata exists.
        document.ActiveLayerId = document.Layers[^1].Id;
        document.Validate();
        warnings.Add("PSD raster interchange is not lossless Photoshop roundtripping. Keep the original PSD; save .imagespace for editable application state. Type, paths, smart objects, effects and unsupported adjustment/group metadata are not preserved.");
        return new(document, warnings.Order(StringComparer.Ordinal).ToArray());
    }

    private static LayerRecord ReadLayerRecord(PsdReader reader, HashSet<string> warnings)
    {
        var top = reader.Int32();
        var left = reader.Int32();
        var bottom = reader.Int32();
        var right = reader.Int32();
        var width = checked(right - left);
        var height = checked(bottom - top);
        ValidateRectangle(left, top, width, height);
        var count = reader.UInt16();
        if (count > 16)
            throw new InvalidDataException("Too many layer channels.");
        var channels = new List<Channel>(count);
        var ids = new HashSet<short>();
        for (var c = 0; c < count; c++)
        {
            var id = reader.Int16();
            var length = reader.UInt32();
            if (!ids.Add(id))
                throw new InvalidDataException("Duplicate PSD layer channel.");
            channels.Add(new(id, length));
        }
        if (reader.Text(4) != "8BIM")
            throw new InvalidDataException("Invalid layer signature.");
        var blendKey = reader.Text(4);
        var blend = ParseBlend(blendKey);
        if (BlendKey(blend) != blendKey)
            warnings.Add($"Unsupported PSD blend mode '{blendKey}' was imported as Normal.");
        var opacity = reader.Byte();
        var clipping = reader.Byte();
        var flags = reader.Byte();
        reader.Byte();
        if (clipping > 1)
            throw new InvalidDataException("Invalid PSD clipping flag.");
        var extra = reader.Section();
        var mask = ReadMask(extra.Section(), warnings);
        var ranges = extra.Section();
        if (ranges.Remaining > 0)
        {
            var value = ranges.Rest().Span;
            for (var i = 0; i < value.Length; i++)
                if (value[i] != (i % 4 < 2 ? 0 : 255))
                {
                    warnings.Add("Non-default Blend If ranges are not retained.");
                    break;
                }
        }
        var nameLength = extra.Byte();
        var layerName = extra.Text(nameLength);
        extra.Take((4 - (nameLength + 1) % 4) % 4);
        var locked = false;
        while (extra.Remaining >= 12)
        {
            var signature = extra.Text(4);
            if (signature is not ("8BIM" or "8B64"))
                throw new InvalidDataException("Invalid additional layer signature.");
            var key = extra.Text(4);
            var length = extra.UInt32();
            var data = extra.Section(length);
            if (key == "luni")
            {
                var characters = data.UInt32();
                if (characters > 1024)
                    throw new InvalidDataException("PSD layer name exceeds 1024 UTF-16 units.");
                layerName = new UnicodeEncoding(true, false, true).GetString(data.Take(checked((int)characters * 2)).Span).TrimEnd('\0');
            }
            else if (key == "lspf")
                locked = (data.UInt32() & 0x80000000u) != 0;
            else if (key == "clbl" && data.Byte() == 0)
                warnings.Add("The non-default Blend Clipped Layers As Group option is not retained; clipping uses grouped base blending.");
            else if (key is "lsct" or "lsdk")
                warnings.Add("Layer groups are imported as flat raster layers; group blend/isolation semantics are not retained.");
            else if (key is "vmsk" or "vsms" or "SoLd" or "SoLE" or "TySh" or "lrFX" or "lfx2" or "curv" or "levl" or "brit" or "hue2")
                warnings.Add($"Photoshop layer metadata '{key}' is not retained as editable content.");
            if ((length & 1) != 0)
                extra.Take(1);
        }
        extra.PaddingOnly();
        return new(left, top, width, height, layerName, opacity, (flags & 2) == 0, locked, clipping == 1, blend, channels, mask);
    }

    private static MaskRecord? ReadMask(PsdReader reader, HashSet<string> warnings)
    {
        if (reader.Remaining == 0)
            return null;
        var top = reader.Int32();
        var left = reader.Int32();
        var bottom = reader.Int32();
        var right = reader.Int32();
        var width = checked(right - left);
        var height = checked(bottom - top);
        ValidateRectangle(left, top, width, height);
        var background = reader.Byte();
        var flags = reader.Byte();
        if (background is not (0 or 255))
            throw new InvalidDataException("Invalid PSD mask default coverage.");
        var density = 1f;
        var feather = 0f;
        if ((flags & 16) != 0)
        {
            var parameters = reader.Byte();
            if ((parameters & 1) != 0)
                density = reader.Byte() / 255f;
            if ((parameters & 2) != 0)
            {
                var value = reader.Double();
                if (!double.IsFinite(value) || value is < 0 or > 32)
                    throw new InvalidDataException("PSD mask feather exceeds the supported 0–32 pixel range.");
                feather = (float)value;
                if (feather != 0)
                    warnings.Add("Imported mask feather uses ImageSpace's Gaussian interpretation; retain the original for Photoshop-specific edge behavior.");
            }
            if ((parameters & 4) != 0)
            {
                reader.Byte();
                warnings.Add("Vector-mask density is not retained.");
            }
            if ((parameters & 8) != 0)
            {
                reader.Double();
                warnings.Add("Vector-mask feather is not retained.");
            }
        }
        return new(left, top, width, height, background, flags, density, feather);
    }

    private static PixelSurface PlaceMask(LayerRecord layer, MaskRecord mask, byte[]? coverage)
    {
        var result = new PixelSurface(layer.Width, layer.Height);
        var inverted = (mask.Flags & 4) != 0;
        var background = inverted ? (byte)(255 - mask.Default) : mask.Default;
        result.Fill(new(255, 255, 255, background));
        if (coverage is null)
            return result;
        var offsetX = (long)mask.X - ((mask.Flags & 1) != 0 ? 0 : layer.X);
        var offsetY = (long)mask.Y - ((mask.Flags & 1) != 0 ? 0 : layer.Y);
        var x0 = (int)Math.Clamp(offsetX, 0, layer.Width);
        var x1 = (int)Math.Clamp(offsetX + mask.Width, 0, layer.Width);
        var row = new byte[Math.Max(0, x1 - x0) * 4];
        if (x0 == x1)
            return result;
        for (var y = (int)Math.Clamp(offsetY, 0, layer.Height); y < Math.Min(offsetY + mask.Height, layer.Height); y++)
        {
            for (var x = x0; x < x1; x++)
            {
                var a = coverage[checked((int)((y - offsetY) * mask.Width + x - offsetX))];
                var i = (x - x0) * 4;
                row[i] = row[i + 1] = row[i + 2] = 255;
                row[i + 3] = inverted ? (byte)(255 - a) : a;
            }
            result.WriteRow(x0, y, row);
        }
        return result;
    }

    private static PixelSurface Assemble(IReadOnlyDictionary<short, byte[]> planes, int width, int height)
    {
        if (!planes.TryGetValue(0, out var r) || !planes.TryGetValue(1, out var g) || !planes.TryGetValue(2, out var b))
            throw new InvalidDataException("RGB pixel layer is missing a color channel.");
        planes.TryGetValue(-1, out var a);
        var result = new PixelSurface(width, height);
        var row = new byte[width * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = y * width + x;
                var i = x * 4;
                var alpha = a is null ? (byte)255 : a[p];
                row[i] = alpha == 0 ? (byte)0 : r[p];
                row[i + 1] = alpha == 0 ? (byte)0 : g[p];
                row[i + 2] = alpha == 0 ? (byte)0 : b[p];
                row[i + 3] = alpha;
            }
            result.WriteRow(0, y, row);
        }
        return result;
    }

    private static void ReadResources(PsdReader reader, ImageDocument document, HashSet<string> warnings)
    {
        while (reader.Remaining >= 12)
        {
            if (reader.Text(4) != "8BIM")
                throw new InvalidDataException("Invalid PSD image resource signature.");
            var id = reader.UInt16();
            var nameLength = reader.Byte();
            reader.Take(nameLength);
            if ((nameLength & 1) == 0)
                reader.Take(1);
            var length = reader.UInt32();
            var data = reader.Section(length);
            if (id == 1005)
            {
                var horizontal = data.UInt32() / 65536d;
                var units = data.UInt16();
                data.UInt16();
                var vertical = data.UInt32() / 65536d;
                var verticalUnits = data.UInt16();
                data.UInt16();
                if (units == 2)
                    horizontal *= 2.54;
                if (verticalUnits == 2)
                    vertical *= 2.54;
                if (units is not (1 or 2) || verticalUnits is not (1 or 2) || horizontal is < 1 or > 2400 || vertical is < 1 or > 2400)
                    throw new InvalidDataException("Unsupported PSD resolution metadata.");
                document.Dpi = horizontal;
                if (Math.Abs(horizontal - vertical) > .001)
                    warnings.Add("Non-square PSD resolution is represented by its horizontal DPI.");
            }
            else if (id == 1039)
                warnings.Add("The embedded PSD ICC profile is not applied or retained; this editor uses an sRGB working workflow.");
            if ((length & 1) != 0)
                reader.Take(1);
        }
        reader.PaddingOnly();
    }

    private static void ValidateRectangle(int x, int y, int width, int height)
    {
        if (Math.Abs((long)x) > 1_000_000 || Math.Abs((long)y) > 1_000_000 || width is < 0 or > 8192 ||
            height is < 0 or > 8192 || (long)width * height > PixelSurface.MaximumPixels)
            throw new InvalidDataException("Unsupported PSD layer or mask rectangle.");
    }
    private static void Reserve(ref long total, long bytes)
    {
        total = checked(total + bytes);
        if (total > 384L * 1024 * 1024)
            throw new InvalidDataException("PSD decoded data exceeds the 384 MiB processing budget.");
    }
}
