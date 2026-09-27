using ImageSpace.Core;
using ImageSpace.Documents;
using ImageSpace.Skia;
using SkiaSharp;
using System.Text.Json;

var tests = new List<(string Name, Action Body)>();
void Test(string name, Action body) => tests.Add((name, body));
void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Near(Rgba32 actual, Rgba32 expected, int tolerance = 2)
{
    Check(Math.Abs(actual.R-expected.R)<=tolerance && Math.Abs(actual.G-expected.G)<=tolerance &&
        Math.Abs(actual.B-expected.B)<=tolerance && Math.Abs(actual.A-expected.A)<=tolerance,
        $"Expected {expected}; got {actual}");
}
ImageDocument Create(byte alpha = 255)
{
    var d = new ImageDocument(32, 16);
    var l = Layer.Raster("Pixels", 32, 16); l.Pixels!.Fill(new(80,120,160,alpha));
    var adjustment = new Layer { Name="Invert", Kind=LayerKind.Adjustment, Adjustment=AdjustmentKind.Invert,
        Width=32, Height=16, Mask=new(32,16) };
    for(var y=0;y<16;y++) for(var x=16;x<32;x++) adjustment.Mask.Set(x,y,Rgba32.White);
    d.Layers.AddRange([l,adjustment]); d.ActiveLayerId=adjustment.Id; return d;
}
Test("mask: only selected pixels receive the adjustment", () =>
{
    var d=Create(); using var r=new ImageRenderer(); var p=r.Rasterize(d);
    Near(p.Get(6,8),new(80,120,160),0); Near(p.Get(24,8),new(175,135,95),0);
});
foreach(var alpha in new byte[]{0,32,128,255})
foreach(var coverage in new byte[]{0,64,128,255})
{
    var a=alpha;var m=coverage;
    Test($"mask: preserves premultiplied alpha {a} at coverage {m}",()=>
    {
        var d=Create(a); d.ActiveLayer!.Mask!.Fill(new(255,255,255,m));
        using var r=new ImageRenderer(); var original=r.Rasterize(new ImageDocument(32,16){Layers=[d.Layers[0]]}).Get(8,8);
        var expected=Rgba32.Lerp(original,new((byte)(255-original.R),(byte)(255-original.G),(byte)(255-original.B),a),m/255.0);
        var actual=r.Rasterize(d).Get(8,8);
        if(a==0) Check(actual.A==0); else Near(actual,expected,a<64?8:3);
        Check(Math.Abs(actual.A-a)<=1,"Color-only masked adjustment must preserve input alpha.");
    });
}
Test("mask: density zero restores the complete effect",()=>
{
    var d=Create(); d.ActiveLayer!.MaskDensity=0; using var r=new ImageRenderer();Near(r.Rasterize(d).Get(6,8),new(175,135,95),0);
});
Test("mask: density half interpolates hidden areas",()=>
{
    var d=Create(); d.ActiveLayer!.MaskDensity=.5f; using var r=new ImageRenderer();Near(r.Rasterize(d).Get(6,8),new(128,128,128));
});
Test("mask: disabled bypasses authored coverage",()=>
{
    var d=Create(); d.ActiveLayer!.MaskEnabled=false; using var r=new ImageRenderer(); Near(r.Rasterize(d).Get(6,8),new(175,135,95),0);
});
Test("mask: layer opacity multiplies mask coverage",()=>
{
    var d=Create(); d.ActiveLayer!.Opacity=.5f; using var r=new ImageRenderer();var p=r.Rasterize(d);
    Near(p.Get(6,8),new(80,120,160),0); Near(p.Get(24,8),new(128,128,128));
});
Test("mask: feather softens without modifying source coverage",()=>
{
    var d=Create();d.ActiveLayer!.MaskFeather=2;using var r=new ImageRenderer();var p=r.Rasterize(d);
    Check(p.Get(15,8).R>80 && p.Get(15,8).R<175);Check(p.Get(16,8).R>p.Get(15,8).R);
    Check(d.ActiveLayer.Mask!.Get(15,8).A==0 && d.ActiveLayer.Mask.Get(16,8).A==255);
});
Test("mask: transformed adjustment coverage follows layer coordinates",()=>
{
    var d=Create(); d.ActiveLayer!.X=-8;using var r=new ImageRenderer();var p=r.Rasterize(d);
    Near(p.Get(6,8),new(80,120,160));Near(p.Get(12,8),new(175,135,95));Near(p.Get(28,8),new(80,120,160));
});
Test("mask: rotated coverage remains aligned",()=>
{
    var d=Create();d.ActiveLayer!.Rotation=180;d.ActiveLayer.X=32;d.ActiveLayer.Y=16;
    using var r=new ImageRenderer();var p=r.Rasterize(d);Near(p.Get(6,8),new(175,135,95));Near(p.Get(24,8),new(80,120,160));
});
Test("mask: layers above adjustment remain untouched",()=>
{
    var d=Create();var top=Layer.Raster("Top",32,16);top.Pixels!.Fill(new(20,40,60));d.Layers.Add(top);
    using var r=new ImageRenderer();Near(r.Rasterize(d).Get(24,8),new(20,40,60),0);
});
Test("mask: multiple masked adjustments compose in painter order",()=>
{
    var d=Create();var l=d.ActiveLayer!.Snapshot();l.Id=Guid.NewGuid();d.Layers.Add(l);
    using var r=new ImageRenderer();Near(r.Rasterize(d).Get(24,8),new(80,120,160),0);
});
Test("mask: changed coverage rebuilds only its filter graph",()=>
{
    var d=Create();using var r=new ImageRenderer();r.Rasterize(d);var builds=r.MaskFilterBuilds;var uploads=r.TileUploads;
    r.Rasterize(d);Check(r.MaskFilterBuilds==builds && r.TileUploads==uploads);
    d.ActiveLayer!.Mask!.Set(6,8,Rgba32.White);Near(r.Rasterize(d).Get(6,8),new(175,135,95),0);
    Check(r.MaskFilterBuilds==builds+1 && r.TileUploads==uploads+1);
});
Test("mask: raster density does not change source pixels",()=>
{
    var d=Create();d.Layers.RemoveAt(1);var l=d.Layers[0];l.Mask=new(32,16);l.MaskDensity=.5f;
    using var r=new ImageRenderer();Near(r.Rasterize(d).Get(8,8),new(80,120,160,128));Check(l.Pixels!.Get(8,8).A==255);
});
Test("mask: raster feather affects alpha not RGB",()=>
{
    var d=Create();var mask=d.ActiveLayer!.Mask;d.Layers.RemoveAt(1);d.Layers[0].Mask=mask;d.Layers[0].MaskFeather=2;
    using var r=new ImageRenderer();var p=r.Rasterize(d).Get(15,8);Check(p.A>0 && p.A<255);Near(p,new(80,120,160,p.A),3);
});
Test("opacity: saturated effect is clamped before crossfade",()=>
{
    var d=Create();var l=d.ActiveLayer!;l.Mask=null;l.Adjustment=AdjustmentKind.BrightnessContrast;l.Amount=100;l.Opacity=.5f;
    using var r=new ImageRenderer();Near(r.Rasterize(d).Get(8,8),new(168,188,208));
});
Test("opacity: blur blends the full-radius effect",()=>
{
    var d=Create();d.Layers[0].Pixels!.Set(16,8,Rgba32.White);var l=d.ActiveLayer!;l.Mask=null;l.Adjustment=AdjustmentKind.GaussianBlur;l.Amount=4;
    using var r=new ImageRenderer();var full=r.Rasterize(d).Get(16,8);l.Opacity=0;var before=r.Rasterize(d).Get(16,8);
    l.Opacity=.5f;Near(r.Rasterize(d).Get(16,8),Rgba32.Lerp(before,full,.5),3);
});
Test("mask: native archive retains density and feather",()=>
{
    var d=Create();d.ActiveLayer!.MaskDensity=.42f;d.ActiveLayer.MaskFeather=3.5f;
    var q=DocumentArchive.Load(DocumentArchive.Save(d));Check(q.ActiveLayer!.MaskDensity==.42f && q.ActiveLayer.MaskFeather==3.5f);
    using var r=new ImageRenderer();Near(r.Rasterize(d).Get(15,8),r.Rasterize(q).Get(15,8),0);
});
foreach(var invalid in new[]{float.NaN,float.PositiveInfinity,-1f,33f})
{
    var v=invalid;Test($"mask: invalid feather {v} rejected",()=>
    {
        var d=Create();d.ActiveLayer!.MaskFeather=v;var rejected=false;try{d.Validate();}catch(InvalidDataException){rejected=true;}Check(rejected);
    });
}
var results=new List<object>();var failed=0;
foreach(var (name,body) in tests)
{
    try{body();Console.WriteLine("PASS "+name);results.Add(new{name,passed=true});}
    catch(Exception error){failed++;Console.Error.WriteLine("FAIL "+name+": "+error);results.Add(new{name,passed=false,error=error.ToString()});}
}
Directory.CreateDirectory("artifacts");File.WriteAllText("artifacts/mask-tests.json",JsonSerializer.Serialize(new{total=tests.Count,passed=tests.Count-failed,failed,results},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{tests.Count-failed}/{tests.Count} mask tests passed");return failed==0?0:1;
