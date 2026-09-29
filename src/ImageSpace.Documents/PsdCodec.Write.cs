using System.IO.Compression;
using System.Text;
using ImageSpace.Core;

namespace ImageSpace.Documents;

public static partial class PsdCodec
{
    private sealed record EncodedLayer(Layer Metadata, int Width, int Height, byte[][] Channels);

    /// <summary>Export rasterized layers and a merged composite. The callback must not bake layer opacity/blending.
    /// Only one callback surface is retained at a time; encoded channels and all writes have explicit budgets.</summary>
    public static byte[] Save(ImageDocument document, Func<Layer, PixelSurface> rasterize, PixelSurface composite,
        PsdCompression compression = PsdCompression.Zip)
    {
        ArgumentNullException.ThrowIfNull(rasterize);
        document.Validate();
        if (!Enum.IsDefined(compression))
            throw new ArgumentOutOfRangeException(nameof(compression));
        if (composite.Width != document.Width || composite.Height != document.Height)
            throw new ArgumentException("Composite dimensions differ.", nameof(composite));
        if (document.Layers.Any(layer => layer.Visible && layer.IsClipped && layer.Kind == LayerKind.Adjustment))
            throw new InvalidOperationException("PSD raster export cannot preserve clipped adjustments. Export a flattened compatibility document.");
        var layers = new List<EncodedLayer>();
        long budget = 0;
        foreach (var layer in document.Layers.AsEnumerable().Reverse().Where(layer => layer.Kind != LayerKind.Adjustment))
        {
            var pixels = rasterize(layer) ?? throw new InvalidOperationException("Layer rasterization returned null.");
            var channels = new byte[4][];
            for (var channel = 0; channel < 4; channel++)
            {
                channels[channel] = PsdChannelCodec.Encode(Plane(pixels, channel), pixels.Width, pixels.Height, compression);
                budget += channels[channel].Length;
                if (budget > DocumentArchive.MaximumArchiveBytes - 8 * 1024 * 1024)
                    throw new InvalidDataException("PSD encoded layers exceed the export budget.");
            }
            layers.Add(new(layer, pixels.Width, pixels.Height, channels));
        }
        // At least one layer is needed to signal merged alpha with a negative layer count.
        if (layers.Count == 0)
        {
            var metadata = Layer.Raster("Composite", composite.Width, composite.Height);
            var channels = Enumerable.Range(0, 4).Select(c => PsdChannelCodec.Encode(Plane(composite, c), composite.Width, composite.Height, compression)).ToArray();
            layers.Add(new(metadata, composite.Width, composite.Height, channels));
        }
        using var output = new PsdOutputStream();
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
        // ResolutionInfo resource (fixed 16.16 pixels/inch, display size in inches).
        U32(writer, 28);
        ASCII(writer, "8BIM");
        U16(writer, 1005);
        U16(writer, 0);
        U32(writer, 16);
        for (var i = 0; i < 2; i++)
        {
            U32(writer, (uint)Math.Round(document.Dpi * 65536));
            U16(writer, 1);
            U16(writer, 1);
        }
        var outerLengthOffset = output.Position;
        U32(writer, 0);
        var infoLengthOffset = output.Position;
        U32(writer, 0);
        var infoStart = output.Position;
        I16(writer, checked((short)-layers.Count));
        foreach (var item in layers)
        {
            I32(writer, 0);
            I32(writer, 0);
            I32(writer, item.Height);
            I32(writer, item.Width);
            U16(writer, 4);
            for (var c = 0; c < 4; c++)
            {
                I16(writer, c == 3 ? (short)-1 : (short)c);
                U32(writer, (uint)item.Channels[c].Length);
            }
            ASCII(writer, "8BIM");
            ASCII(writer, BlendKey(item.Metadata.Blend));
            writer.Write(Rgba32.Byte(item.Metadata.Opacity * 255));
            writer.Write(item.Metadata.IsClipped ? (byte)1 : (byte)0);
            writer.Write((byte)(item.Metadata.Visible ? 0 : 2));
            writer.Write((byte)0);
            var extraOffset = output.Position;
            U32(writer, 0);
            var extraStart = output.Position;
            U32(writer, 0);
            U32(writer, 0); // Masks already baked by the callback, no Blend If.
            var name = Encoding.ASCII.GetBytes(item.Metadata.Name);
            var count = Math.Min(255, name.Length);
            writer.Write((byte)count);
            writer.Write(name, 0, count);
            for (var pad = (4 - (count + 1) % 4) % 4; pad > 0; pad--)
                writer.Write((byte)0);
            ASCII(writer, "8BIM");
            ASCII(writer, "luni");
            var unicode = new UnicodeEncoding(true, false, true).GetBytes(item.Metadata.Name);
            U32(writer, checked((uint)(4 + unicode.Length)));
            U32(writer, (uint)item.Metadata.Name.Length);
            writer.Write(unicode);
            if (item.Metadata.Locked)
            {
                ASCII(writer, "8BIM");
                ASCII(writer, "lspf");
                U32(writer, 4);
                U32(writer, 0x80000000);
            }
            PatchLength(extraOffset, output.Position - extraStart);
        }
        foreach (var layer in layers)
            foreach (var plane in layer.Channels)
                writer.Write(plane);
        if ((output.Position - infoStart) % 2 != 0)
            writer.Write((byte)0);
        PatchLength(infoLengthOffset, output.Position - infoStart);
        U32(writer, 0); // Global mask.
        PatchLength(outerLengthOffset, output.Position - outerLengthOffset - 4);
        // Composite uses a single zlib stream spanning all planar channels, not four channel headers.
        if (compression is PsdCompression.Zip or PsdCompression.ZipPrediction)
        {
            U16(writer, (ushort)compression);
            using var zip = new ZLibStream(output, CompressionLevel.Fastest, true);
            for (var c = 0; c < 4; c++)
            {
                var plane = Plane(composite, c);
                if (compression == PsdCompression.ZipPrediction)
                    for (var y = 0; y < composite.Height; y++)
                        for (var x = composite.Width - 1; x > 0; x--)
                        {
                            var p = y * composite.Width + x;
                            plane[p] = unchecked((byte)(plane[p] - plane[p - 1]));
                        }
                zip.Write(plane);
            }
        }
        else
        {
            // Raw composite is universally compatible; layer compression remains caller-selected.
            U16(writer, 0);
            for (var c = 0; c < 4; c++)
                writer.Write(Plane(composite, c));
        }
        return output.ToArray();
        void PatchLength(long offset, long length)
        {
            var position = output.Position;
            output.Position = offset;
            U32(writer, checked((uint)length));
            output.Position = position;
        }
    }

    private static byte[] Plane(PixelSurface pixels, int channel)
    {
        var result = new byte[checked(pixels.Width * pixels.Height)];
        foreach (var (tx, ty, data, _) in pixels.EnumerateTiles())
        {
            var span = data.Span;
            var width = Math.Min(PixelSurface.TileSize, pixels.Width - tx);
            for (var y = 0; y < Math.Min(PixelSurface.TileSize, pixels.Height - ty); y++)
                for (var x = 0; x < width; x++)
                    result[(ty + y) * pixels.Width + tx + x] = span[(y * PixelSurface.TileSize + x) * 4 + channel];
        }
        return result;
    }

    private sealed class PsdOutputStream : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Guard(buffer.Length);
            base.Write(buffer);
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            Guard(count);
            base.Write(buffer, offset, count);
        }
        public override void WriteByte(byte value)
        {
            Guard(1);
            base.WriteByte(value);
        }
        private void Guard(int count)
        {
            if (Position + count > DocumentArchive.MaximumArchiveBytes)
                throw new InvalidDataException("PSD export exceeds 128 MiB.");
        }
    }
}
