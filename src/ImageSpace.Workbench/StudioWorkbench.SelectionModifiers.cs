namespace ImageSpace.Workbench;

public sealed partial class StudioWorkbench
{
    private async Task ModifySelectionAsync(SelectionModification modification)
    {
        var session = Session;
        var selection = session.Document.Selection;
        if (selection is null || _busy)
            return;
        var radius = new NumericField("Radius", 3, 0, 256, 280) { Format = "0" };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(radius);
        panel.Children.Add(new TextBlock
        {
            Text = "Soft coverage is preserved. The square neighborhood extends by the radius in each direction; outside the canvas is unselected.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            FontSize = 12
        });
        if (await ShowDialogAsync(modification + " selection", panel, "Apply") != ContentDialogResult.Primary)
            return;
        Run(() =>
        {
            if (!ReferenceEquals(session, Session) || !ReferenceEquals(selection, session.Document.Selection))
                throw new InvalidOperationException("The active selection changed while the dialog was open.");
            session.ModifySelection(modification, (int)radius.Value);
            ShowStatus(modification + " applied to selection; Undo is available.");
        });
    }
}
