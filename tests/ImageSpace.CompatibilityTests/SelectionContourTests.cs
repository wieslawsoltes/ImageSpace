using ImageSpace.Core;
using ImageSpace.Imaging;

internal static partial class Program
{
    private static HashSet<SelectionEdge> ScalarEdges(PixelSurface surface, byte threshold = 128)
    {
        var result = new HashSet<SelectionEdge>();
        bool Inside(int x, int y) => surface.Get(x, y).A >= threshold;
        for (var y = 0; y < surface.Height; y++)
        for (var x = 0; x < surface.Width; x++)
        {
            if (!Inside(x, y))
                continue;
            if (!Inside(x - 1, y))
                result.Add(new(x, y, x, y + 1));
            if (!Inside(x + 1, y))
                result.Add(new(x + 1, y, x + 1, y + 1));
            if (!Inside(x, y - 1))
                result.Add(new(x, y, x + 1, y));
            if (!Inside(x, y + 1))
                result.Add(new(x, y + 1, x + 1, y + 1));
        }
        return result;
    }
    private static HashSet<SelectionEdge> StreamedEdges(PixelSurface surface, byte threshold = 128)
    {
        var result = new HashSet<SelectionEdge>();
        SelectionContours.Trace(surface, e =>
        {
            Check(e.X1 == e.X2 || e.Y1 == e.Y2, "Only axis-aligned segments are allowed.");
            if (e.X1 == e.X2)
            for (var y = e.Y1; y < e.Y2; y++)
                Check(result.Add(new(e.X1, y, e.X2, y + 1)), "Duplicate edge");
            else
            for (var x = e.X1; x < e.X2; x++)
                Check(result.Add(new(x, e.Y1, x + 1, e.Y2)), "Duplicate edge");
        }, threshold);
        return result;
    }
    private static void RegisterContourTests()
    {
        Test("contours: exhaustive 3x3 topology matches all scalar boundary edges", () =>
        {
            for (var bits = 0; bits < 512; bits++)
            {
                var surface = new PixelSurface(3, 3);
                for (var n = 0; n < 9; n++)
                if ((bits & (1 << n)) != 0)
                    surface.Set(n % 3, n / 3, Rgba32.White);
                Check(ScalarEdges(surface).SetEquals(StreamedEdges(surface)), $"Topology {bits}");
            }
        });
        foreach (var size in new[] { (1, 1), (1, 129), (129, 1), (129, 131), (259, 3) })
        {
            var shape = size;
            Test($"contours: seeded soft selection across tile edges {shape}", () =>
            {
                var surface = Seed(shape.Item1, shape.Item2);
                foreach (var threshold in new byte[] { 1, 64, 128, 255 })
                    Check(ScalarEdges(surface, threshold).SetEquals(StreamedEdges(surface, threshold)));
            });
        }
        Test("contours: full rectangular selection emits exactly four merged segments", () =>
        {
            var surface = new PixelSurface(4096, 16);
            surface.Fill(Rgba32.White);
            var edges = new List<SelectionEdge>();
            var stats = SelectionContours.Trace(surface, edges.Add);
            Check(edges.Count == 4 && stats.Segments == 4 && stats.Samples == 4096 * 16);
            Check(stats.ScratchBytes == 4096 * 12 + 4);
        });
        Test("contours: empty sparse selection emits nothing", () =>
        {
            var stats = SelectionContours.Trace(new PixelSurface(259, 131), _ => throw new Exception("Unexpected edge."));
            Check(stats.Segments == 0);
        });
        Test("contours: holes and disconnected islands retain exact boundaries", () =>
        {
            var surface = new PixelSurface(19, 17);
            surface.Fill(Rgba32.White);
            for (var y = 2; y < 15; y++)
            for (var x = 2; x < 17; x++)
                surface.Set(x, y, Rgba32.Transparent);
            surface.Set(8, 8, Rgba32.White);
            surface.Set(9, 9, Rgba32.White);
            Check(ScalarEdges(surface).SetEquals(StreamedEdges(surface)));
        });
        Test("contours: threshold zero rejected", () =>
            Throws<ArgumentOutOfRangeException>(() => SelectionContours.Trace(new PixelSurface(1, 1), _ => { }, 0)));
        Test("contours: cancellation is honored before work", () =>
            Throws<OperationCanceledException>(() => SelectionContours.Trace(new PixelSurface(2, 2), _ => { }, 128, new CancellationToken(true))));
        Test("contours: midstream cancellation releases scratch buffers", () =>
        {
            var surface = new PixelSurface(129, 131);
            surface.Fill(Rgba32.White);
            using var token = new CancellationTokenSource();
            Throws<OperationCanceledException>(() => SelectionContours.Trace(surface, _ => token.Cancel(), 128, token.Token));
            Check(StreamedEdges(surface).SetEquals(ScalarEdges(surface)));
        });
        Test("contours: receiver exceptions propagate without mutating source", () =>
        {
            var surface = Seed(131, 129);
            var revision = surface.Revision;
            var bytes = surface.ToRgba();
            Throws<FormatException>(() => SelectionContours.Trace(surface, _ => throw new FormatException("receiver")));
            Check(surface.Revision == revision && surface.ToRgba().SequenceEqual(bytes));
        });
    }
}
