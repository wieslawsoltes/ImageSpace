using System.Numerics;
using System.Text.Json;
using ImageSpace.Core;
using ImageSpace.Imaging;
using ImageSpace.Editing;
using ImageSpace.Filters;
using ImageSpace.Documents;
using ImageSpace.Skia;
using SkiaSharp;

internal static class Program
{
    private static readonly List<(string Name,Action Test)> Tests=[];
    private static void Add(string name,Action test)=>Tests.Add((name,test));
    private static void Equal<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"Expected {expected}, got {actual}");}
    private static void True(bool value){if(!value)throw new Exception("Assertion failed");}
    private static void Throws(Action action){try{action();}catch{return;}throw new Exception("Expected an exception");}
    private static ImageDocument Doc(){var d=new ImageDocument(24,20,"Test");var l=Layer.Raster("Pixels",24,20);l.Pixels!.Set(3,4,new(100,50,200));d.Layers.Add(l);d.ActiveLayerId=l.Id;return d;}
    public static int Main()
    {
        Add("color: hex",()=>Equal(new Rgba32(170,187,204),Rgba32.Parse("#abc")));
        Add("color: alpha hex",()=>Equal((byte)128,Rgba32.Parse("10203080").A));
        Add("color: reject invalid",()=>Throws(()=>Rgba32.Parse("no")));
        Add("color: transparent over",()=>Equal(new Rgba32(30,40,50),Rgba32.Over(new(30,40,50),Rgba32.Transparent)));
        Add("color: opaque over",()=>Equal(Rgba32.White,Rgba32.Over(Rgba32.Black,Rgba32.White)));
        Add("color: half alpha",()=>Equal((byte)128,Rgba32.Over(Rgba32.Transparent,new(20,30,40,128)).A));
        Add("surface: limits",()=>Throws(()=>new PixelSurface(0,4)));
        Add("surface: pixel budget",()=>Throws(()=>new PixelSurface(8192,8192)));
        Add("surface: sparse",()=>Equal(0,new PixelSurface(1024,1024).AllocatedTiles));
        Add("surface: boundary",()=>{var p=new PixelSurface(129,129);p.Set(128,128,Rgba32.White);Equal(Rgba32.White,p.Get(128,128));Equal(Rgba32.Transparent,p.Get(-1,0));Equal(1,p.AllocatedTiles);});
        Add("surface: snapshot isolation",()=>{var p=new PixelSurface(4,4);p.Set(0,0,Rgba32.White);var q=p.Snapshot();p.Set(0,0,Rgba32.Black);Equal(Rgba32.White,q.Get(0,0));q.Set(1,1,Rgba32.White);Equal(Rgba32.Transparent,p.Get(1,1));});
        Add("surface: transitive snapshots",()=>{var a=new PixelSurface(4,4);a.Set(0,0,Rgba32.White);var b=a.Snapshot();var c=b.Snapshot();a.Set(0,0,Rgba32.Black);b.Set(0,0,new(255,0,0));Equal(Rgba32.White,c.Get(0,0));});
        Add("surface: raw roundtrip",()=>{var p=new PixelSurface(130,131);p.Set(129,130,new(1,2,3,4));var q=PixelSurface.FromRgba(130,131,p.ToRgba());Equal(p.Get(129,130),q.Get(129,130));});
        Add("surface: reject truncated raw",()=>Throws(()=>PixelSurface.FromRgba(2,2,new byte[4])));
        Add("surface: clear",()=>{var p=new PixelSurface(4,4);p.Fill(Rgba32.White);p.Fill(Rgba32.Transparent);Equal(0,p.AllocatedTiles);});
        Add("selection: rectangle",()=>{var p=Selections.Rectangle(10,10,new(2,3),new(5,7));Equal((byte)255,p.Get(2,3).A);Equal((byte)0,p.Get(5,7).A);Equal((2,3,3,4),Selections.Bounds(p)!.Value);});
        Add("selection: inverted drag",()=>Equal((2,3,3,4),Selections.Bounds(Selections.Rectangle(10,10,new(5,7),new(2,3)))!.Value));
        Add("selection: ellipse",()=>{var p=Selections.Rectangle(10,10,new(0,0),new(10,10),true);Equal((byte)0,p.Get(0,0).A);Equal((byte)255,p.Get(5,5).A);});
        Add("selection: lasso",()=>{var p=Selections.Polygon(12,12,[new(1,1),new(10,1),new(1,10)]);Equal((byte)255,p.Get(2,2).A);Equal((byte)0,p.Get(9,9).A);});
        Add("selection: empty versus none",()=>{var d=Doc();Equal(1f,d.Coverage(3,4));d.Selection=new(d.Width,d.Height);Equal(0f,d.Coverage(3,4));});
        Add("selection: subtract",()=>{var a=Selections.Rectangle(10,10,new(1,1),new(9,9));var b=Selections.Rectangle(10,10,new(3,3),new(6,6));var c=Selections.Combine(a,b,SelectionCombine.Subtract);Equal((byte)0,c.Get(4,4).A);Equal((byte)255,c.Get(2,2).A);});
        Add("selection: intersect",()=>{var a=Selections.Rectangle(10,10,new(1,1),new(6,6));var b=Selections.Rectangle(10,10,new(3,3),new(9,9));Equal((3,3,3,3),Selections.Bounds(Selections.Combine(a,b,SelectionCombine.Intersect))!.Value);});
        Add("selection: invert",()=>Equal((byte)255,Selections.Invert(new(3,3),3,3).Get(1,1).A));
        Add("selection: contiguous",()=>{var p=new PixelSurface(5,5);p.Fill(Rgba32.Black);for(var y=0;y<5;y++)p.Set(2,y,Rgba32.White);var s=Selections.Contiguous(p,0,0,0);Equal((byte)255,s.Get(1,4).A);Equal((byte)0,s.Get(4,4).A);});
        Add("raster: crop",()=>{var d=Doc();Equal(new Rgba32(100,50,200),RasterOperations.Crop(d.ActiveLayer!.Pixels!,3,4,2,2).Get(0,0));});
        Add("raster: rotate",()=>{var p=new PixelSurface(2,3);p.Set(0,0,Rgba32.White);var r=RasterOperations.Rotate90(p);Equal(3,r.Width);Equal(2,r.Height);Equal(Rgba32.White,r.Get(2,0));});
        Add("raster: flip",()=>{var p=new PixelSurface(2,3);p.Set(0,0,Rgba32.White);Equal(Rgba32.White,RasterOperations.Flip(p,true).Get(1,0));});
        Add("raster: resize premultiplied",()=>{var p=new PixelSurface(2,1);p.Set(0,0,new(255,0,0));var r=RasterOperations.Resize(p,3,1);Equal((byte)255,r.Get(1,0).R);True(r.Get(1,0).A>0);});
        Add("raster: histogram",()=>{var p=new PixelSurface(2,2);p.Fill(Rgba32.White);Equal(4,RasterOperations.Histogram(p)[255]);});
        Add("brush: paint",()=>{var d=Doc();var b=new BrushEngine();b.Begin(d.ActiveLayer!.Pixels!,Vector2.Zero);b.Paint(d,d.ActiveLayer,new(10,10),1,new(8,1,1,1,.1f,false),Rgba32.White,PaintMode.Brush);Equal(Rgba32.White,d.ActiveLayer.Pixels!.Get(10,10));});
        Add("brush: eraser",()=>{var d=Doc();d.ActiveLayer!.Pixels!.Fill(Rgba32.White);var b=new BrushEngine();b.Begin(d.ActiveLayer.Pixels,Vector2.Zero);b.Paint(d,d.ActiveLayer,new(10,10),1,new(8,1,1,1,.1f,false),Rgba32.White,PaintMode.Eraser);Equal((byte)0,d.ActiveLayer.Pixels.Get(10,10).A);});
        Add("brush: respects empty selection",()=>{var d=Doc();d.Selection=new(24,20);var b=new BrushEngine();b.Begin(d.ActiveLayer!.Pixels!,Vector2.Zero);b.Paint(d,d.ActiveLayer,new(10,10),1,new(8,1,1,1),Rgba32.White,PaintMode.Brush);Equal((byte)0,d.ActiveLayer.Pixels!.Get(10,10).A);});
        Add("brush: lock",()=>{var d=Doc();d.ActiveLayer!.Locked=true;var b=new BrushEngine();b.Begin(d.ActiveLayer.Pixels!,Vector2.Zero);b.Paint(d,d.ActiveLayer,new(10,10),1,new(8,1,1,1),Rgba32.White,PaintMode.Brush);Equal((byte)0,d.ActiveLayer.Pixels!.Get(10,10).A);});
        Add("history: undo redo",()=>{var s=new EditorSession(Doc());s.Execute("paint",d=>d.ActiveLayer!.Pixels!.Set(3,4,Rgba32.White));s.Undo();Equal(new Rgba32(100,50,200),s.Document.ActiveLayer!.Pixels!.Get(3,4));s.Redo();Equal(Rgba32.White,s.Document.ActiveLayer!.Pixels!.Get(3,4));});
        Add("history: cancellation",()=>{var s=new EditorSession(Doc());s.Begin("paint");s.Document.Name="Changed";s.Cancel();Equal("Test",s.Document.Name);True(!s.CanUndo);});
        Add("history: atomic failure",()=>{var s=new EditorSession(Doc());Throws(()=>s.Execute("bad",d=>{d.Name="Bad";throw new Exception();}));Equal("Test",s.Document.Name);});
        Add("history: saved marker",()=>{var s=new EditorSession(Doc());s.Execute("name",d=>d.Name="New");True(s.IsDirty);s.MarkSaved();True(!s.IsDirty);s.Undo();True(s.IsDirty);s.Redo();True(!s.IsDirty);});
        Add("history: redo discarded",()=>{var s=new EditorSession(Doc());s.AddLayer();s.Undo();s.AddLayer();True(!s.CanRedo);});
        Add("history: duplicate distinct ID",()=>{var s=new EditorSession(Doc());s.DuplicateLayer();Equal(2,s.Document.Layers.Select(l=>l.Id).Distinct().Count());});
        Add("history: mask",()=>{var s=new EditorSession(Doc());s.AddMask();Equal((byte)255,s.Document.ActiveLayer!.Mask!.Get(4,4).A);s.Undo();True(s.Document.ActiveLayer!.Mask is null);});
        Add("history: crop non-destructive",()=>{var s=new EditorSession(Doc());s.Crop(3,4,8,9);Equal(8,s.Document.Width);Equal(-3f,s.Document.ActiveLayer!.X);Equal(new Rgba32(100,50,200),s.Document.ActiveLayer.Pixels!.Get(3,4));});
        Add("history: image resize",()=>{var s=new EditorSession(Doc());s.ResizeImage(48,40);Equal(2f,s.Document.ActiveLayer!.ScaleX);s.Undo();Equal(24,s.Document.Width);});
        Add("history: rotate",()=>{var s=new EditorSession(Doc());s.RotateCanvas(true);Equal(20,s.Document.Width);Equal(24,s.Document.Height);Equal(90f,s.Document.ActiveLayer!.Rotation);});
        Add("filter: invert",()=>Equal(new Rgba32(155,205,55),FilterEngine.Apply(Doc().ActiveLayer!.Pixels!,FilterKind.Invert).Get(3,4)));
        Add("filter: identity brightness",()=>Equal(new Rgba32(100,50,200),FilterEngine.Apply(Doc().ActiveLayer!.Pixels!,FilterKind.BrightnessContrast).Get(3,4)));
        Add("filter: identity gamma",()=>Equal(new Rgba32(100,50,200),FilterEngine.Apply(Doc().ActiveLayer!.Pixels!,FilterKind.Gamma,1).Get(3,4)));
        Add("filter: zero blur",()=>Equal(new Rgba32(100,50,200),FilterEngine.Apply(Doc().ActiveLayer!.Pixels!,FilterKind.GaussianBlur,0).Get(3,4)));
        Add("filter: premultiplied blur",()=>{var p=new PixelSurface(5,5);p.Set(2,2,new(255,0,0));var q=FilterEngine.Blur(p,1);Equal((byte)255,q.Get(2,2).R);True(q.Get(2,2).A<255&&q.Get(2,2).A>0);});
        foreach(var kind in Enum.GetValues<FilterKind>()){var k=kind;Add("filter: valid output "+kind,()=>{var p=Doc().ActiveLayer!.Pixels!;var q=FilterEngine.Apply(p,k,2);Equal(p.Width,q.Width);Equal(p.Height,q.Height);True(q.ToRgba().Length==p.Width*p.Height*4);});}
        Add("archive: raster roundtrip",()=>{var d=Doc();var q=DocumentArchive.Load(DocumentArchive.Save(d));Equal(d.Name,q.Name);Equal(d.ActiveLayer!.Pixels!.Get(3,4),q.ActiveLayer!.Pixels!.Get(3,4));});
        Add("archive: text roundtrip",()=>{var d=Doc();d.Layers.Add(new(){Kind=LayerKind.Text,Text="Zażółć gęślą jaźń",Rotation=15,FontSize=42});var q=DocumentArchive.Load(DocumentArchive.Save(d));Equal(d.Layers[1].Text,q.Layers[1].Text);Equal(15f,q.Layers[1].Rotation);});
        Add("archive: mask roundtrip",()=>{var s=new EditorSession(Doc());s.AddMask();var q=DocumentArchive.Load(DocumentArchive.Save(s.Document));Equal((byte)255,q.ActiveLayer!.Mask!.Get(2,2).A);});
        Add("archive: corrupt rejected",()=>Throws(()=>DocumentArchive.Load(new byte[]{1,2,3})));
        Add("document: nonfinite rejected",()=>{var d=Doc();d.ActiveLayer!.Opacity=float.NaN;Throws(d.Validate);});
        Add("document: duplicate IDs rejected",()=>{var d=Doc();d.Layers.Add(d.Layers[0].Snapshot());Throws(d.Validate);});
        Add("psd: packbits",()=>{var row=new byte[5];PsdCodec.DecodeRow(new byte[]{254,42,1,1,2},row);True(row.SequenceEqual(new byte[]{42,42,42,1,2}));});
        Add("psd: reject malformed packbits",()=>Throws(()=>PsdCodec.DecodeRow(new byte[]{127},new byte[2])));
        Add("psd: layers roundtrip",()=>{var d=Doc();var bytes=PsdCodec.Save(d,l=>l.Pixels!,d.ActiveLayer!.Pixels!);var q=PsdCodec.Load(bytes).Document;Equal(1,q.Layers.Count);Equal(new Rgba32(100,50,200),q.ActiveLayer!.Pixels!.Get(3,4));});
        Add("psd: reject truncated",()=>Throws(()=>PsdCodec.Load(new byte[]{56,66,80,83})));
        Add("skia: raster composite",()=>{using var r=new ImageRenderer();Equal(new Rgba32(100,50,200),r.Rasterize(Doc()).Get(3,4));});
        Add("skia: PNG roundtrip",()=>{using var r=new ImageRenderer();var q=ImageRenderer.Decode(r.Export(Doc(),SKEncodedImageFormat.Png));Equal(new Rgba32(100,50,200),q.Get(3,4));});
        Add("skia: blend multiply",()=>{var d=new ImageDocument(4,4);var a=Layer.Raster("a",4,4);a.Pixels!.Fill(new(200,100,50));var b=Layer.Raster("b",4,4);b.Pixels!.Fill(new(128,128,128));b.Blend=LayerBlend.Multiply;d.Layers.AddRange([a,b]);using var r=new ImageRenderer();var c=r.Rasterize(d).Get(1,1);True(Math.Abs(c.R-100)<=1);});
        Add("skia: mask transparency",()=>{var d=Doc();d.ActiveLayer!.Mask=new(24,20);using var r=new ImageRenderer();Equal((byte)0,r.Rasterize(d).Get(3,4).A);});
        Add("skia: invert adjustment",()=>{var d=Doc();d.Layers.Add(new(){Kind=LayerKind.Adjustment,Adjustment=AdjustmentKind.Invert});using var r=new ImageRenderer();var c=r.Rasterize(d).Get(3,4);True(Math.Abs(c.R-155)<=1&&Math.Abs(c.G-205)<=1);});
        var results=new List<object>();var failed=0;foreach(var(name,test)in Tests){try{test();Console.WriteLine("PASS "+name);results.Add(new{name,passed=true,error=""});}catch(Exception ex){failed++;Console.Error.WriteLine("FAIL "+name+": "+ex);results.Add(new{name,passed=false,error=ex.ToString()});}}
        Directory.CreateDirectory("artifacts");File.WriteAllText("artifacts/engine-tests.json",JsonSerializer.Serialize(new{total=Tests.Count,passed=Tests.Count-failed,failed,results},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"{Tests.Count-failed}/{Tests.Count} tests passed");return failed==0?0:1;
    }
}
