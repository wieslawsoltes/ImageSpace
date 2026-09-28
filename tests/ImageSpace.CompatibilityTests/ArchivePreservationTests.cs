using ImageSpace.Core;
using ImageSpace.Documents;

internal static partial class Program
{
    static Program()
    {
        foreach (var mask in new[] { false, true })
        {
            var isMask = mask;
            Test($"native: streaming archive preserves hidden RGB in {(isMask ? "masks" : "pixels")}", () =>
            {
                var surface = new PixelSurface(129, 3);
                surface.Fill(Rgba32.White);
                surface.Set(128, 0, new Rgba32(77, 88, 99, 0));
                surface.WriteRow(127, 2, new byte[] { 12, 34, 56, 0, 90, 80, 70, 127 });
                var layer = Layer.Raster("Preserved bytes", surface.Width, surface.Height);
                if (isMask)
                    layer.Mask = surface;
                else
                    layer.Pixels = surface;
                var document = new ImageDocument(surface.Width, surface.Height)
                {
                    Layers = [layer],
                    ActiveLayerId = layer.Id
                };
                var restored = DocumentArchive.Load(DocumentArchive.Save(document)).ActiveLayer!;
                var actual = isMask ? restored.Mask! : restored.Pixels!;
                Check(surface.ToRgba().SequenceEqual(actual.ToRgba()),
                    "Native save/load must not erase hidden color channels.");
            });
        }
    }
}
