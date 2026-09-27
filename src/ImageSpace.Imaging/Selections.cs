using System.Numerics;
using ImageSpace.Core;
namespace ImageSpace.Imaging;

public enum SelectionCombine { Replace, Add, Subtract, Intersect }
public static class Selections
{
    public static PixelSurface Rectangle(int width,int height,Vector2 a,Vector2 b,bool ellipse=false)
    {
        var mask=new PixelSurface(width,height);var x0=Math.Clamp((int)MathF.Floor(Math.Min(a.X,b.X)),0,width);var x1=Math.Clamp((int)MathF.Ceiling(Math.Max(a.X,b.X)),0,width);
        var y0=Math.Clamp((int)MathF.Floor(Math.Min(a.Y,b.Y)),0,height);var y1=Math.Clamp((int)MathF.Ceiling(Math.Max(a.Y,b.Y)),0,height);
        var rx=Math.Max(0.5f,(x1-x0)/2f);var ry=Math.Max(0.5f,(y1-y0)/2f);
        for(var y=y0;y<y1;y++)for(var x=x0;x<x1;x++)
            if(!ellipse || Math.Pow((x+0.5f-(x0+x1)/2f)/rx,2)+Math.Pow((y+0.5f-(y0+y1)/2f)/ry,2)<=1) mask.Set(x,y,Rgba32.White);
        return mask;
    }
    public static PixelSurface Polygon(int width,int height,IReadOnlyList<Vector2> points)
    {
        var mask=new PixelSurface(width,height);if(points.Count<3)return mask;
        var minY=Math.Max(0,(int)points.Min(p=>p.Y));var maxY=Math.Min(height-1,(int)Math.Ceiling(points.Max(p=>p.Y)));
        for(var y=minY;y<=maxY;y++)
        {
            var hits=new List<float>();for(int i=0,j=points.Count-1;i<points.Count;j=i++)
            {
                var a=points[i];var b=points[j];var scan=y+0.5f;
                if((a.Y>scan)!=(b.Y>scan))hits.Add(a.X+(scan-a.Y)*(b.X-a.X)/(b.Y-a.Y));
            }
            hits.Sort();for(var k=0;k+1<hits.Count;k+=2)for(var x=Math.Max(0,(int)Math.Ceiling(hits[k]-0.5));x<Math.Min(width,(int)Math.Ceiling(hits[k+1]-0.5));x++) mask.Set(x,y,Rgba32.White);
        }
        return mask;
    }
    public static PixelSurface Combine(PixelSurface? old,PixelSurface next,SelectionCombine mode)
    {
        if(mode==SelectionCombine.Replace || old is null)return next;
        if(old.Width!=next.Width||old.Height!=next.Height)throw new ArgumentException("Selection sizes differ.");
        var result=new PixelSurface(next.Width,next.Height);
        for(var y=0;y<next.Height;y++)for(var x=0;x<next.Width;x++)
        {
            var a=old.Get(x,y).A;var b=next.Get(x,y).A;
            var value=mode switch {SelectionCombine.Add=>Math.Max(a,b),SelectionCombine.Subtract=>Math.Max(0,a-b),_=>Math.Min(a,b)};
            if(value>0)result.Set(x,y,new(255,255,255,(byte)value));
        }
        return result;
    }
    public static PixelSurface Invert(PixelSurface? source,int width,int height)
    {
        var result=new PixelSurface(width,height);
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)result.Set(x,y,new(255,255,255,(byte)(255-(source?.Get(x,y).A??0))));return result;
    }
    public static PixelSurface Contiguous(PixelSurface source,int x,int y,int tolerance)
    {
        var result=new PixelSurface(source.Width,source.Height);
        if((uint)x>=(uint)source.Width||(uint)y>=(uint)source.Height)return result;
        tolerance=Math.Clamp(tolerance,0,255);var seed=source.Get(x,y);var seen=new byte[source.Width*source.Height];var queue=new Queue<int>();queue.Enqueue(y*source.Width+x);seen[y*source.Width+x]=1;
        while(queue.TryDequeue(out var index))
        {
            var px=index%source.Width;var py=index/source.Width;var c=source.Get(px,py);
            if(Math.Max(Math.Max(Math.Abs(c.R-seed.R),Math.Abs(c.G-seed.G)),Math.Max(Math.Abs(c.B-seed.B),Math.Abs(c.A-seed.A)))>tolerance)continue;
            result.Set(px,py,Rgba32.White); Visit(px-1,py);Visit(px+1,py);Visit(px,py-1);Visit(px,py+1);
        }
        return result;
        void Visit(int px,int py){if((uint)px>=(uint)source.Width||(uint)py>=(uint)source.Height)return;var i=py*source.Width+px;if(seen[i]!=0)return;seen[i]=1;queue.Enqueue(i);}
    }
    public static (int X,int Y,int Width,int Height)? Bounds(PixelSurface? source)
    {
        if(source is null)return null;var x0=source.Width;var y0=source.Height;var x1=-1;var y1=-1;
        foreach(var(tx,ty,data,_)in source.EnumerateTiles())
            for(var y=0;y<Math.Min(PixelSurface.TileSize,source.Height-ty);y++)for(var x=0;x<Math.Min(PixelSurface.TileSize,source.Width-tx);x++)
                if(data.Span[(y*PixelSurface.TileSize+x)*4+3]>0){x0=Math.Min(x0,tx+x);y0=Math.Min(y0,ty+y);x1=Math.Max(x1,tx+x);y1=Math.Max(y1,ty+y);}
        return x1<x0?null:(x0,y0,x1-x0+1,y1-y0+1);
    }
}
