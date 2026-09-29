using System.Buffers;
using System.Runtime.InteropServices;
using System.IO.Compression;
using System.Text.Json;
using ImageSpace.Core;

namespace ImageSpace.Documents;

public static class DocumentArchive
{
    public const long MaximumArchiveBytes = 128L * 1024 * 1024;
    private const long MaximumExpandedBytes = 384L * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 32
    };

    public sealed class Manifest
    {
        public int Version { get; set; } = 1;
        public Guid Id
        {
            get; set;
        }
        public string Name { get; set; } = "Untitled";
        public int Width
        {
            get; set;
        }
        public int Height
        {
            get; set;
        }
        public double Dpi { get; set; } = 72;
        public Guid ActiveLayerId
        {
            get; set;
        }
        public List<LayerRecord> Layers { get; set; } = [];
    }

    public sealed class LayerRecord
    {
        public Layer Metadata { get; set; } = new();
        public string? Pixels
        {
            get; set;
        }
        public string? Mask
        {
            get; set;
        }
        public int PixelWidth
        {
            get; set;
        }
        public int PixelHeight
        {
            get; set;
        }
        public int MaskWidth
        {
            get; set;
        }
        public int MaskHeight
        {
            get; set;
        }
    }

    private static int RequiredVersion(ImageDocument document)
    {
        if (document.Layers.Any(layer => layer.IsClipped))
            return 5;
        if (document.Layers.Any(layer => !layer.MaskLinked || layer.MaskPlacement != AffinePlacement.Identity))
            return 4;
        // Existing version-1/2 readers ignore new metadata. Refuse that silent visual loss
        // by marking any mask-specific or output-crossfade semantics as version 3.
        if (document.Layers.Any(layer =>
            (layer.Mask is not null && (layer.Kind == LayerKind.Adjustment || layer.MaskDensity != 1 || layer.MaskFeather != 0)) ||
            (layer.Kind == LayerKind.Adjustment && layer.Opacity is > 0 and < 1)))
            return 3;
        return document.Layers.Any(layer => layer.Adjustment is AdjustmentKind.Curves or AdjustmentKind.Levels) ? 2 : 1;
    }

    public static byte[] Save(ImageDocument document)
    {
        document.Validate();
        var estimated = document.Layers.SelectMany(layer => new[] { layer.Pixels, layer.Mask })
            .Where(surface => surface is not null).Sum(surface => (long)surface!.Width * surface.Height * 4);
        if (estimated > MaximumExpandedBytes)
        {
            throw new InvalidDataException("Document exceeds the native archive memory limit.");
        }
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var manifest = new Manifest
            {
                // Older readers must reject, rather than silently drop, the new adjustment semantics.
                Version = RequiredVersion(document),
                Id = document.Id,
                Name = document.Name,
                Width = document.Width,
                Height = document.Height,
                Dpi = document.Dpi,
                ActiveLayerId = document.ActiveLayerId
            };
            long expanded = 0;
            foreach (var layer in document.Layers)
            {
                var metadata = layer.Snapshot();
                metadata.Pixels = null;
                metadata.Mask = null;
                var record = new LayerRecord { Metadata = metadata };
                if (layer.Pixels is not null)
                {
                    record.Pixels = $"layers/{layer.Id:N}.rgba";
                    record.PixelWidth = layer.Pixels.Width;
                    record.PixelHeight = layer.Pixels.Height;
                    WriteSurface(record.Pixels, layer.Pixels);
                }
                if (layer.Mask is not null)
                {
                    record.Mask = $"masks/{layer.Id:N}.rgba";
                    record.MaskWidth = layer.Mask.Width;
                    record.MaskHeight = layer.Mask.Height;
                    WriteSurface(record.Mask, layer.Mask);
                }
                manifest.Layers.Add(record);
            }
            var json = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
            if (json.Length > 4 * 1024 * 1024)
            {
                throw new InvalidDataException("Document metadata exceeds the 4 MiB manifest limit.");
            }
            Write("manifest.json", json);
            void WriteSurface(string path, PixelSurface surface)
            {
                expanded += (long)surface.Width * surface.Height * 4;
                if (expanded > MaximumExpandedBytes)
                    throw new InvalidDataException("Document exceeds the native archive memory limit.");
                using var entry = zip.CreateEntry(path, CompressionLevel.Fastest).Open();
                var buffer = ArrayPool<byte>.Shared.Rent(surface.Width * 4);
                try
                {
                    var row = buffer.AsSpan(0, surface.Width * 4);
                    for (var y = 0; y < surface.Height; y++)
                    {
                        surface.CopyRowTo(0, y, row);
                        entry.Write(row);
                        if (output.Length > MaximumArchiveBytes)
                            throw new InvalidDataException("Compressed document exceeds 128 MiB.");
                    }
                }
                finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
            }
            void Write(string path, byte[] bytes)
            {
                expanded += bytes.Length;
                if (expanded > MaximumExpandedBytes)
                {
                    throw new InvalidDataException("Document exceeds the native archive memory limit.");
                }
                using var stream = zip.CreateEntry(path, CompressionLevel.Fastest).Open();
                stream.Write(bytes);
            }
        }
        if (output.Length > MaximumArchiveBytes)
        {
            throw new InvalidDataException("Compressed document exceeds 128 MiB.");
        }
        return output.ToArray();
    }

    public static ImageDocument Load(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaximumArchiveBytes)
        {
            throw new InvalidDataException("File exceeds 128 MiB.");
        }
        if (!MemoryMarshal.TryGetArray(bytes, out ArraySegment<byte> segment))
            segment = new ArraySegment<byte>(bytes.ToArray());
        using var input = new MemoryStream(segment.Array!, segment.Offset, segment.Count, false);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count > 260 || zip.Entries.Sum(entry => entry.Length) > MaximumExpandedBytes)
        {
            throw new InvalidDataException("Archive exceeds safety limits.");
        }
        if (zip.Entries.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count() != zip.Entries.Count)
        {
            throw new InvalidDataException("Duplicate archive entries.");
        }
        var manifest = JsonSerializer.Deserialize<Manifest>(Read("manifest.json", 4 * 1024 * 1024), Json)
            ?? throw new InvalidDataException("Missing document manifest.");
        if (manifest.Version is not (1 or 2 or 3 or 4 or 5))
        {
            throw new InvalidDataException($"Unsupported document version {manifest.Version}.");
        }
        if (manifest.Layers is null || manifest.Layers.Count > 128 || manifest.Layers.Any(record => record?.Metadata is null))
        {
            throw new InvalidDataException("Invalid layer metadata or layer count.");
        }
        if (manifest.Version < 5 && manifest.Layers.Any(record => record.Metadata.IsClipped))
            throw new InvalidDataException("Clipping metadata requires native manifest version 5.");
        var document = new ImageDocument(manifest.Width, manifest.Height, manifest.Name)
        {
            Id = manifest.Id,
            Dpi = manifest.Dpi,
            ActiveLayerId = manifest.ActiveLayerId,
            Layers = manifest.Layers.Select(record => record.Metadata).ToList()
        };
        document.Validate();
        if (manifest.Version < 4 && document.Layers.Any(layer =>
            !layer.MaskLinked || layer.MaskPlacement != AffinePlacement.Identity))
            throw new InvalidDataException("Independent mask placement requires archive version 4.");
        foreach (var record in manifest.Layers)
        {
            var layer = record.Metadata;
            layer.Pixels = null;
            layer.Mask = null;
            if (record.Pixels is not null)
            {
                PixelSurface.ValidateSize(record.PixelWidth, record.PixelHeight);
                layer.Pixels = ReadSurface(record.Pixels, record.PixelWidth, record.PixelHeight);
            }
            if (record.Mask is not null)
            {
                PixelSurface.ValidateSize(record.MaskWidth, record.MaskHeight);
                layer.Mask = ReadSurface(record.Mask, record.MaskWidth, record.MaskHeight);
            }
        }
        return document;

        PixelSurface ReadSurface(string path, int width, int height)
        {
            var entry = zip.GetEntry(path) ?? throw new InvalidDataException($"Missing archive entry: {path}");
            if (entry.Length != (long)width * height * 4)
                throw new InvalidDataException("Entry length differs from declared pixel dimensions.");
            var result = new PixelSurface(width, height);
            using var stream = entry.Open();
            var buffer = ArrayPool<byte>.Shared.Rent(width * 4);
            try
            {
                var row = buffer.AsSpan(0, width * 4);
                for (var y = 0; y < height; y++)
                {
                    stream.ReadExactly(row);
                    // Native archives preserve authored RGBA bytes, including RGB under zero alpha.
                    // PixelSurface.WriteRow retains these bytes without a per-pixel normalization pass.
                    result.WriteRow(0, y, row);
                }
                if (stream.ReadByte() != -1)
                    throw new InvalidDataException("Entry exceeds declared pixel dimensions.");
            }
            finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
            return result;
        }
        byte[] Read(string path, int maximum)
        {
            var entry = zip.GetEntry(path) ?? throw new InvalidDataException($"Missing archive entry: {path}");
            if (entry.Length < 0 || entry.Length > maximum)
            {
                throw new InvalidDataException("Entry exceeds declared dimensions.");
            }
            using var stream = entry.Open();
            var data = new byte[(int)entry.Length];
            stream.ReadExactly(data);
            if (stream.ReadByte() != -1)
            {
                throw new InvalidDataException("Entry exceeds its declared length.");
            }
            return data;
        }
    }
}
