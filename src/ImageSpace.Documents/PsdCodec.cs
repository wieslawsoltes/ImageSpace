using System.Buffers.Binary;
using System.Text;
using ImageSpace.Core;

namespace ImageSpace.Documents;

/// <summary>PSD v1 RGB/8 raster interchange. Preserves Unicode names, resolution and imported user masks;
/// unsupported Photoshop semantics are reported explicitly rather than claimed to roundtrip.</summary>
public static partial class PsdCodec
{
    public sealed record ImportResult(ImageDocument Document, IReadOnlyList<string> Warnings);
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
