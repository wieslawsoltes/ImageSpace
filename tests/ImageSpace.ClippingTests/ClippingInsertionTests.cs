using ImageSpace.Core;
using ImageSpace.Editing;

internal static class ClippingInsertionTests
{
    public static void Register(Action<string, Action> test)
    {
        foreach (var target in new[] { 0, 1, 2, 3 })
        foreach (var kind in new[] { AdjustmentKind.Curves, AdjustmentKind.GaussianBlur })
        {
            var selected = target;
            var effect = kind;
            test($"adjustment insertion preserves clipping base: target={selected}, effect={effect}", () =>
            {
                var background = Layer.Raster("Background", 4, 3);
                var basis = Layer.Raster("Base", 4, 3);
                var first = Layer.Raster("First clip", 4, 3);
                var second = Layer.Raster("Second clip", 4, 3);
                first.IsClipped = second.IsClipped = true;
                var document = new ImageDocument(4, 3) { Layers = [background, basis, first, second] };
                document.ActiveLayerId = document.Layers[selected].Id;
                var session = new EditorSession(document);
                var layer = session.AddAdjustment(effect);
                document.Validate();
                Check(document.Layers.IndexOf(layer) == selected + 1);
                Check(layer.IsClipped == (selected > 0));
                Check(layer.Adjustment == effect && layer.Width == 4 && layer.Height == 3);
                Check(layer.Amount == (effect == AdjustmentKind.GaussianBlur ? 4 : 0));
                foreach (var original in new[] { first, second })
                {
                    var baseIndex = LayerClipping.FindBaseIndex(document.Layers, document.Layers.IndexOf(original));
                    Check(document.Layers[baseIndex].Id == basis.Id);
                }
                Check(session.History.Count == 1 && document.ActiveLayerId == layer.Id);
                session.Undo();
                Check(session.Document.Layers.Count == 4);
                Check(session.Document.ActiveLayerId == document.Layers[selected].Id);
                session.Redo();
                session.Document.Validate();
                Check(session.Document.ActiveLayer?.Id == layer.Id && session.Document.Layers.Count == 5);
            });
        }
        test("adjustment insertion into an empty document has no clipping relationship", () =>
        {
            var session = new EditorSession(new ImageDocument(3, 2));
            var layer = session.AddAdjustment(AdjustmentKind.Levels);
            Check(!layer.IsClipped && layer.Kind == LayerKind.Adjustment && session.Document.Layers.Count == 1);
            session.Document.Validate();
        });
        test("invalid adjustment kind is rejected before opening a transaction", () =>
        {
            var session = new EditorSession(new ImageDocument(3, 2));
            foreach (var kind in new[] { AdjustmentKind.None, (AdjustmentKind)int.MaxValue })
            {
                try { session.AddAdjustment(kind); throw new Exception("Invalid adjustment was accepted."); }
                catch (ArgumentOutOfRangeException) { }
            }
            Check(session.History.Count == 0 && !session.IsInTransaction && session.Document.Layers.Count == 0);
        });
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Clipping adjustment insertion invariant failed.");
    }
}
