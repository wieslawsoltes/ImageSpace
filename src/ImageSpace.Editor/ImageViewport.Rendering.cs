using System.Numerics;
using Windows.Foundation;
using ImageSpace.Core;
using ImageSpace.Skia;
using SkiaSharp;
namespace ImageSpace.Editor;

public sealed partial class ImageViewport
{
    private PixelSurface? _selection;private long _selectionRevision=-1;private SKPath? _selectionPath;
    private void Render(SKCanvas c,Size area)
    {
        if(_fitPending&&area.Width>50&&area.Height>50)Fit();c.Clear(new SKColor(31,31,31));var d=_session.Document;
        var rect=new SKRect(Pan.X,Pan.Y,Pan.X+d.Width*Zoom,Pan.Y+d.Height*Zoom);
        using var paint=new SKPaint{IsAntialias=false,Color=new SKColor(15,15,15)};c.DrawRect(new(rect.Left+4,rect.Top+5,rect.Right+5,rect.Bottom+6),paint);
        c.Save();c.ClipRect(rect);paint.Color=new(204,204,204);c.DrawRect(rect,paint);paint.Color=new(242,242,242);
        for(var y=rect.Top;y<rect.Bottom;y+=12)for(var x=rect.Left;x<rect.Right;x+=12)if(((int)((x-rect.Left)/12)+(int)((y-rect.Top)/12))%2==0)c.DrawRect(x,y,12,12,paint);
        c.Translate(Pan.X,Pan.Y);c.Scale(Zoom);Renderer.Draw(c,d);
        if(ShowGrid&&Zoom>4){paint.Color=new(0,0,0,60);paint.StrokeWidth=1/Zoom;for(var x=0;x<d.Width;x++)c.DrawLine(x,0,x,d.Height,paint);for(var y=0;y<d.Height;y++)c.DrawLine(0,y,d.Width,y,paint);}
        DrawSelection(c,d);c.Restore();
        if(ShowTransform&&Tool==EditorTool.Move&&d.ActiveLayer is{} layer&&layer.Kind!=LayerKind.Adjustment)DrawHandles(c,layer);
        if(_gesture=="selection")
        {
            using var path=new SKPath();if(Tool==EditorTool.Lasso&&_lasso.Count>0){var p=ToScreen(_lasso[0]);path.MoveTo(p.X,p.Y);foreach(var item in _lasso.Skip(1)){p=ToScreen(item);path.LineTo(p.X,p.Y);}path.Close();}
            else{var a=ToScreen(_start);var b=ToScreen(_last);var bounds=new SKRect(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y));if(Tool==EditorTool.EllipseSelect)path.AddOval(bounds);else path.AddRect(bounds);}Ants(c,path,1);
        }
        if(_crop is{} crop)
        {
            var a=ToScreen(crop.A);var b=ToScreen(crop.B);var r=new SKRect(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y));paint.Style=SKPaintStyle.Stroke;paint.Color=SKColors.White;paint.StrokeWidth=1;c.DrawRect(r,paint);
            for(var i=1;i<3;i++){c.DrawLine(r.Left+r.Width*i/3,r.Top,r.Left+r.Width*i/3,r.Bottom,paint);c.DrawLine(r.Left,r.Top+r.Height*i/3,r.Right,r.Top+r.Height*i/3,paint);}
        }
        if(_gesture=="gradient"){var a=ToScreen(_start);var b=ToScreen(_last);paint.Style=SKPaintStyle.Stroke;paint.Color=SKColors.White;paint.StrokeWidth=1;c.DrawLine(a.X,a.Y,b.X,b.Y,paint);c.DrawCircle(a.X,a.Y,4,paint);c.DrawCircle(b.X,b.Y,4,paint);}
        if(Tool is EditorTool.Brush or EditorTool.Pencil or EditorTool.Eraser or EditorTool.Clone or EditorTool.Dodge or EditorTool.Burn or EditorTool.Smudge)
        {paint.Style=SKPaintStyle.Stroke;paint.StrokeWidth=1;paint.Color=SKColors.White;c.DrawCircle(_cursor.X,_cursor.Y,Math.Max(2,Brush.Size*Zoom/2),paint);paint.Color=new(0,0,0,170);c.DrawCircle(_cursor.X,_cursor.Y,Math.Max(3,Brush.Size*Zoom/2+1),paint);}
        if(ShowRulers)DrawRulers(c,area);
    }
    private void DrawSelection(SKCanvas c,ImageDocument d)
    {
        var mask=d.Selection;if(mask is null)return;
        if(!ReferenceEquals(mask,_selection)||mask.Revision!=_selectionRevision)
        {
            _selection=mask;_selectionRevision=mask.Revision;_selectionPath?.Dispose();_selectionPath=new SKPath();var data=mask.ToRgba();var w=mask.Width;var h=mask.Height;
            for(var y=0;y<h;y++)for(var x=0;x<w;x++)
            {
                if(data[(y*w+x)*4+3]<128)continue;
                if(x==0||data[(y*w+x-1)*4+3]<128)Edge(x,y,x,y+1);
                if(y==0||data[((y-1)*w+x)*4+3]<128)Edge(x,y,x+1,y);
                if(x==w-1||data[(y*w+x+1)*4+3]<128)Edge(x+1,y,x+1,y+1);
                if(y==h-1||data[((y+1)*w+x)*4+3]<128)Edge(x,y+1,x+1,y+1);
            }
            void Edge(float x,float y,float xx,float yy){_selectionPath.MoveTo(x,y);_selectionPath.LineTo(xx,yy);}
        }
        if(_selectionPath is not null)Ants(c,_selectionPath,1/Zoom);
    }
    private void Ants(SKCanvas c,SKPath path,float scale)
    {
        using var p=new SKPaint{Color=SKColors.Black,Style=SKPaintStyle.Stroke,StrokeWidth=scale,IsAntialias=false};c.DrawPath(path,p);p.Color=SKColors.White;using var dash=SKPathEffect.CreateDash([4*scale,4*scale],_dashOffset*scale);p.PathEffect=dash;c.DrawPath(path,p);
    }
    private void DrawHandles(SKCanvas c,Layer layer)
    {
        using var p=new SKPaint{Color=new(97,167,236),StrokeWidth=1,IsAntialias=true,Style=SKPaintStyle.Stroke};using var outline=new SKPath();
        var dimensions=new Vector2(layer.Width,layer.Height);var corners=new[]{Vector2.Zero,new Vector2(1,0),Vector2.One,new Vector2(0,1)};for(var i=0;i<4;i++){var point=ToScreen(layer.ToDocument(corners[i]*dimensions));if(i==0)outline.MoveTo(point.X,point.Y);else outline.LineTo(point.X,point.Y);}outline.Close();c.DrawPath(outline,p);
        foreach(var handle in Handles){var point=ToScreen(layer.ToDocument(handle*dimensions));p.Style=SKPaintStyle.Fill;p.Color=new(39,39,39);c.DrawRect(point.X-3,point.Y-3,6,6,p);p.Style=SKPaintStyle.Stroke;p.Color=new(140,193,245);c.DrawRect(point.X-3,point.Y-3,6,6,p);}
        var top=ToScreen(layer.ToDocument(new(layer.Width/2,0)));c.DrawLine(top.X,top.Y,top.X,top.Y-25,p);c.DrawCircle(top.X,top.Y-28,3,p);
    }
    private void DrawRulers(SKCanvas c,Size area)
    {
        using var p=new SKPaint{Color=new(51,51,51)};c.DrawRect(0,0,(float)area.Width,20,p);c.DrawRect(0,0,20,(float)area.Height,p);using var font=new SKFont(SKTypeface.Default,9);p.Color=new(159,159,159);p.StrokeWidth=1;
        var step=Zoom<.2f?500:Zoom<.5f?200:Zoom<1.5f?100:Zoom<4?50:10;
        for(var x=0;x<=_session.Document.Width;x+=step){var screen=ToScreen(new(x,0)).X;if(screen<20||screen>area.Width)continue;c.DrawLine(screen,14,screen,20,p);c.DrawText(x.ToString(),screen+3,10,SKTextAlign.Left,font,p);}
        for(var y=0;y<=_session.Document.Height;y+=step){var screen=ToScreen(new(0,y)).Y;if(screen<20||screen>area.Height)continue;c.DrawLine(14,screen,20,screen,p);c.Save();c.Translate(10,screen+3);c.RotateDegrees(-90);c.DrawText(y.ToString(),0,0,SKTextAlign.Right,font,p);c.Restore();}
    }
}
