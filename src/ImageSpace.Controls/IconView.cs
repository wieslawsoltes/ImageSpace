using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
namespace ImageSpace.Controls;

/// <summary>Original vector icon set; no proprietary icon font or Adobe artwork.</summary>
public sealed class IconView : SKCanvasElement
{
    private string _icon = "";
    private SKColor _tint = new(207, 207, 207);
    public string Icon
    {
        get => _icon;
        set
        {
            if (_icon == value)
                return;
            _icon = value;
            Invalidate();
        }
    }
    public SKColor Tint
    {
        get => _tint;
        set
        {
            if (_tint == value)
                return;
            _tint = value;
            Invalidate();
        }
    }
    public IconView(string icon)
    {
        Icon = icon;
        Width = 18;
        Height = 18;
        IsHitTestVisible = false;
    }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        canvas.Save();
        canvas.Scale((float)area.Width / 24, (float)area.Height / 24);
        using var p = new SKPaint { Color = Tint, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.55f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        var path = Icon switch
        {
            "move" => "M4 3 L4 19 L8 15 L12 22 L15 20 L11 13 L17 12 Z M17 3 L17 8 M14.5 5.5 L19.5 5.5",
            "marquee" => "M4 4 L8 4 M11 4 L15 4 M18 4 L20 4 L20 7 M20 10 L20 14 M20 17 L20 20 L17 20 M14 20 L10 20 M7 20 L4 20 L4 17 M4 14 L4 10 M4 7 L4 4",
            "ellipse-select" => "M12 3 C22 3 24 20 12 21 C0 20 2 3 12 3 Z",
            "lasso" => "M8 18 C-2 15 4 3 14 4 C23 4 24 13 15 16 C11 18 5 16 6 21 C7 23 12 21 10 19",
            "wand" => "M4 21 L18 7 M14 5 L19 10 M5 4 L5 9 M2.5 6.5 L7.5 6.5 M17 17 L17 22 M14.5 19.5 L19.5 19.5 M21 2 L21 5",
            "crop" => "M6 2 L6 18 L22 18 M2 6 L18 6 L18 22 M9 15 L21 3",
            "brush" => "M9 15 L19 3 Q23 0 21 6 L13 17 Z M10 15 C4 13 6 20 2 21 C9 23 14 20 10 15 Z",
            "pencil" => "M4 20 L5 15 L17 3 L21 7 L9 19 Z M15 5 L19 9",
            "eraser" => "M3 15 L14 3 L22 10 L12 21 L9 21 Z M8 10 L16 17 M12 21 L22 21",
            "clone" => "M7 17 L7 14 Q12 11 9 7 C6 0 18 0 15 7 Q12 11 17 14 L17 17 Z M4 17 L20 17 L21 21 L3 21 Z",
            "dodge" => "M10 4 C3 3 1 9 4 13 C7 18 16 14 16 9 C16 6 13 4 10 4 Z M14 14 L20 22",
            "burn" => "M7 20 C1 15 8 12 9 4 C17 7 13 13 17 11 C23 18 15 23 7 20 Z",
            "smudge" => "M5 17 L10 20 Q14 23 18 19 L20 12 Q20 9 17 10 L15 13 L16 4 Q15 1 13 4 L11 13 L10 8 Q8 6 7 9 L8 14 L5 12 Q2 12 5 17 Z",
            "gradient" => "M3 5 L21 5 L21 19 L3 19 Z M6 6 L6 18 M9 6 L9 18 M12 6 L12 18",
            "fill" => "M5 6 L14 15 L20 9 L11 1 Z M5 6 L2 10 L10 18 L14 15 M20 13 C15 18 16 22 20 22 C24 22 24 18 20 13 Z",
            "text" => "M4 5 L4 3 L20 3 L20 5 M12 3 L12 21 M8 21 L16 21",
            "rectangle" => "M4 4 L20 4 L20 20 L4 20 Z",
            "ellipse" => "M12 3 C24 3 24 21 12 21 C0 21 0 3 12 3 Z",
            "eyedropper" => "M4 20 L5 15 L16 4 L20 8 L9 19 Z M13 5 L19 11 M17 3 L21 7",
            "hand" => "M5 13 L5 7 Q6 4 8 7 L8 4 Q10 1 11 4 L11 3 Q13 1 14 4 L14 6 Q17 2 17 7 L17 12 L20 10 Q23 10 21 14 L17 21 L9 21 Z",
            "zoom" => "M10 3 C19 3 19 16 10 16 C1 16 1 3 10 3 Z M15 15 L22 22 M7 9 L13 9 M10 6 L10 12",
            "eye" => "M2 12 Q12 0 22 12 Q12 24 2 12 Z M12 8 C18 8 18 16 12 16 C6 16 6 8 12 8 Z",
            "hidden" => "M3 3 L21 21 M2 12 Q12 0 22 12 Q12 24 2 12 Z",
            "lock" => "M5 10 L19 10 L19 22 L5 22 Z M8 10 L8 6 C8 0 16 0 16 6 L16 10 M12 14 L12 18",
            "plus" => "M12 4 L12 20 M4 12 L20 12",
            "trash" => "M5 6 L19 6 M9 6 L9 3 L15 3 L15 6 M7 8 L8 21 L16 21 L17 8 M10 10 L10 18 M14 10 L14 18",
            "link" => "M9 15 L15 9 M9 7 L12 4 C17 -1 25 7 20 12 L17 15 M7 9 L4 12 C-1 17 7 25 12 20 L15 17",
            "unlink" => "M9 7 L12 4 C17 -1 25 7 20 12 L17 15 M7 9 L4 12 C-1 17 7 25 12 20 L15 17 M3 3 L21 21",
            "clipping" => "M6 4 L6 13 Q6 16 9 16 L19 16 M15 12 L19 16 L15 20",
            "mask" => "M3 5 L21 5 L21 19 L3 19 Z M12 8 C18 8 18 16 12 16 C6 16 6 8 12 8 Z",
            "adjust" => "M12 3 C24 3 24 21 12 21 C0 21 0 3 12 3 Z M12 3 L12 21 M14 5 L14 19 M16 6 L16 18",
            "undo" => "M8 6 L3 10 L8 14 M3 10 L15 10 C24 10 23 21 15 21",
            "redo" => "M16 6 L21 10 L16 14 M21 10 L9 10 C0 10 1 21 9 21",
            "folder" => "M3 6 L10 6 L12 9 L21 9 L21 21 L3 21 Z",
            "up" => "M5 15 L12 8 L19 15",
            "down" => "M5 8 L12 15 L19 8",
            "close" => "M6 6 L18 18 M18 6 L6 18",
            "save" => "M4 3 L18 3 L21 6 L21 21 L3 21 L3 3 Z M7 3 L7 10 L17 10 L17 3 M7 21 L7 14 L17 14 L17 21",
            "fit" => "M3 9 L3 3 L9 3 M15 3 L21 3 L21 9 M21 15 L21 21 L15 21 M9 21 L3 21 L3 15",
            _ => "M4 12 L20 12"
        };
        using var geometry = SKPath.ParseSvgPathData(path);
        if (geometry is not null)
            canvas.DrawPath(geometry, p);
        canvas.Restore();
    }
}
