using System.IO.Compression;
using System.Runtime.InteropServices;

namespace ImageSpace.Documents;

internal static class PsdChannelCodec
{
    public static byte[][] Decode(PsdReader reader, int width, int height, int channels)
    {
        var planeSize = checked(width * height);
        if (channels is < 1 or > 16 || (long)planeSize * channels > 384L * 1024 * 1024)
            throw new InvalidDataException("PSD channel expansion exceeds the memory limit.");
        var compression = (PsdCompression)reader.UInt16();
        var planes = new byte[channels][];
        if (compression == PsdCompression.Raw)
        {
            for (var c = 0; c < channels; c++)
                planes[c] = reader.Take(planeSize).ToArray();
        }
        else if (compression == PsdCompression.PackBits)
        {
            var lengths = new ushort[checked(height * channels)];
            for (var i = 0; i < lengths.Length; i++)
                lengths[i] = reader.UInt16();
            for (var c = 0; c < channels; c++)
            {
                planes[c] = new byte[planeSize];
                for (var y = 0; y < height; y++)
                    PsdCodec.DecodeRow(reader.Take(lengths[c * height + y]).Span, planes[c].AsSpan(y * width, width));
            }
        }
        else if (compression is PsdCompression.Zip or PsdCompression.ZipPrediction)
        {
            var compressed = reader.Rest();
            if (!MemoryMarshal.TryGetArray(compressed, out ArraySegment<byte> segment))
                segment = new ArraySegment<byte>(compressed.ToArray());
            using var input = new MemoryStream(segment.Array!, segment.Offset, segment.Count, false);
            using var zip = new ZLibStream(input, CompressionMode.Decompress);
            for (var c = 0; c < channels; c++)
            {
                var plane = planes[c] = new byte[planeSize];
                zip.ReadExactly(plane);
                if (compression == PsdCompression.ZipPrediction)
                    for (var y = 0; y < height; y++)
                        for (var x = 1; x < width; x++)
                        {
                            var i = y * width + x;
                            plane[i] = unchecked((byte)(plane[i] + plane[i - 1]));
                        }
            }
            if (zip.ReadByte() != -1)
                throw new InvalidDataException("PSD ZIP channel expands beyond its declared dimensions.");
        }
        else
            throw new InvalidDataException($"Unsupported PSD compression code {(ushort)compression}.");
        reader.PaddingOnly();
        return planes;
    }

    public static byte[] Encode(byte[] source, int width, int height, PsdCompression compression)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((byte)0);
        writer.Write((byte)compression);
        if (compression == PsdCompression.Raw)
            writer.Write(source);
        else if (compression is PsdCompression.Zip or PsdCompression.ZipPrediction)
        {
            using var zip = new ZLibStream(output, CompressionLevel.Fastest, true);
            // Caller supplies its own scratch plane: prediction never changes the source surface.
            if (compression == PsdCompression.ZipPrediction)
                for (var y = 0; y < height; y++)
                    for (var x = width - 1; x > 0; x--)
                    {
                        var i = y * width + x;
                        source[i] = unchecked((byte)(source[i] - source[i - 1]));
                    }
            zip.Write(source);
        }
        else if (compression == PsdCompression.PackBits)
        {
            using var rows = new MemoryStream();
            for (var y = 0; y < height; y++)
            {
                var start = rows.Position;
                EncodeRow(source.AsSpan(y * width, width), rows);
                var length = checked((ushort)(rows.Position - start));
                writer.Write((byte)(length >> 8));
                writer.Write((byte)length);
            }
            rows.Position = 0;
            rows.CopyTo(output);
        }
        else
            throw new ArgumentOutOfRangeException(nameof(compression));
        return output.ToArray();
    }

    private static void EncodeRow(ReadOnlySpan<byte> row, Stream output)
    {
        var i = 0;
        while (i < row.Length)
        {
            var run = Run(row, i);
            if (run >= 3)
            {
                output.WriteByte(unchecked((byte)(1 - run)));
                output.WriteByte(row[i]);
                i += run;
            }
            else
            {
                var start = i++;
                while (i < row.Length && i - start < 128 && Run(row, i) < 3)
                    i++;
                output.WriteByte((byte)(i - start - 1));
                output.Write(row[start..i]);
            }
        }
        static int Run(ReadOnlySpan<byte> span, int start)
        {
            var length = 1;
            while (start + length < span.Length && length < 128 && span[start + length] == span[start])
                length++;
            return length;
        }
    }
}
