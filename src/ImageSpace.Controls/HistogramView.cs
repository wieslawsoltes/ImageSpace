using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;
namespace ImageSpace.Controls;
public sealed class HistogramView:SKCanvasElement
{
    private int[] _values=new int[256];public int[] Values{get=>_values;set{_values=value;Invalidate();}}
    public HistogramView(){Height=75;}
    protected override void RenderOverride(SKCanvas c,Size area)
    {
        c.Clear(new SKColor(34,34,34));var max=Math.Max(1,_values.Max());using var p=new SKPaint{IsAntialias=true,Color=new(151,163,177)};using var path=new SKPath();path.MoveTo(0,(float)area.Height);
        for(var i=0;i<Math.Min(256,_values.Length);i++)path.LineTo((float)area.Width*i/255,(float)(area.Height*(1-Math.Sqrt((double)_values[i]/max))));path.LineTo((float)area.Width,(float)area.Height);path.Close();c.DrawPath(path,p);
        p.Color=new(80,80,80);p.StrokeWidth=1;for(var i=1;i<4;i++)c.DrawLine((float)area.Width*i/4,0,(float)area.Width*i/4,(float)area.Height,p);
    }
}
