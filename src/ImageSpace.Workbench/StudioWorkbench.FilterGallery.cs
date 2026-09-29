using ImageSpace.Filters;

namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    public Func<PixelSurface, CancellationToken, Task<IFilterSession?>>? FilterSessionFactory
    {
        get; set;
    }
    private FilterOperation[] _lastFilterRecipe = [];

    private async Task<IFilterSession> CreateFilterSessionAsync(PixelSurface source, CancellationToken cancellationToken)
    {
        if (FilterSessionFactory is not null)
        {
            try
            {
                var accelerated = await FilterSessionFactory(source, cancellationToken);
                if (accelerated is not null)
                    return accelerated;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { ShowStatus("GPU session unavailable; using CPU kernels. " + error.Message); }
        }
        return new CpuFilterSession(source);
    }

    private async Task FilterGalleryAsync()
    {
        if (_busy || _dialogOpen || !Session.CanApplyFilterStack)
            return;
        var session = Session;
        var layer = session.Document.ActiveLayer!;
        var revision = session.Revision;
        FilterOperation[] operations;
        _dialogOpen = true;
        try
        {
            Surface.IsSpaceDown = false;
            await using var editor = new FilterGalleryEditor(layer.Pixels!,
                _lastFilterRecipe.Length > 0 ? _lastFilterRecipe : [FilterOperation.Default(FilterKind.Grayscale)], CreateFilterSessionAsync)
            {
                Width = Math.Clamp(ActualWidth - 130, 540, 1000),
                Height = Math.Clamp(ActualHeight - 200, 270, 540)
            };
            var dialog = new ContentDialog
            {
                Title = "Filter Gallery",
                Content = editor,
                PrimaryButtonText = "Apply filters",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = ElementTheme.Dark,
                Background = Studio.Brush("#323232"),
                Foreground = Studio.Brush("#dddddd"),
                FontFamily = Studio.Font
            };
            dialog.Resources["ContentDialogMaxWidth"] = Math.Clamp(ActualWidth - 50, 600, 1100);
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                return;
            operations = editor.Operations;
        }
        catch (Exception error) { ShowStatus("Filter Gallery: " + error.Message); return; }
        finally { _dialogOpen = false; }
        if (!ReferenceEquals(Session, session) || session.Revision != revision || !ReferenceEquals(session.Document.ActiveLayer, layer))
        {
            ShowStatus("The document changed while the gallery was open; no filters were applied.");
            return;
        }
        await ApplyRecipeAsync(operations);
    }

    private async Task ApplyRecipeAsync(IReadOnlyList<FilterOperation> operations)
    {
        if (_busy || _dialogOpen || !Session.CanApplyFilterStack)
            return;
        var captured = FilterRecipe.Capture(operations);
        if (!captured.Any(op => op.Enabled))
        {
            ShowStatus("No enabled filters; the document was not changed.");
            return;
        }
        _busy = true;
        _workspace.IsHitTestVisible = false;
        ShowStatus("Applying filter stack…");
        try
        {
            await Session.ApplyFilterStackAsync(captured, CreateFilterSessionAsync);
            _lastFilterRecipe = captured;
            ShowStatus($"Applied {captured.Count(op => op.Enabled)} filters as one undoable edit.");
        }
        catch (Exception error) { ShowStatus("Filter stack: " + error.Message); }
        finally { _busy = false; _workspace.IsHitTestVisible = true; StateChanged?.Invoke(); }
    }
}
