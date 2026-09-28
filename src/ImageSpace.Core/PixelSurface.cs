using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ImageSpace.Core;

/// <summary>
/// Sparse, straight-alpha RGBA8 storage with 128-pixel copy-on-write tiles.
/// The owner is single-writer. Snapshots share immutable tile generations.
/// Revisions are monotonic content stamps, not counts of individual pixel writes.
/// </summary>
public sealed class PixelSurface
{
    public const int TileSize = 128;
    public const int MaximumDimension = 8192;
    public const long MaximumPixels = 16_777_216;
    private const int TileBytes = TileSize * TileSize * 4;
    private static long _nextOwner;
    private long _owner = Interlocked.Increment(ref _nextOwner);
    private readonly Dictionary<int, Tile> _tiles;

    private sealed class Tile(long owner, byte[] pixels, long revision = 0)
    {
        public long Owner { get; } = owner;
        public byte[] Pixels { get; } = pixels;
        public long Revision { get; set; } = revision;
    }

    public int Width
    {
        get;
    }
    public int Height
    {
        get;
    }
    public long Revision
    {
        get; private set;
    }
    public int TilesAcross => (Width + TileSize - 1) / TileSize;
    public int AllocatedTiles => _tiles.Count;
    public long AllocatedBytes => (long)_tiles.Count * TileBytes;

    public PixelSurface(int width, int height)
    {
        ValidateSize(width, height);
        Width = width;
        Height = height;
        _tiles = [];
    }

    private PixelSurface(PixelSurface source)
    {
        Width = source.Width;
        Height = source.Height;
        Revision = source.Revision;
        _tiles = new(source._tiles);
        source._owner = Interlocked.Increment(ref _nextOwner);
    }

    public static void ValidateSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > MaximumDimension || height > MaximumDimension ||
            (long)width * height > MaximumPixels)
            throw new ArgumentOutOfRangeException(nameof(width), "Images must be 1–8192 pixels per side and at most 16 megapixels.");
    }

    public PixelSurface Snapshot() => new(this);

    public long GetTileRevision(int x, int y) =>
        (uint)x < (uint)Width && (uint)y < (uint)Height &&
        _tiles.TryGetValue(y / TileSize * TilesAcross + x / TileSize, out var tile) ? tile.Revision : -1;

    public Rgba32 Get(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height ||
            !_tiles.TryGetValue(y / TileSize * TilesAcross + x / TileSize, out var tile))
            return Rgba32.Transparent;
        var index = ((y % TileSize) * TileSize + x % TileSize) * 4;
        var bytes = tile.Pixels;
        return new(bytes[index], bytes[index + 1], bytes[index + 2], bytes[index + 3]);
    }

    public void Set(int x, int y, Rgba32 color)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return;
        var key = y / TileSize * TilesAcross + x / TileSize;
        var index = ((y % TileSize) * TileSize + x % TileSize) * 4;
        if (!_tiles.TryGetValue(key, out var tile))
        {
            if (color.A == 0)
                return;
            _tiles[key] = tile = new(_owner, new byte[TileBytes]);
        }
        else
        {
            // Check before copy-on-write: a no-op must retain both sharing and cache identity.
            var old = tile.Pixels;
            if (old[index] == color.R && old[index + 1] == color.G &&
                old[index + 2] == color.B && old[index + 3] == color.A)
                return;
            tile = MakeWritable(key, tile);
        }
        var bytes = tile.Pixels;
        bytes[index] = color.R;
        bytes[index + 1] = color.G;
        bytes[index + 2] = color.B;
        bytes[index + 3] = color.A;
        tile.Revision++;
        Revision++;
    }

    /// <summary>Replace the surface using vectorizable tile fills rather than per-pixel dictionary lookups.</summary>
    public void Fill(Rgba32 color)
    {
        _tiles.Clear();
        Revision++;
        if (color.A == 0)
            return;
        Span<byte> rgba = stackalloc byte[] { color.R, color.G, color.B, color.A };
        var packed = BitConverter.IsLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(rgba) : BinaryPrimitives.ReadUInt32BigEndian(rgba);
        for (var ty = 0; ty < Height; ty += TileSize)
        {
            var rows = Math.Min(TileSize, Height - ty);
            for (var tx = 0; tx < Width; tx += TileSize)
            {
                var columns = Math.Min(TileSize, Width - tx);
                var pixels = new byte[TileBytes];
                var words = MemoryMarshal.Cast<byte, uint>(pixels.AsSpan());
                if (columns == TileSize && rows == TileSize)
                    words.Fill(packed);
                else
                for (var row = 0; row < rows; row++)
                    words.Slice(row * TileSize, columns).Fill(packed);
                _tiles.Add(ty / TileSize * TilesAcross + tx / TileSize, new(_owner, pixels, 1));
            }
        }
    }

    /// <summary>
    /// Copy a contiguous pixel run into a caller-owned RGBA span. An empty span is allowed at x == Width.
    /// Padding is not required; all arguments are validated before writing the destination.
    /// </summary>
    public void CopyRowTo(int x, int y, Span<byte> destination)
    {
        ValidateRow(x, y, destination.Length);
        while (!destination.IsEmpty)
        {
            var count = Math.Min(TileSize - x % TileSize, destination.Length / 4);
            var bytes = destination[..(count * 4)];
            if (_tiles.TryGetValue(y / TileSize * TilesAcross + x / TileSize, out var tile))
                tile.Pixels.AsSpan(((y % TileSize) * TileSize + x % TileSize) * 4, bytes.Length).CopyTo(bytes);
            else
                bytes.Clear();
            destination = destination[bytes.Length..];
            x += count;
        }
    }

    /// <summary>
    /// Write an exact RGBA run, preserving hidden RGB values. All-zero runs keep unallocated tiles sparse.
    /// Unchanged runs retain tile identities, revisions, and snapshot sharing.
    /// </summary>
    public void WriteRow(int x, int y, ReadOnlySpan<byte> source)
    {
        ValidateRow(x, y, source.Length);
        while (!source.IsEmpty)
        {
            var count = Math.Min(TileSize - x % TileSize, source.Length / 4);
            var bytes = source[..(count * 4)];
            var key = y / TileSize * TilesAcross + x / TileSize;
            var offset = ((y % TileSize) * TileSize + x % TileSize) * 4;
            if (!_tiles.TryGetValue(key, out var tile))
            {
                if (bytes.IndexOfAnyExcept((byte)0) < 0)
                {
                    source = source[bytes.Length..];
                    x += count;
                    continue;
                }
                _tiles[key] = tile = new(_owner, new byte[TileBytes]);
            }
            else if (tile.Pixels.AsSpan(offset, bytes.Length).SequenceEqual(bytes))
            {
                source = source[bytes.Length..];
                x += count;
                continue;
            }
            else
            {
                // Span.CopyTo covers overlap within this chunk. A borrowed tile span may also
                // overlap a later source chunk: snapshot that remaining run before mutating it.
                if (tile.Owner == _owner && tile.Pixels.AsSpan(offset, bytes.Length).Overlaps(source[bytes.Length..]))
                {
                    WriteRow(x, y, source.ToArray());
                    return;
                }
                tile = MakeWritable(key, tile);
            }
            bytes.CopyTo(tile.Pixels.AsSpan(offset, bytes.Length));
            tile.Revision++;
            Revision++;
            source = source[bytes.Length..];
            x += count;
        }
    }

    private Tile MakeWritable(int key, Tile tile)
    {
        if (tile.Owner != _owner)
            _tiles[key] = tile = new(_owner, (byte[])tile.Pixels.Clone(), tile.Revision);
        return tile;
    }

    private void ValidateRow(int x, int y, int byteLength)
    {
        if ((byteLength & 3) != 0 || (uint)y >= (uint)Height || (uint)x > (uint)Width || byteLength / 4 > Width - x)
            throw new ArgumentOutOfRangeException(nameof(x), "The RGBA pixel run must lie inside the surface.");
    }

    public IEnumerable<(int X, int Y, ReadOnlyMemory<byte> Pixels, object Identity)> EnumerateTiles()
    {
        foreach (var (key, tile) in _tiles)
            yield return (key % TilesAcross * TileSize, key / TilesAcross * TileSize, tile.Pixels, tile.Pixels);
    }

    public byte[] ToRgba()
    {
        var output = new byte[checked(Width * Height * 4)];
        CopyToRgba(output);
        return output;
    }

    /// <summary>Copy to packed or padded RGBA storage without allocating an intermediate image. Padding is untouched.</summary>
    public void CopyToRgba(Span<byte> destination, int rowBytes = 0)
    {
        if (rowBytes == 0)
            rowBytes = checked(Width * 4);
        ValidateBuffer(Width, Height, destination.Length, rowBytes);
        for (var y = 0; y < Height; y++)
            destination.Slice(y * rowBytes, Width * 4).Clear();
        foreach (var (tx, ty, pixels, _) in EnumerateTiles())
        {
            var columns = Math.Min(TileSize, Width - tx) * 4;
            for (var y = 0; y < Math.Min(TileSize, Height - ty); y++)
                pixels.Span.Slice(y * TileSize * 4, columns).CopyTo(destination.Slice((ty + y) * rowBytes + tx * 4, columns));
        }
    }

    public static PixelSurface FromRgba(int width, int height, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != (long)width * height * 4)
            throw new ArgumentException("RGBA byte length does not match dimensions.", nameof(bytes));
        return FromRgba(width, height, bytes, checked(width * 4));
    }

    /// <summary>
    /// Import packed/padded RGBA by tile. Transparent source pixels are canonicalized to zero,
    /// matching the native archive's existing sparse import semantics.
    /// </summary>
    public static PixelSurface FromRgba(int width, int height, ReadOnlySpan<byte> bytes, int rowBytes)
    {
        ValidateSize(width, height);
        ValidateBuffer(width, height, bytes.Length, rowBytes);
        var result = new PixelSurface(width, height);
        for (var ty = 0; ty < height; ty += TileSize)
        {
            var rows = Math.Min(TileSize, height - ty);
            for (var tx = 0; tx < width; tx += TileSize)
            {
                var columns = Math.Min(TileSize, width - tx);
                byte[]? tile = null;
                for (var y = 0; y < rows; y++)
                {
                    var input = bytes.Slice((ty + y) * rowBytes + tx * 4, columns * 4);
                    for (var x = 0; x < columns; x++)
                    {
                        if (input[x * 4 + 3] == 0)
                            continue;
                        tile ??= new byte[TileBytes];
                        input.Slice(x * 4, 4).CopyTo(tile.AsSpan((y * TileSize + x) * 4, 4));
                    }
                }
                if (tile is not null)
                {
                    result._tiles.Add(ty / TileSize * result.TilesAcross + tx / TileSize, new(result._owner, tile, 1));
                    result.Revision++;
                }
            }
        }
        return result;
    }

    private static void ValidateBuffer(int width, int height, int byteLength, int rowBytes)
    {
        if (rowBytes < (long)width * 4 || (long)(height - 1) * rowBytes + (long)width * 4 > byteLength)
            throw new ArgumentException("RGBA buffer or row stride does not match the surface dimensions.");
    }

    /// <summary>Copy alpha to a compact, row-major coverage buffer.</summary>
    public void CopyAlphaTo(Span<byte> destination)
    {
        if (destination.Length != checked(Width * Height))
            throw new ArgumentException("Coverage dimensions differ.", nameof(destination));
        destination.Clear();
        foreach (var (tx, ty, pixels, _) in EnumerateTiles())
            for (var y = 0; y < Math.Min(TileSize, Height - ty); y++)
                for (var x = 0; x < Math.Min(TileSize, Width - tx); x++)
                    destination[(ty + y) * Width + tx + x] = pixels.Span[(y * TileSize + x) * 4 + 3];
    }

    /// <summary>Construct a sparse white mask from row-major alpha coverage.</summary>
    public static PixelSurface FromAlpha(int width, int height, ReadOnlySpan<byte> coverage)
    {
        ValidateSize(width, height);
        if (coverage.Length != checked(width * height))
            throw new ArgumentException("Coverage dimensions differ.", nameof(coverage));
        var result = new PixelSurface(width, height);
        for (var ty = 0; ty < height; ty += TileSize)
            for (var tx = 0; tx < width; tx += TileSize)
            {
                byte[]? tile = null;
                for (var y = 0; y < Math.Min(TileSize, height - ty); y++)
                    for (var x = 0; x < Math.Min(TileSize, width - tx); x++)
                    {
                        var alpha = coverage[(ty + y) * width + tx + x];
                        if (alpha == 0)
                            continue;
                        tile ??= new byte[TileBytes];
                        var i = (y * TileSize + x) * 4;
                        tile[i] = tile[i + 1] = tile[i + 2] = 255;
                        tile[i + 3] = alpha;
                    }
                if (tile is not null)
                {
                    result._tiles.Add(ty / TileSize * result.TilesAcross + tx / TileSize, new(result._owner, tile, 1));
                    result.Revision++;
                }
            }
        return result;
    }
}
