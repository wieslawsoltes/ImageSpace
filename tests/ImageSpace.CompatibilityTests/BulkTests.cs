using ImageSpace.Core;
using ImageSpace.Imaging;

internal static partial class Program
{
    private static void RegisterBulkTests()
    {
        foreach (var size in new[] { (1, 1), (127, 129), (128, 128), (259, 131), (1, 8192) })
        {
            Test($"bulk fill agrees with pixel writes at {size}", () =>
            {
                var expected = new PixelSurface(size.Item1, size.Item2);
                var actual = new PixelSurface(size.Item1, size.Item2);
                var color = new Rgba32(20, 40, 90, 121);
                for (var y = 0; y < expected.Height; y++)
                for (var x = 0; x < expected.Width; x++)
                    expected.Set(x, y, color);
                actual.Fill(color);
                Equal(expected, actual);
                foreach (var (tx, ty, data, _) in actual.EnumerateTiles())
                    for (var y = 0; y < PixelSurface.TileSize; y++)
                    for (var x = 0; x < PixelSurface.TileSize; x++)
                        if (tx + x >= actual.Width || ty + y >= actual.Height)
                            Check(data.Span[(y * PixelSurface.TileSize + x) * 4 + 3] == 0, "Unused tile padding must stay transparent.");
            });
        }
        Test("unchanged writes preserve shared tile identity", () =>
        {
            var surface = Seed();
            var snapshot = surface.Snapshot();
            var identity = surface.EnumerateTiles().First().Identity;
            var revision = surface.Revision;
            surface.Set(0, 0, surface.Get(0, 0));
            Check(ReferenceEquals(identity, surface.EnumerateTiles().First().Identity));
            Check(ReferenceEquals(identity, snapshot.EnumerateTiles().First().Identity));
            Check(surface.Revision == revision);
        });
        Test("row copies cross tile boundaries and retain all bytes", () =>
        {
            var source = Seed();
            var target = new PixelSurface(source.Width, source.Height);
            var buffer = new byte[source.Width * 4];
            for (var y = 0; y < source.Height; y++)
            {
                source.CopyRowTo(0, y, buffer);
                target.WriteRow(0, y, buffer);
            }
            Check(source.ToRgba().SequenceEqual(target.ToRgba()));
        });
        Test("row changes clone only touched tiles", () =>
        {
            var surface = Seed();
            var snapshot = surface.Snapshot();
            var ids = surface.EnumerateTiles().ToDictionary(t => (t.X, t.Y), t => t.Identity);
            surface.WriteRow(127, 0, new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 });
            Check(surface.Get(127, 0) == new Rgba32(1, 2, 3));
            Check(snapshot.Get(127, 0) != surface.Get(127, 0));
            foreach (var tile in surface.EnumerateTiles())
                Check(ReferenceEquals(ids[(tile.X, tile.Y)], tile.Identity) == (tile.Y != 0 || tile.X >= 256));
        });
        Test("zero rows do not allocate sparse tiles", () =>
        {
            var surface = new PixelSurface(257, 10);
            surface.WriteRow(0, 4, new byte[257 * 4]);
            Check(surface.AllocatedTiles == 0 && surface.Revision == 0);
        });
        Test("invalid row writes cannot partially change a surface", () =>
        {
            var surface = Seed();
            var before = surface.ToRgba();
            Throws<ArgumentOutOfRangeException>(() => surface.WriteRow(250, 0, new byte[100]));
            Check(before.SequenceEqual(surface.ToRgba()));
            Throws<ArgumentOutOfRangeException>(() => surface.CopyRowTo(0, -1, new byte[4]));
            Throws<ArgumentOutOfRangeException>(() => surface.WriteRow(0, 0, new byte[3]));
        });
        Test("padded RGBA import and export retain caller padding", () =>
        {
            var source = Seed(131, 3);
            const int stride = 131 * 4 + 13;
            var bytes = Enumerable.Repeat((byte)177, stride * 3).ToArray();
            source.CopyToRgba(bytes, stride);
            for (var y = 0; y < 3; y++)
                Check(bytes.AsSpan(y * stride + 131 * 4, 13).IndexOfAnyExcept((byte)177) < 0);
            Equal(source, PixelSurface.FromRgba(131, 3, bytes, stride));
            Throws<ArgumentException>(() => source.CopyToRgba(new byte[2], stride));
        });
        Test("alpha conversion retains sparse coverage exactly", () =>
        {
            var source = Seed();
            var a = new byte[source.Width * source.Height];
            source.CopyAlphaTo(a);
            var mask = PixelSurface.FromAlpha(source.Width, source.Height, a);
            var b = new byte[a.Length];
            mask.CopyAlphaTo(b);
            Check(a.SequenceEqual(b));
            Check(PixelSurface.FromAlpha(259, 131, new byte[259 * 131]).AllocatedTiles == 0);
        });
        Test("pixel and row writes preserve snapshot isolation after mixed mutations", () =>
        {
            var surface = Seed();
            var snapshot = surface.Snapshot();
            var before = snapshot.ToRgba();
            surface.Fill(Rgba32.White);
            surface.WriteRow(100, 30, new byte[500]);
            surface.Set(0, 0, Rgba32.Black);
            Check(before.SequenceEqual(snapshot.ToRgba()));
            snapshot.WriteRow(0, 0, new byte[] { 21, 32, 43, 54 });
            Check(surface.Get(0, 0) == Rgba32.Black);
        });
        Test("histograms agree with scalar traversal for all channels", () =>
        {
            var surface = Seed();
            for (var channel = -1; channel <= 2; channel++)
            {
                var expected = new int[256];
                for (var y = 0; y < surface.Height; y++)
                for (var x = 0; x < surface.Width; x++)
                {
                    var c = surface.Get(x, y);
                    if (c.A == 0)
                        continue;
                    expected[channel switch
                    {
                        0 => c.R,
                        1 => c.G,
                        2 => c.B,
                        _ => Rgba32.Byte(c.R * .2126 + c.G * .7152 + c.B * .0722)
                    }]++;
                }
                Check(expected.SequenceEqual(RasterOperations.Histogram(surface, channel)));
            }
        });
        foreach (var size in new[] { (1, 1), (257, 99), (51, 201), (129, 127) })
            foreach (var nearest in new[] { false, true })
            {
                Test($"row resampling agrees with scalar reference {size}, nearest={nearest}", () =>
                {
                    var source = Seed(137, 123);
                    var reference = new PixelSurface(size.Item1, size.Item2);
                    for (var y = 0; y < reference.Height; y++)
                    for (var x = 0; x < reference.Width; x++)
                    {
                        var sx = (x + .5) * source.Width / reference.Width - .5;
                        var sy = (y + .5) * source.Height / reference.Height - .5;
                        reference.Set(x, y, nearest ? source.Get(Math.Clamp((int)Math.Round(sx), 0, source.Width - 1), Math.Clamp((int)Math.Round(sy), 0, source.Height - 1)) : RasterOperations.Sample(source, sx, sy));
                    }
                    Equal(reference, RasterOperations.Resize(source, reference.Width, reference.Height, nearest));
                });
            }
        Test("large sampling loops allocate no per-pixel objects", () =>
        {
            var source = Seed(8, 8);
            for (var i = 0; i < 1000; i++)
                RasterOperations.Sample(source, i % 7 + .2, i % 5 + .3);
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10000; i++)
                RasterOperations.Sample(source, i % 7 + .2, i % 5 + .3);
            Check(GC.GetAllocatedBytesForCurrentThread() - bytes < 1024, "Bilinear sampling allocated per-pixel data.");
        });
        Test("crop and flip agree with scalar coordinates", () =>
        {
            var source = Seed();
            var crop = RasterOperations.Crop(source, -7, 100, 281, 43);
            for (var y = 0; y < crop.Height; y++)
            for (var x = 0; x < crop.Width; x++)
                Check(crop.Get(x, y) == source.Get(x - 7, y + 100));
            foreach (var horizontal in new[] { false, true })
            {
                var flipped = RasterOperations.Flip(source, horizontal);
                for (var y = 0; y < source.Height; y++)
                for (var x = 0; x < source.Width; x++)
                    Check(flipped.Get(x, y) == source.Get(horizontal ? source.Width - 1 - x : x, horizontal ? y : source.Height - 1 - y));
            }
            Check(RasterOperations.Crop(source, int.MaxValue, int.MinValue, 10, 10).AllocatedTiles == 0);
        });
    }
}
