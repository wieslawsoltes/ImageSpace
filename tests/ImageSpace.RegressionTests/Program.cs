using System.Text.Json;
using ImageSpace.Core;
using ImageSpace.Imaging;
using ImageSpace.Skia;
using ImageSpace.Editing;
using ImageSpace.Documents;
using SkiaSharp;

var tests=new List<(string Name,Action Test)>();
void Check(bool condition,string message="Assertion failed"){if(!condition)throw new Exception(message);}
void Test(string name,Action test)=>tests.Add((name,test));
ImageDocument Document(int width=260,int height=130){var d=new ImageDocument(width,height);var l=Layer.Raster("Pixels",width,height);l.Pixels!.Set(2,2,Rgba32.White);l.Pixels.Set(width-1,height-1,new(30,90,140));d.Layers.Add(l);d.ActiveLayerId=l.Id;return d;}
Test("renderer refreshes a mutated tile without requiring a snapshot",()=>{var d=Document();using var renderer=new ImageRenderer();Check(renderer.Rasterize(d).Get(2,2)==Rgba32.White);d.ActiveLayer!.Pixels!.Set(2,2,new(255,0,0));Check(renderer.Rasterize(d).Get(2,2)==new Rgba32(255,0,0));});
Test("renderer retains unchanged tile uploads",()=>{var d=Document();using var renderer=new ImageRenderer();renderer.Rasterize(d);var uploads=renderer.TileUploads;renderer.Rasterize(d);Check(renderer.TileUploads==uploads);d.ActiveLayer!.Pixels!.Set(2,2,Rgba32.Black);renderer.Rasterize(d);Check(renderer.TileUploads==uploads+1);});
Test("renderer restores old tile generations on undo",()=>{var s=new EditorSession(Document());using var renderer=new ImageRenderer();renderer.Rasterize(s.Document);s.Execute("Paint",d=>d.ActiveLayer!.Pixels!.Set(2,2,Rgba32.Black));Check(renderer.Rasterize(s.Document).Get(2,2)==Rgba32.Black);s.Undo();Check(renderer.Rasterize(s.Document).Get(2,2)==Rgba32.White);s.Redo();Check(renderer.Rasterize(s.Document).Get(2,2)==Rgba32.Black);});
Test("mask updates invalidate their own cached image",()=>{var d=Document(16,16);d.ActiveLayer!.Mask=new(16,16);d.ActiveLayer.Mask.Fill(Rgba32.White);using var renderer=new ImageRenderer();Check(renderer.Rasterize(d).Get(2,2).A==255);d.ActiveLayer.Mask.Set(2,2,Rgba32.Transparent);Check(renderer.Rasterize(d).Get(2,2).A==0);});
Test("tile revisions remain isolated across snapshots",()=>{var s=new PixelSurface(8,8);s.Set(0,0,Rgba32.White);var clone=s.Snapshot();var revision=clone.GetTileRevision(0,0);s.Set(0,0,Rgba32.Black);Check(clone.GetTileRevision(0,0)==revision);Check(s.GetTileRevision(0,0)>revision);});
Test("setting identical bytes avoids redundant revisions",()=>{var s=new PixelSurface(8,8);s.Set(0,0,Rgba32.White);var revision=s.Revision;s.Set(0,0,Rgba32.White);Check(s.Revision==revision);});
Test("mask outside extent clears the layer",()=>{var d=Document(16,16);d.ActiveLayer!.Pixels!.Fill(Rgba32.White);d.ActiveLayer.Mask=new(4,4);d.ActiveLayer.Mask.Fill(Rgba32.White);using var renderer=new ImageRenderer();var p=renderer.Rasterize(d);Check(p.Get(2,2).A==255);Check(p.Get(8,8).A==0);});
Test("adjustment opacity zero is identity",()=>{var d=Document(16,16);d.Layers.Add(new(){Kind=LayerKind.Adjustment,Adjustment=AdjustmentKind.Invert,Opacity=0});using var renderer=new ImageRenderer();Check(renderer.Rasterize(d).Get(2,2)==Rgba32.White);});
Test("export does not mutate the editable document",()=>{var d=SampleDocument.Create();using var renderer=new ImageRenderer();var archive=DocumentArchive.Save(d);renderer.Export(d,SKEncodedImageFormat.Png);Check(DocumentArchive.Load(archive).Layers.Count==d.Layers.Count);Check(d.ActiveLayer!.Kind==LayerKind.Text);});
Test("sample composes visible artwork",()=>{var d=SampleDocument.Create();using var renderer=new ImageRenderer();var pixels=renderer.Rasterize(d);Check(pixels.Get(500,650).A==255);Check(pixels.Get(10,10)!=pixels.Get(900,500));});
var failed=0;var results=new List<object>();foreach(var(name,test)in tests){try{test();Console.WriteLine("PASS "+name);results.Add(new{name,passed=true});}catch(Exception ex){failed++;Console.Error.WriteLine("FAIL "+name+": "+ex);results.Add(new{name,passed=false,error=ex.ToString()});}}
Directory.CreateDirectory("artifacts");File.WriteAllText("artifacts/regression-tests.json",JsonSerializer.Serialize(new{total=tests.Count,passed=tests.Count-failed,failed,results},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"{tests.Count-failed}/{tests.Count} regression tests passed");return failed==0?0:1;
