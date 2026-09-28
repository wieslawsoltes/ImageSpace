using ImageSpace.Skia;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
namespace ImageSpace.Workbench;

public sealed class LayerThumbnail : SKCanvasElement
{
    public Layer? Layer
    {
        get; set;
    }
    public bool ShowMask
    {
        get; set;
    }
    public ImageRenderer? Renderer
    {
        get; set;
    }
    private LayerRenderStamp? _stamp;
    private long _fontRevision = -1;
    private static long _renders;
    public static long RenderCount => Interlocked.Read(ref _renders);
    public void Bind(Layer layer, ImageRenderer renderer)
    {
        var stamp = LayerRenderStamp.Capture(layer);
        var changed = _stamp != stamp || !ReferenceEquals(Renderer, renderer) || _fontRevision != renderer.TypefaceRevision;
        Layer = layer;
        Renderer = renderer;
        _stamp = stamp;
        _fontRevision = renderer.TypefaceRevision;
        if (changed) Invalidate();
    }
    public LayerThumbnail()
    {
        Width = 44;
        Height = 30;
        IsHitTestVisible = false;
    }
    protected override void RenderOverride(SKCanvas c, Size area)
    {
        Interlocked.Increment(ref _renders);
        c.Clear(new(180, 180, 180));
        using var paint = new SKPaint { Color = new(225, 225, 225) };
        for (var y = 0; y < area.Height; y += 5)
        for (var x = 0; x < area.Width; x += 5)
        if ((x / 5 + y / 5) % 2 == 0)
            c.DrawRect(x, y, 5, 5, paint);
        if (Layer is null || Renderer is null)
            return;
        c.Save();
        var width = Math.Max(1, Layer.Width);
        var height = Math.Max(1, Layer.Height);
        var scale = (float)Math.Min(area.Width / width, area.Height / height);
        c.Translate(((float)area.Width - width * scale) / 2, ((float)area.Height - height * scale) / 2);
        c.Scale(scale);
        if (ShowMask && Layer.Mask is not null)
        {
            c.Clear(SKColors.Black);
            Renderer.DrawTiles(c, Layer.Mask);
        }
        else
        {
            Renderer.DrawLayer(c, Layer, true, true, ignoreTransform: true);
        }
        c.Restore();
    }
}
