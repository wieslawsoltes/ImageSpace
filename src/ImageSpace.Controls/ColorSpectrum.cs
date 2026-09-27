using ImageSpace.Core;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
namespace ImageSpace.Controls;

public sealed class ColorSpectrum : SKCanvasElement
{
    private float _hue = 202, _saturation = .73f, _value = .92f; private bool _dragging;
    public event Action<Rgba32>? ColorChanged;
    public ColorSpectrum()
    {
        Height = 140;
        PointerPressed += (_, e) => { _dragging = true; CapturePointer(e.Pointer); Pick(e.GetCurrentPoint(this).Position); e.Handled = true; };
        PointerMoved += (_, e) => { if (_dragging) Pick(e.GetCurrentPoint(this).Position); };
        PointerReleased += (_, e) => { _dragging = false; ReleasePointerCapture(e.Pointer); };
        PointerCanceled += (_, _) => _dragging = false;
        PointerCaptureLost += (_, _) => _dragging = false;
    }
    private void Pick(Point p)
    {
        if (p.Y > ActualHeight - 18)
            _hue = (float)Math.Clamp(p.X / ActualWidth, 0, 1) * 359.9f;
        else
        {
            _saturation = (float)Math.Clamp(p.X / ActualWidth, 0, 1);
            _value = 1 - (float)Math.Clamp(p.Y / (ActualHeight - 25), 0, 1);
        }
        ColorChanged?.Invoke(Hsv(_hue, _saturation, _value));
        Invalidate();
    }
    protected override void RenderOverride(SKCanvas c, Size area)
    {
        var w = (float)area.Width;
        var h = (float)area.Height - 25;
        using var p = new SKPaint { IsAntialias = true };
        p.Color = ToSk(Hsv(_hue, 1, 1));
        c.DrawRect(0, 0, w, h, p);
        using (var shader = SKShader.CreateLinearGradient(new(0, 0), new(w, 0), [SKColors.White, SKColors.Transparent], SKShaderTileMode.Clamp))
        {
            p.Shader = shader;
            c.DrawRect(0, 0, w, h, p);
        }
        p.Shader = null;
        using (var shader = SKShader.CreateLinearGradient(new(0, 0), new(0, h), [SKColors.Transparent, SKColors.Black], SKShaderTileMode.Clamp))
        {
            p.Shader = shader;
            c.DrawRect(0, 0, w, h, p);
        }
        p.Shader = null;
        for (var x = 0; x < w; x += 2)
        {
            p.Color = ToSk(Hsv(x / w * 360, 1, 1));
            c.DrawRect(x, h + 8, 2, 13, p);
        }
        p.Style = SKPaintStyle.Stroke;
        p.StrokeWidth = 1.5f;
        p.Color = SKColors.White;
        c.DrawCircle(_saturation * w, (1 - _value) * h, 4, p);
        c.DrawRect(_hue / 360 * w - 2, h + 7, 4, 15, p);
    }
    public static Rgba32 Hsv(float h, float s, float v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (rgbR, rgbG, rgbB) = h switch
        {
            < 60 => (c, x, 0f),
            < 120 => (x, c, 0f),
            < 180 => (0f, c, x),
            < 240 => (0f, x, c),
            < 300 => (x, 0f, c),
            _ => (c, 0f, x)
        };
        return new(Rgba32.Byte((rgbR + m) * 255), Rgba32.Byte((rgbG + m) * 255), Rgba32.Byte((rgbB + m) * 255));
    }
    private static SKColor ToSk(Rgba32 c) => new(c.R, c.G, c.B, c.A);
}
