namespace ImageSpace.Editing;

/// <summary>Invalidation category. Document is conservative; selection and saving do not change image content.</summary>
public enum EditorChange
{
    Document, ActiveTarget, SavedState
}

public sealed class EditorChangedEventArgs : EventArgs
{
    private static readonly EditorChangedEventArgs[] Values =
        [new(EditorChange.Document), new(EditorChange.ActiveTarget), new(EditorChange.SavedState)];
    public EditorChange Change
    {
        get;
    }
    private EditorChangedEventArgs(EditorChange change) => Change = change;
    internal static EditorChangedEventArgs For(EditorChange change) => Values[(int)change];
}
