using ImageSpace.Imaging;

namespace ImageSpace.Editing;

public sealed partial class EditorSession
{
    /// <summary>Modify the active soft selection as one atomic undoable edit.</summary>
    public void ModifySelection(SelectionModification modification, int radius, CancellationToken cancellationToken = default)
    {
        if (Document.Selection is null)
            throw new InvalidOperationException("Create a selection before modifying it.");
        var selection = Document.Selection;
        var result = SelectionMorphology.Apply(selection, modification, radius, cancellationToken);
        Execute(modification + " selection", document => document.Selection = result);
    }
}
