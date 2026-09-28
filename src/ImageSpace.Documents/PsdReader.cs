using System.Buffers.Binary;
using System.Text;

namespace ImageSpace.Documents;

/// <summary>Section-local zero-copy reader. No read can consume a following PSD section.</summary>
internal sealed class PsdReader(ReadOnlyMemory<byte> memory)
{
    private int _position;
    public int Remaining => memory.Length - _position;
    public ReadOnlyMemory<byte> Take(int count)
    {
        if (count < 0 || count > Remaining)
            throw new InvalidDataException("PSD section is truncated or exceeds its declared bounds.");
        var result = memory.Slice(_position, count);
        _position += count;
        return result;
    }
    public byte Byte() => Take(1).Span[0];
    public ushort UInt16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2).Span);
    public short Int16() => unchecked((short)UInt16());
    public uint UInt32() => BinaryPrimitives.ReadUInt32BigEndian(Take(4).Span);
    public int Int32() => unchecked((int)UInt32());
    public double Double() => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(Take(8).Span));
    public string Text(int count) => Encoding.Latin1.GetString(Take(count).Span);
    public PsdReader Section() => Section(UInt32());
    public PsdReader Section(uint count)
    {
        if (count > int.MaxValue)
            throw new InvalidDataException("PSD section exceeds supported bounds.");
        return new(Take((int)count));
    }
    public ReadOnlyMemory<byte> Rest() => Take(Remaining);
    public void PaddingOnly()
    {
        if (Remaining > 3 || Rest().Span.IndexOfAnyExcept((byte)0) >= 0)
            throw new InvalidDataException("Unexpected data beyond a PSD section.");
    }
}
