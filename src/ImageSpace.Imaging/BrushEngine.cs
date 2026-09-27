using System.Numerics;
using ImageSpace.Core;
namespace ImageSpace.Imaging;

public enum PaintMode
{
    Brush, Pencil, Eraser, Clone, Dodge, Burn, Smudge
}
public sealed record BrushSettings(float Size = 40, float Hardness = 0.7f, float Opacity = 1, float Flow = 0.35f, float Spacing = 0.12f, bool Pressure = true);
public sealed class BrushEngine
{
    private Vector2? _previous; private float _carry;
    private PixelSurface? _source; private Vector2 _cloneOffset;
    public void Begin(PixelSurface target, Vector2 cloneOffset)
    {
        _previous = null;
        _carry = 0;
        _source = target.Snapshot();
        _cloneOffset = cloneOffset;
    }
    public void Paint(ImageDocument document, Layer layer, Vector2 position, float pressure, BrushSettings settings, Rgba32 color, PaintMode mode)
    {
        if (layer.Locked)
            return;
        var target = document.EditMask ? layer.Mask : layer.Pixels;
        if (target is null)
            return;
        var point = layer.ToLocal(position);
        var size = Math.Clamp(settings.Size, 1, 1024);
        var spacing = Math.Max(0.5f, size * Math.Clamp(settings.Spacing, 0.01f, 1));
        pressure = settings.Pressure ? Math.Clamp(pressure, 0.05f, 1) : 1;
        if (_previous is null)
            Dab(point, pressure);
        else
        {
            var delta = point - _previous.Value;
            var length = delta.Length();
            if (length > 0)
            {
                var direction = delta / length;
                var travel = spacing - _carry;
                while (travel <= length)
                {
                    Dab(_previous.Value + direction * travel, pressure);
                    travel += spacing;
                }
                _carry = (length + _carry) % spacing;
            }
        }
        _previous = point;
        void Dab(Vector2 p, float force)
        {
            var radius = Math.Max(0.5f, size * 0.5f * (settings.Pressure ? MathF.Sqrt(force) : 1));
            var minX = Math.Max(0, (int)MathF.Floor(p.X - radius));
            var maxX = Math.Min(target.Width - 1, (int)MathF.Ceiling(p.X + radius));
            var minY = Math.Max(0, (int)MathF.Floor(p.Y - radius));
            var maxY = Math.Min(target.Height - 1, (int)MathF.Ceiling(p.Y + radius));
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                var distance = Vector2.Distance(new(x + 0.5f, y + 0.5f), p) / radius;
                if (distance > 1)
                    continue;
                var hardness = mode == PaintMode.Pencil ? 1 : Math.Clamp(settings.Hardness, 0, 1);
                var edge = distance <= hardness ? 1 : Math.Clamp((1 - distance) / Math.Max(0.001f, 1 - hardness), 0, 1);
                var dp = layer.ToDocument(new(x + 0.5f, y + 0.5f));
                var coverage = document.Coverage((int)dp.X, (int)dp.Y);
                var alpha = Math.Clamp(edge * settings.Opacity * settings.Flow * force * coverage, 0, 1);
                if (alpha <= 0)
                    continue;
                var old = target.Get(x, y);
                var ink = color;
                if (document.EditMask)
                {
                    var luminance = (color.R * 0.2126f + color.G * 0.7152f + color.B * 0.0722f) / 255;
                    var a = Rgba32.Byte(old.A + (255 * luminance - old.A) * alpha);
                    target.Set(x, y, new(255, 255, 255, a));
                    continue;
                }
                if (mode == PaintMode.Eraser)
                {
                    target.Set(x, y, old.WithAlpha(Rgba32.Byte(old.A * (1 - alpha))));
                    continue;
                }
                if (mode == PaintMode.Clone && _source is not null)
                    ink = _source.Get((int)(x + _cloneOffset.X), (int)(y + _cloneOffset.Y));
                if (mode == PaintMode.Dodge)
                    ink = new(Rgba32.Byte(old.R * 1.15 + 10), Rgba32.Byte(old.G * 1.15 + 10), Rgba32.Byte(old.B * 1.15 + 10), old.A);
                if (mode == PaintMode.Burn)
                    ink = new(Rgba32.Byte(old.R * 0.85), Rgba32.Byte(old.G * 0.85), Rgba32.Byte(old.B * 0.85), old.A);
                if (mode == PaintMode.Smudge && _source is not null)
                    ink = _source.Get((int)(_previous?.X ?? p.X), (int)(_previous?.Y ?? p.Y));
                target.Set(x, y, mode is PaintMode.Dodge or PaintMode.Burn ? Rgba32.Lerp(old, ink, alpha) : Rgba32.Over(old, ink, alpha));
            }
        }
    }
    public void End()
    {
        _previous = null;
        _source = null;
    }
}
