namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private UIElement BuildMenuBar()
    {
        var grid=new Grid{Background=Studio.Brush("#242424"),ColumnDefinitions={new(){Width=GridLength.Auto},new(){Width=new(1,GridUnitType.Star)},new(){Width=GridLength.Auto}}};
        var logo=Studio.Label("Is",16,"#80baff");logo.FontWeight=Windows.UI.Text.FontWeights.SemiBold;logo.Margin=new Thickness(11,0,12,0);grid.Children.Add(logo);
        var menus=new StackPanel{Orientation=Orientation.Horizontal,Spacing=0};foreach(var name in new[]{"File","Edit","Image","Layer","Type","Select","Filter","View","Window","Help"})
        {
            StudioButton? button=null;button=new StudioButton(name,()=>ShowMenu(name,button!)){Height=27,Padding=new Thickness(7,0,7,0),Background=Studio.Brush("#242424"),BorderThickness=new Thickness(0),CornerRadius=new CornerRadius(0)};menus.Children.Add(button);
        }
        Grid.SetColumn(menus,1);grid.Children.Add(menus);var title=Studio.Label("ImageSpace",11,"#a6a6a6");title.Margin=new Thickness(10,0,15,0);Grid.SetColumn(title,2);grid.Children.Add(title);return grid;
    }
    private UIElement BuildToolPalette()
    {
        _toolPalette.Children.Add(Studio.Label("··",11,"#777777"));
        foreach(var(tool,icon,key)in new (EditorTool,string,string)[]{(EditorTool.Move,"move","V"),(EditorTool.Marquee,"marquee","M"),(EditorTool.EllipseSelect,"ellipse-select","Shift+M"),(EditorTool.Lasso,"lasso","L"),(EditorTool.Wand,"wand","W"),(EditorTool.Crop,"crop","C"),(EditorTool.Eyedropper,"eyedropper","I"),(EditorTool.Brush,"brush","B"),(EditorTool.Pencil,"pencil","Shift+B"),(EditorTool.Clone,"clone","S"),(EditorTool.Smudge,"smudge","R"),(EditorTool.Eraser,"eraser","E"),(EditorTool.Gradient,"gradient","G"),(EditorTool.Fill,"fill","Shift+G"),(EditorTool.Dodge,"dodge","O"),(EditorTool.Burn,"burn","Shift+O"),(EditorTool.Text,"text","T"),(EditorTool.Rectangle,"rectangle","U"),(EditorTool.Ellipse,"ellipse","Shift+U"),(EditorTool.Hand,"hand","H"),(EditorTool.Zoom,"zoom","Z")})
        {
            var b=new StudioButton(tool+" tool ("+key+")",()=>SelectTool(tool),icon){Width=34,Height=28,Padding=new Thickness(8,4,8,4),CornerRadius=new CornerRadius(2)};b.Selected(tool==EditorTool.Move);_toolButtons[tool]=b;_toolPalette.Children.Add(b);
        }
        _fgButton=new StudioButton("Foreground color",()=>_ = ColorAsync(true)){Width=24,Height=24,Padding=new Thickness(0),Content=null,BorderBrush=Studio.Brush("#dddddd"),BorderThickness=new Thickness(1)};
        _bgButton=new StudioButton("Background color",()=>_ = ColorAsync(false)){Width=24,Height=24,Padding=new Thickness(0),Content=null,BorderBrush=Studio.Brush("#dddddd"),BorderThickness=new Thickness(1),Margin=new Thickness(9,-12,0,0)};
        _toolPalette.Children.Add(_bgButton);_toolPalette.Children.Add(_fgButton);_toolPalette.Margin=new Thickness(4,0,4,8);
        return new ScrollViewer{Content=_toolPalette,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Background=Studio.Brush("#303030")};
    }
    private UIElement BuildPanelRail()
    {
        var stack=new StackPanel{Spacing=8,Margin=new Thickness(1,13,1,0)};foreach(var(name,icon)in new[]{("History","undo"),("Properties","rectangle"),("Layers","folder"),("Color","adjust")})stack.Children.Add(new StudioButton(name,()=>{if(name=="History"||name=="Layers")SetBottomMode(name);else ShowStatus(name+" panel is docked on the right.");},icon){Width=27,Height=30,Padding=new Thickness(5)});return Studio.Box(stack,"#292929");
    }
    private UIElement BuildRightPanels()
    {
        var grid=new Grid{Background=Studio.Brush("#303030"),BorderBrush=Studio.Brush("#171717"),BorderThickness=new Thickness(1,0,0,0),RowDefinitions={new(){Height=new(192)},new(){Height=new(224)},new(){Height=new(1,GridUnitType.Star)}}};
        var color=new Grid{RowDefinitions={new(){Height=new(30)},new(){Height=new(1,GridUnitType.Star)}}};color.Children.Add(PanelHeader("Color","Swatches",()=>ShowSwatches()));
        var spectrum=new ColorSpectrum();spectrum.ColorChanged+=c=>{Surface.Foreground=c;RefreshColors();};var colors=new StackPanel{Spacing=5,Margin=new Thickness(11,7,11,7)};spectrum.Height=122;colors.Children.Add(spectrum);colors.Children.Add(Studio.Row(_colorHex,Studio.Label("  sRGB",10,"#777777"),new StudioButton("Edit color…",()=>_ = ColorAsync(true)){Height=20,Padding=new Thickness(7,0,7,0)}));Grid.SetRow(colors,1);color.Children.Add(colors);grid.Children.Add(color);
        var properties=new Grid{RowDefinitions={new(){Height=new(30)},new(){Height=new(1,GridUnitType.Star)}}};properties.Children.Add(PanelHeader("Properties","Adjustments",()=>ShowAdjustmentMenu(_properties)));Grid.SetRow(_properties,1);properties.Children.Add(_properties);Grid.SetRow(properties,1);grid.Children.Add(properties);
        var bottom=new Grid{RowDefinitions={new(){Height=new(30)},new(){Height=new(1,GridUnitType.Star)}}};var modes=new StackPanel{Orientation=Orientation.Horizontal,Spacing=0};foreach(var name in new[]{"Layers","Channels","History"})modes.Children.Add(new StudioButton(name,()=>SetBottomMode(name)){Height=29,Padding=new Thickness(11,0,11,0),CornerRadius=new CornerRadius(0),Background=Studio.Brush("#2b2b2b"),BorderThickness=new Thickness(0)});bottom.Children.Add(Studio.Box(modes,"#272727"));_bottomPanel.Content=_layers;Grid.SetRow(_bottomPanel,1);bottom.Children.Add(_bottomPanel);Grid.SetRow(bottom,2);grid.Children.Add(bottom);return grid;
    }
    private static UIElement PanelHeader(string active,string secondary,Action action)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=1};row.Children.Add(new StudioButton(active,()=>{}){Height=29,Padding=new Thickness(12,0,12,0),CornerRadius=new CornerRadius(0),Background=Studio.Brush("#383838"),BorderThickness=new Thickness(0)});row.Children.Add(new StudioButton(secondary,action){Height=29,Padding=new Thickness(10,0,10,0),CornerRadius=new CornerRadius(0),Background=Studio.Brush("#282828"),BorderThickness=new Thickness(0)});return Studio.Box(row,"#272727");
    }
    private void SetBottomMode(string mode)
    {
        _bottomMode=mode;_bottomPanel.Content=mode switch{"History"=>new ScrollViewer{Content=_history,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},"Channels"=>new ScrollViewer{Content=_channels,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},_=>_layers};if(mode=="Channels")UpdateHistogram();
    }
    private void RefreshHistory()
    {
        _history.Children.Clear();_history.Children.Add(Studio.Box(Studio.Label("  History states  ·  "+Session.History.Count,11,"#a7a7a7"),"#303030",new Thickness(6,10,6,8)));
        var initial=new StudioButton("Open document",()=>Run(()=>{while(Session.CanUndo)Session.Undo();})){Height=32,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(14,3,8,3)};_history.Children.Add(initial);
        for(var index=0;index<Session.History.Count;index++){var target=index+1;var label=Session.History[index].Name;var button=new StudioButton(label,()=>Run(()=>{while(Session.History.Count>target)Session.Undo();})){Height=31,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(20,3,8,3)};button.Selected(index==Session.History.Count-1);_history.Children.Add(button);}
        _history.Children.Add(Studio.Row(new StudioButton("Undo",()=>Run(Session.Undo),"undo"),new StudioButton("Redo",()=>Run(Session.Redo),"redo")));
    }
    private void RefreshChannels()
    {
        _channels.Children.Clear();_channels.Margin=new Thickness(9);_channels.Children.Add(Studio.Label("Composite histogram",12));_channels.Children.Add(_histogram);
        foreach(var(name,index)in new[]{("RGB",-1),("Red",0),("Green",1),("Blue",2)})_channels.Children.Add(new StudioButton(name+" histogram",()=>UpdateHistogram(index)){HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left,Height=30});
        _channels.Children.Add(new StudioButton("Load alpha as selection",()=>Run(()=>{var composite=Surface.Renderer.Rasterize(Session.Document);Session.Execute("Select composite alpha",d=>{var selection=new PixelSurface(d.Width,d.Height);for(var y=0;y<d.Height;y++)for(var x=0;x<d.Width;x++)selection.Set(x,y,new(255,255,255,composite.Get(x,y).A));d.Selection=selection;});})));
    }
    private void UpdateHistogram(int channel=-1){try{_histogram.Values=RasterOperations.Histogram(Surface.Renderer.Rasterize(Session.Document),channel);}catch(Exception ex){ShowStatus(ex.Message);}}
    private void RefreshOptions()
    {
        _options.Children.Clear();_options.Children.Add(Studio.Label(Surface.Tool.ToString(),12));_options.Children.Add(new Border{Width=1,Height=22,Background=Studio.Brush("#1f1f1f")});
        if(Surface.Tool is EditorTool.Brush or EditorTool.Pencil or EditorTool.Eraser or EditorTool.Clone or EditorTool.Dodge or EditorTool.Burn or EditorTool.Smudge)
        {
            var size=new NumericField("Size",Surface.Brush.Size,1,1024,93);size.ValueChanged+=v=>Surface.Brush=Surface.Brush with{Size=(float)v};var hardness=new NumericField("Hardness",Surface.Brush.Hardness*100,0,100,116);hardness.ValueChanged+=v=>Surface.Brush=Surface.Brush with{Hardness=(float)v/100};var opacity=new NumericField("Opacity",Surface.Brush.Opacity*100,1,100,108);opacity.ValueChanged+=v=>Surface.Brush=Surface.Brush with{Opacity=(float)v/100};var flow=new NumericField("Flow",Surface.Brush.Flow*100,1,100,94);flow.ValueChanged+=v=>Surface.Brush=Surface.Brush with{Flow=(float)v/100};_options.Children.Add(size);_options.Children.Add(hardness);_options.Children.Add(opacity);_options.Children.Add(flow);
            var pressure=new StudioButton(Surface.Brush.Pressure?"Pressure ✓":"Pressure",()=>{Surface.Brush=Surface.Brush with{Pressure=!Surface.Brush.Pressure};RefreshOptions();});_options.Children.Add(pressure);_options.Children.Add(new StudioButton("New paint layer",()=>Run(()=>Session.AddLayer("Paint")),"plus"));
        }
        else if(Surface.Tool==EditorTool.Move){_options.Children.Add(Studio.Label("Auto-select: Layer",11));_options.Children.Add(new StudioButton(Surface.ShowTransform?"Transform controls ✓":"Transform controls",()=>{Surface.ShowTransform=!Surface.ShowTransform;Surface.Invalidate();RefreshOptions();}));_options.Children.Add(new StudioButton("Fit on screen",Surface.Fit));_options.Children.Add(new StudioButton("100%",()=>Surface.SetZoom(1)));}
        else if(Surface.Tool is EditorTool.Marquee or EditorTool.EllipseSelect or EditorTool.Lasso or EditorTool.Wand)
        {
            _options.Children.Add(Studio.Label("New  ·  Shift: add  ·  Alt: subtract",11));var tolerance=new NumericField("Tolerance",Surface.Tolerance,0,255,120);tolerance.ValueChanged+=v=>Surface.Tolerance=(int)v;_options.Children.Add(tolerance);_options.Children.Add(new StudioButton("Deselect",Deselect));_options.Children.Add(new StudioButton("Invert selection",InvertSelection));_options.Children.Add(new StudioButton("Feather…",()=>_ = FeatherAsync()));
        }
        else if(Surface.Tool==EditorTool.Crop){_options.Children.Add(Studio.Label("Non-destructive canvas crop",11));_options.Children.Add(new StudioButton("Apply crop",()=>Run(Surface.ApplyCrop)));_options.Children.Add(new StudioButton("Cancel",Surface.CancelGesture));}
        else if(Surface.Tool==EditorTool.Text){_options.Children.Add(Studio.Label("Inter    Regular / Bold    Anti-alias: smooth",11));_options.Children.Add(new StudioButton("Edit selected text…",()=>{if(Session.Document.ActiveLayer is{Kind:LayerKind.Text} l)_ = EditTextAsync(l);}));}
        else if(Surface.Tool is EditorTool.Rectangle or EditorTool.Ellipse){_options.Children.Add(Studio.Label("Shape  ·  Fill: "+Surface.Foreground.Hex+"  ·  Hold Shift to constrain",11));}
        else if(Surface.Tool is EditorTool.Gradient or EditorTool.Fill){_options.Children.Add(Studio.Label("Foreground → Background    Normal    100%",11));var tolerance=new NumericField("Tolerance",Surface.Tolerance,0,255,124);tolerance.ValueChanged+=v=>Surface.Tolerance=(int)v;_options.Children.Add(tolerance);}
        else{_options.Children.Add(new StudioButton("Fit screen",Surface.Fit));_options.Children.Add(new StudioButton("100%",()=>Surface.SetZoom(1)));_options.Children.Add(Studio.Label("Scroll to zoom at pointer · Space to pan",11));}
    }
    private void ShowSwatches()
    {
        var panel=new StackPanel{Spacing=8};string[] colors=["#101820","#ffffff","#e94848","#f39b46","#f5d76e","#73be6e","#49a9a0","#4e9dd9","#575fcf","#a766ca","#d764a0","#a47255","#e9d4b5","#53616b","#b8c7c3","#263e4e"];
        for(var y=0;y<4;y++){var row=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};foreach(var hex in colors.Skip(y*4).Take(4)){var b=new StudioButton(hex,()=>{Surface.Foreground=Rgba32.Parse(hex);RefreshColors();}){Width=48,Height=36,Background=Studio.Brush(hex),Content=null};row.Children.Add(b);}panel.Children.Add(row);}
        _ = ShowDialogAsync("Swatches",panel,"Done");
    }
}
