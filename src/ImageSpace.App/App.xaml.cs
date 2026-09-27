using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using ImageSpace.Controls;
using ImageSpace.Editing;
using ImageSpace.Documents;
using ImageSpace.Imaging;
using ImageSpace.Storage;
using ImageSpace.Workbench;
using SkiaSharp;
namespace ImageSpace.App;

public partial class App:Application
{
    private Window? _window;private StudioWorkbench? _workbench;
    public App(){InitializeComponent();RequestedTheme=ApplicationTheme.Dark;}
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window=new Window{Title="ImageSpace"};_window.Content=new Grid{Background=Studio.Brush("#242424"),Children={new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Spacing=14,Children={Studio.Label("ImageSpace",32,"#88bdff"),Studio.Label("Preparing the editing workspace…",13,"#adadad")}}}};_window.Activate();
        try
        {
            IEditorStorage storage;
#if __WASM__
            storage=new BrowserEditorStorage();
#else
            storage=new DesktopEditorStorage();
#endif
            var document=SampleDocument.Create();string? warning=null;
            try
            {
#if __WASM__
                var restore=!BrowserFiles.IsTestMode();
#else
                var restore=true;
#endif
                if(restore){var saved=await storage.ReadRecoveryAsync();if(saved is not null)document=DocumentArchive.Load(saved);}
            }
            catch(Exception ex){warning="Recovery could not be opened; the saved copy has not been deleted. "+ex.Message;}
            Studio.Font=new FontFamily("ms-appx:///Assets/Fonts/Inter.ttf#Inter");
            _workbench=new StudioWorkbench(new EditorSession(document),storage);_window.Content=_workbench;
            _window.Closed+=(_,_)=>_workbench.Dispose();_window.Activated+=(_,e)=>{if(e.WindowActivationState==Windows.UI.Core.CoreWindowActivationState.Deactivated)_workbench.Surface.IsSpaceDown=false;};
            if(warning is not null)_workbench.ShowStatus(warning);
            try
            {
                var file=await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Fonts/Inter.ttf"));using var stream=await file.OpenStreamForReadAsync();using var memory=new MemoryStream();await stream.CopyToAsync(memory);using var data=SKData.CreateCopy(memory.ToArray());var typeface=SKTypeface.FromData(data);if(typeface is not null){_workbench.Surface.Renderer.SetTypeface(typeface);_workbench.Surface.Invalidate();}
            }
            catch(Exception ex){Console.WriteLine("Optional Inter canvas typeface unavailable: "+ex.Message);}
#if __WASM__
            var backend=new BrowserGpuBackend();_workbench.GpuFilter=backend.ApplyAsync;
            BrowserDiagnostics.Attach(_workbench);
            try{_workbench.ComputeBackend=await BrowserFiles.InitializeGpu();}catch(Exception ex){_workbench.ComputeBackend="CPU fallback: "+ex.Message;}
#endif
        }
        catch(Exception ex)
        {
            Console.Error.WriteLine(ex);_window.Content=new ScrollViewer{Background=Studio.Brush("#242424"),Content=new TextBlock{Text="ImageSpace could not start.\n\n"+ex+"\n\nYour saved data has not been deleted. Reload to retry.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(36),FontSize=14,Foreground=Studio.Brush("#e5e5e5")}};
        }
    }
}
