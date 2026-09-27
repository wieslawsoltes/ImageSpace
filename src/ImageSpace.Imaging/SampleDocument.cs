using ImageSpace.Core;
namespace ImageSpace.Imaging;

public static class SampleDocument
{
    /// <summary>Original procedural artwork. No Adobe assets, stock images or network dependency.</summary>
    public static ImageDocument Create()
    {
        const int w=1000,h=680;var d=new ImageDocument(w,h,"After the light");
        var sky=Layer.Raster("01  •  Evening sky",w,h);var ridges=Layer.Raster("02  •  Distant dunes",w,h);var foreground=Layer.Raster("03  •  Foreground",w,h);var random=new Random(137);
        for(var y=0;y<h;y++)for(var x=0;x<w;x++)
        {
            var t=y/(double)h;var c=Rgba32.Lerp(new(42,51,91),new(237,169,137),Math.Pow(t,.7));
            var glow=Math.Exp(-((x-725.0)*(x-725)+(y-258.0)*(y-258))/24000);c=Rgba32.Lerp(c,new(255,221,175),glow*.8);
            if((x-727)*(x-727)+(y-244)*(y-244)<61*61)c=new(255,222,168);
            var grain=(random.NextDouble()-.5)*3;sky.Pixels!.Set(x,y,new(Rgba32.Byte(c.R+grain),Rgba32.Byte(c.G+grain),Rgba32.Byte(c.B+grain)));
            var horizon=350+75*Math.Sin(x*.004+1)+35*Math.Sin(x*.009);
            if(y>horizon){var q=(y-horizon)/(h-horizon);var col=Rgba32.Lerp(new(144,91,118),new(65,69,94),q);ridges.Pixels!.Set(x,y,col);}
            var near=520+68*Math.Sin(x*.005-1.2);if(y>near){var q=(y-near)/(h-near);var col=Rgba32.Lerp(new(29,78,88),new(15,35,54),q);foreground.Pixels!.Set(x,y,col);}
        }
        d.Layers.AddRange([sky,ridges,foreground]);
        d.Layers.Add(new(){Name="FIELD NOTES",Kind=LayerKind.Text,Text="FIELD NOTES   /   VOL. 03",X=62,Y=44,FontSize=13,Width=310,Height=24,Color=new(241,227,207)});
        d.Layers.Add(new(){Name="After the light",Kind=LayerKind.Text,Text="AFTER\nTHE LIGHT",X=59,Y=129,FontSize=82,Bold=true,Width=540,Height=198,Color=new(249,237,213)});
        d.Layers.Add(new(){Name="Description",Kind=LayerKind.Text,Text="An exploration of color, form\nand the quiet in between.",X=64,Y=373,FontSize=16,Width=350,Height=60,Color=new(247,223,205)});
        d.Layers.Add(new(){Name="Footer rule",Kind=LayerKind.Rectangle,X=63,Y=602,Width=874,Height=1,Color=new(196,210,205,160)});
        d.Layers.Add(new(){Name="Footer",Kind=LayerKind.Text,Text="IMAGESPACE STUDIO                                      ORIGINAL LANDSCAPES / 2026",X=64,Y=619,FontSize=12,Width=900,Height=24,Color=new(217,229,218)});
        d.ActiveLayerId=d.Layers[4].Id;return d;
    }
}
