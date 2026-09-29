using ImageSpace.Core;
using ImageSpace.Filters;
using ImageSpace.Imaging;

namespace ImageSpace.Editing;

public sealed partial class EditorSession
{
    public bool CanApplyFilterStack => !IsInTransaction && !Document.EditMask &&
        Document.ActiveLayer is { Kind: LayerKind.Raster, Locked: false, Pixels: not null };

    /// <summary>Evaluate a detached raster snapshot and commit one selection-restricted edit if the target is unchanged.</summary>
    public async Task ApplyFilterStackAsync(IReadOnlyList<FilterOperation> operations,
        Func<PixelSurface, CancellationToken, Task<IFilterSession>> createSession,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createSession);
        var captured = FilterRecipe.Capture(operations);
        if (!CanApplyFilterStack)
            throw new InvalidOperationException("Select unlocked raster content and finish the current gesture first.");
        if (!captured.Any(operation => operation.Enabled))
            return;
        var document = Document;
        var layer = document.ActiveLayer!;
        var target = layer.Pixels!;
        var source = target.Snapshot();
        var revision = Revision;
        var pixelRevision = target.Revision;
        var selection = document.Selection;
        var selectionRevision = selection?.Revision ?? 0;
        var transform = layer.Transform;
        cancellationToken.ThrowIfCancellationRequested();
        await using var backend = await createSession(source, cancellationToken)
            ?? throw new InvalidOperationException("No filter backend was created.");
        var output = await backend.ApplyAsync(captured, cancellationToken)
            ?? throw new InvalidOperationException("The filter backend returned no pixels.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanApplyFilterStack || !ReferenceEquals(Document, document) || Revision != revision ||
            !ReferenceEquals(document.ActiveLayer, layer) || !ReferenceEquals(layer.Pixels, target) ||
            target.Revision != pixelRevision || layer.Transform != transform ||
            !ReferenceEquals(document.Selection, selection) || (selection?.Revision ?? 0) != selectionRevision)
            throw new InvalidOperationException("The filter target changed; its result was discarded.");
        if (output.Width != source.Width || output.Height != source.Height)
            throw new InvalidOperationException("A filter stack must preserve authored pixel dimensions.");
        var restricted = PixelEdits.RestrictToSelection(document, layer, source, output);
        Execute("Filter Gallery", state => PixelTarget.Replace(state, layer, restricted.Snapshot()));
    }
}
