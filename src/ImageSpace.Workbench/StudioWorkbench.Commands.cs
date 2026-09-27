using System.Numerics;
using ImageSpace.Documents;
using ImageSpace.Skia;
using SkiaSharp;
namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private void ShowMenu(string name,FrameworkElement anchor)
    {
        var active=Session.Document.ActiveLayer;var has=active is not null;var pixels=active?.Pixels is not null;var editable=has&&!active!.Locked;
        List<(string Label,string Shortcut,Action Action,bool Enabled)> items=[];
        void Item(string label,string shortcut,Action action,bool enabled=true)=>items.Add((label,shortcut,action,enabled));void Line()=>Item("-","",()=>{});
        switch(name)
        {
            case "File":
                Item("New…","Ctrl+N",()=>_ = NewAsync());Item("Open…","Ctrl+O",()=>_ = OpenAsync(false));Item("Place image…","",()=>_ = OpenAsync(true));Item("Open sample artwork","",()=>Run(()=>AddDocument(SampleDocument.Create())));Line();
                Item("Save editable document","Ctrl+S",()=>_ = SaveAsync());Item("Export PNG…","",()=>_ = ExportAsync("png"));Item("Export JPEG…","",()=>_ = ExportAsync("jpg"));Item("Export WebP…","",()=>_ = ExportAsync("webp"));Item("Export layered PSD…","",()=>_ = ExportAsync("psd"));Line();Item("Close document","",()=>_ = CloseAsync(Session));Item("Clear recovery copy…","",()=>_ = ClearRecoveryAsync());break;
            case "Edit":
                Item("Undo"+(Session.History.Count>0?" "+Session.History[^1].Name:""),"Ctrl+Z",()=>Run(Session.Undo),Session.CanUndo);Item("Redo","Ctrl+Shift+Z",()=>Run(Session.Redo),Session.CanRedo);Line();
                Item("Cut","Ctrl+X",()=>Copy(true),editable);Item("Copy merged","Ctrl+C",()=>Copy(false));Item("Paste as new layer","Ctrl+V",Paste,_clipboard is not null);Item("Clear selected pixels","Delete",()=>Run(Session.ClearPixels),editable&&pixels);Line();
                Item("Free transform","Ctrl+T",()=>{SelectTool(EditorTool.Move);Surface.ShowTransform=true;Surface.Invalidate();},has);Item("Fill with foreground","Alt+Backspace",()=>Fill(Surface.Foreground),editable&&pixels);Item("Fill with background","Ctrl+Backspace",()=>Fill(Surface.BackgroundColor),editable&&pixels);Item("Keyboard shortcuts","",()=>_ = HelpAsync());break;
            case "Image":
                Item("Image size…","",()=>_ = ImageSizeAsync(false));Item("Canvas size…","",()=>_ = ImageSizeAsync(true));Line();Item("Rotate 90° clockwise","",()=>Run(()=>{Session.RotateCanvas(true);Surface.Fit();}));Item("Rotate 90° counterclockwise","",()=>Run(()=>{Session.RotateCanvas(false);Surface.Fit();}));Item("Flip canvas horizontal","",()=>FlipCanvas(true));Item("Flip canvas vertical","",()=>FlipCanvas(false));Item("Trim transparent pixels","",Trim);Line();Item("Brightness / Contrast…","",()=>_ = FilterDialogAsync(FilterKind.BrightnessContrast),editable&&pixels);Item("Hue / Saturation (saturation)…","",()=>_ = FilterDialogAsync(FilterKind.Saturation),editable&&pixels);Item("Invert","Ctrl+I",()=>_ = ApplyFilterAsync(FilterKind.Invert),editable&&pixels);Item("Grayscale","",()=>_ = ApplyFilterAsync(FilterKind.Grayscale),editable&&pixels);break;
            case "Layer":
                Item("New pixel layer","Ctrl+Shift+N",()=>Run(()=>Session.AddLayer("Layer "+(Session.Document.Layers.Count+1))));Item("Duplicate layer","Ctrl+J",()=>Run(Session.DuplicateLayer),has);Item("Rename layer…","",()=>_ = RenameLayerAsync(),has);Item("Delete layer","",()=>Run(Session.DeleteLayer),editable);Line();Item("Rasterize layer","",RasterizeLayer,editable&&active?.Kind is not LayerKind.Raster and not LayerKind.Adjustment);Item("Merge down","Ctrl+E",MergeDown,editable&&Session.Document.Layers.IndexOf(active!)>0);Item("Flatten image…","",()=>_ = FlattenAsync());Line();Item("Add layer mask","",()=>Run(Session.AddMask),editable);Item(active?.MaskEnabled==false?"Enable mask":"Disable mask","",()=>Run(()=>Session.Execute("Toggle mask",d=>{if(d.ActiveLayer is{} l)l.MaskEnabled=!l.MaskEnabled;})),active?.Mask is not null);Item("Delete mask","",()=>Run(()=>Session.Execute("Delete mask",d=>{if(d.ActiveLayer is{} l)l.Mask=null;d.EditMask=false;})),active?.Mask is not null);Line();Item("New adjustment layer…","",()=>ShowAdjustmentMenu(anchor));Item(active?.Locked==true?"Unlock layer":"Lock layer","",()=>Run(()=>Session.Execute("Layer lock",d=>{if(d.ActiveLayer is{} l)l.Locked=!l.Locked;})),has);break;
            case "Type":Item("Edit text…","",()=>{if(active is not null)_ = EditTextAsync(active);},active?.Kind==LayerKind.Text);Item("Bold / Regular","",()=>Run(()=>Session.Execute("Text weight",_=>active!.Bold=!active.Bold)),active?.Kind==LayerKind.Text&&!active.Locked);Item("Rasterize type","",RasterizeLayer,active?.Kind==LayerKind.Text&&!active.Locked);break;
            case "Select":
                Item("All","Ctrl+A",SelectAll);Item("Deselect","Ctrl+D",Deselect,Session.Document.Selection is not null);Item("Inverse","Ctrl+Shift+I",InvertSelection);Line();Item("Feather…","",()=>_ = FeatherAsync(),Session.Document.Selection is not null);Item("Select layer alpha","",SelectLayerAlpha,has);Item("Create layer mask from selection","",()=>Run(Session.AddMask),editable);break;
            case "Filter":
                Item("Gaussian blur…","",()=>_ = FilterDialogAsync(FilterKind.GaussianBlur),editable&&pixels);Item("Sharpen…","",()=>_ = FilterDialogAsync(FilterKind.Sharpen),editable&&pixels);Item("Add noise…","",()=>_ = FilterDialogAsync(FilterKind.Noise),editable&&pixels);Item("Pixelate…","",()=>_ = FilterDialogAsync(FilterKind.Pixelate),editable&&pixels);Line();Item("Invert","",()=>_ = ApplyFilterAsync(FilterKind.Invert),editable&&pixels);Item("Black and white","",()=>_ = ApplyFilterAsync(FilterKind.Grayscale),editable&&pixels);Item("Sepia","",()=>_ = ApplyFilterAsync(FilterKind.Sepia),editable&&pixels);Item("Emboss","",()=>_ = ApplyFilterAsync(FilterKind.Emboss),editable&&pixels);Item("Find edges","",()=>_ = ApplyFilterAsync(FilterKind.Edges),editable&&pixels);Item("Posterize…","",()=>_ = FilterDialogAsync(FilterKind.Posterize),editable&&pixels);Item("Threshold…","",()=>_ = FilterDialogAsync(FilterKind.Threshold),editable&&pixels);Item("Gamma…","",()=>_ = FilterDialogAsync(FilterKind.Gamma),editable&&pixels);break;
            case "View":
                Item("Fit on screen","Ctrl+0",Surface.Fit);Item("100%","Ctrl+1",()=>Surface.SetZoom(1));Item("Zoom in","+",()=>Surface.SetZoom(Surface.Zoom*1.25f));Item("Zoom out","−",()=>Surface.SetZoom(Surface.Zoom/.8f*.64f));Line();Item((Surface.ShowRulers?"✓ ":"")+"Rulers","Ctrl+R",()=>{Surface.ShowRulers=!Surface.ShowRulers;Surface.Invalidate();});Item((Surface.ShowGrid?"✓ ":"")+"Pixel grid","",()=>{Surface.ShowGrid=!Surface.ShowGrid;Surface.Invalidate();});Item("Hide / show panels","Tab",TogglePanels);break;
            case "Window":
                Item("Layers","",()=>SetBottomMode("Layers"));Item("Channels and histogram","",()=>SetBottomMode("Channels"));Item("History","",()=>SetBottomMode("History"));Line();foreach(var doc in _documents){var captured=doc;Item((doc==Session?"✓ ":"")+doc.Document.Name,"",()=>Switch(captured));}break;
            default:Item("ImageSpace user guide","F1",()=>_ = HelpAsync());Item("Features and compatibility","",()=>_ = CapabilitiesAsync());Item("Rendering diagnostics","",()=>_ = DiagnosticsAsync());Item("About ImageSpace","",()=>_ = AboutAsync());break;
        }
        Studio.Menu(anchor,items);
    }
    private void ShowAdjustmentMenu(FrameworkElement anchor)
    {
        var kinds=new[]{AdjustmentKind.BrightnessContrast,AdjustmentKind.Saturation,AdjustmentKind.Invert,AdjustmentKind.Grayscale,AdjustmentKind.Sepia,AdjustmentKind.GaussianBlur};
        Studio.Menu(anchor,kinds.Select(kind=>(kind.ToString(),"",(Action)(()=>Run(()=>Session.Execute("New adjustment",d=>{var layer=new Layer{Name=kind.ToString(),Kind=LayerKind.Adjustment,Adjustment=kind,Amount=kind==AdjustmentKind.GaussianBlur?4:0,Width=d.Width,Height=d.Height};d.Layers.Add(layer);d.ActiveLayerId=layer.Id;d.EditMask=false;}))),true)));
    }
    private void Deselect()=>Run(()=>Session.Execute("Deselect",d=>d.Selection=null));
    private void SelectAll()=>Run(()=>Session.Execute("Select all",d=>{d.Selection=new(d.Width,d.Height);d.Selection.Fill(Rgba32.White);}));
    private void InvertSelection()=>Run(()=>Session.Execute("Inverse selection",d=>d.Selection=Selections.Invert(d.Selection,d.Width,d.Height)));
    private void SelectLayerAlpha()=>Run(()=>{if(Session.Document.ActiveLayer is not{} l)return;var pixels=Surface.Renderer.RasterizeLayer(Session.Document,l);Session.Execute("Select layer alpha",d=>{var mask=new PixelSurface(d.Width,d.Height);for(var y=0;y<d.Height;y++)for(var x=0;x<d.Width;x++)mask.Set(x,y,new(255,255,255,pixels.Get(x,y).A));d.Selection=mask;});});
    private void Fill(Rgba32 color)=>Run(()=>{if(Session.Document.ActiveLayer is not{} l)return;Session.Execute("Fill",d=>RasterOperations.Fill(d,l,color));});
    private void Copy(bool cut)
    {
        Run(()=>{var d=Session.Document;var pixels=Surface.Renderer.Rasterize(d);var bounds=Selections.Bounds(d.Selection)??(0,0,d.Width,d.Height);_clipboard=RasterOperations.Crop(pixels,bounds.Item1,bounds.Item2,bounds.Item3,bounds.Item4);if(d.Selection is not null)for(var y=0;y<_clipboard.Height;y++)for(var x=0;x<_clipboard.Width;x++){var c=_clipboard.Get(x,y);_clipboard.Set(x,y,c.WithAlpha(Rgba32.Byte(c.A*d.Coverage(x+bounds.Item1,y+bounds.Item2))));}if(cut)Session.ClearPixels();ShowStatus(cut?"Selection cut to the application clipboard.":"Merged pixels copied to the application clipboard.");});
    }
    private void Paste()=>Run(()=>{if(_clipboard is null)return;Session.Execute("Paste",d=>{var l=Layer.Raster("Pasted pixels",_clipboard.Width,_clipboard.Height);l.Pixels=_clipboard.Snapshot();l.X=(d.Width-l.Width)/2;l.Y=(d.Height-l.Height)/2;d.Layers.Add(l);d.ActiveLayerId=l.Id;d.EditMask=false;});SelectTool(EditorTool.Move);});
    private void RasterizeLayer()=>Run(()=>{var d=Session.Document;var l=d.ActiveLayer;if(l is null||l.Locked||l.Kind==LayerKind.Adjustment)return;var pixels=Surface.Renderer.RasterizeLayer(d,l);Session.Execute("Rasterize layer",_=>{l.Pixels=pixels;l.Kind=LayerKind.Raster;l.X=l.Y=l.Rotation=0;l.ScaleX=l.ScaleY=1;l.Width=d.Width;l.Height=d.Height;l.Mask=null;d.EditMask=false;});});
    private void MergeDown()=>Run(()=>
    {
        var d=Session.Document;var upper=d.ActiveLayer;var index=upper is null?-1:d.Layers.IndexOf(upper);if(index<1||upper!.Locked)return;var lower=d.Layers[index-1];if(lower.Locked)throw new InvalidOperationException("Unlock the lower layer before merging.");
        if(upper.Kind==LayerKind.Adjustment||lower.Kind==LayerKind.Adjustment||upper.Blend!=LayerBlend.Normal||lower.Blend!=LayerBlend.Normal)throw new InvalidOperationException("For adjustments or interacting blend modes, use Flatten Image to preserve the visible result.");
        var pair=new ImageDocument(d.Width,d.Height){Layers=[lower.Snapshot(),upper.Snapshot()]};var composite=Surface.Renderer.Rasterize(pair);
        Session.Execute("Merge down",doc=>{var l=Layer.Raster(upper.Name,d.Width,d.Height);l.Pixels=composite;doc.Layers.RemoveRange(index-1,2);doc.Layers.Insert(index-1,l);doc.ActiveLayerId=l.Id;doc.EditMask=false;});
    });
    private async Task FlattenAsync()
    {
        if(!await ConfirmAsync("Flatten image?","All layers, including hidden layers, will be replaced by the visible composite. Undo remains available.","Flatten"))return;
        Run(()=>{var composite=Surface.Renderer.Rasterize(Session.Document);Session.Execute("Flatten image",d=>{var l=Layer.Raster("Background",d.Width,d.Height);l.Pixels=composite;d.Layers=[l];d.ActiveLayerId=l.Id;d.EditMask=false;});});
    }
    private void FlipCanvas(bool horizontal)=>Run(()=>Session.Execute(horizontal?"Flip canvas horizontal":"Flip canvas vertical",d=>{foreach(var l in d.Layers){if(horizontal){l.X=d.Width-l.X;l.ScaleX=-l.ScaleX;}else{l.Y=d.Height-l.Y;l.ScaleY=-l.ScaleY;}l.Rotation=-l.Rotation;}if(d.Selection is not null)d.Selection=RasterOperations.Flip(d.Selection,horizontal);}));
    private void Trim()=>Run(()=>{var mask=Surface.Renderer.Rasterize(Session.Document);var bounds=Selections.Bounds(mask);if(bounds is not{} b){ShowStatus("The canvas is fully transparent.");return;}Session.Crop(b.X,b.Y,b.Width,b.Height);Surface.Fit();});
    private void TogglePanels(){_panelsHidden=!_panelsHidden;_workspace.ColumnDefinitions[3].Width=new GridLength(_panelsHidden?0:292);_workspace.ColumnDefinitions[2].Width=new GridLength(_panelsHidden?0:30);}
    private async Task ApplyFilterAsync(FilterKind kind,float amount=0,float secondary=0)
    {
        if(_busy)return;var session=Session;var layer=session.Document.ActiveLayer;if(layer?.Pixels is null||layer.Locked){ShowStatus("Select an unlocked pixel layer, or rasterize the selected layer first.");return;}
        _busy=true;_workspace.IsEnabled=false;ShowStatus("Applying "+kind+"…");
        try
        {
            var maskEditing=session.Document.EditMask;var source=(maskEditing?layer.Mask:layer.Pixels)?.Snapshot()??throw new InvalidOperationException("No mask exists on this layer.");PixelSurface? result=null;var usedGpu=false;
            if(maskEditing&&kind==FilterKind.Invert){result=source.Snapshot();for(var y=0;y<source.Height;y++)for(var x=0;x<source.Width;x++){var c=source.Get(x,y);result.Set(x,y,new(255,255,255,(byte)(255-c.A)));}}
            if(result is null&&GpuFilter is not null&&!maskEditing){try{result=await GpuFilter(source,kind,amount,secondary);usedGpu=result is not null;}catch(Exception ex){ShowStatus("GPU unavailable; using the CPU kernel. "+ex.Message);}}
            if(result is null){await Task.Yield();result=OperatingSystem.IsBrowser()?FilterEngine.Apply(source,kind,amount,secondary):await Task.Run(()=>FilterEngine.Apply(source,kind,amount,secondary));}
            var output=result;session.Execute(kind.ToString(),d=>{if(d.Selection is not null)for(var y=0;y<source.Height;y++)for(var x=0;x<source.Width;x++){var p=layer.ToDocument(new(x,y));output.Set(x,y,Rgba32.Lerp(source.Get(x,y),output.Get(x,y),d.Coverage((int)p.X,(int)p.Y)));}if(maskEditing)layer.Mask=output;else layer.Pixels=output;});ShowStatus(kind+" applied · "+(usedGpu?"WebGPU compute":"CPU kernel"));
        }
        catch(Exception ex){ShowStatus(ex.Message);}
        finally{_busy=false;_workspace.IsEnabled=true;StateChanged?.Invoke();}
    }
}
