using ImageSpace.Core;

namespace ImageSpace.Editing;

public sealed partial class EditorSession
{
    public bool CanCreateClippingMask
    {
        get
        {
            var layer = Document.ActiveLayer;
            var index = layer is null ? -1 : Document.Layers.IndexOf(layer);
            return !IsInTransaction && layer is { Locked: false, IsClipped: false } && index > 0 &&
                LayerClipping.FindBaseIndex(Document.Layers, index - 1) >= 0;
        }
    }

    public bool CanReleaseClippingMask
    {
        get
        {
            if (IsInTransaction || Document.ActiveLayer is not { IsClipped: true } layer)
                return false;
            var index = Document.Layers.IndexOf(layer);
            for (var i = index; i <= LayerClipping.FindEndIndex(Document.Layers, index); i++)
                if (Document.Layers[i].Locked)
                    return false;
            return true;
        }
    }

    public void CreateClippingMask()
    {
        if (!CanCreateClippingMask)
            throw new InvalidOperationException("Select an unlocked layer above a raster, text or shape clipping base.");
        Execute("Create clipping mask", document => document.ActiveLayer!.IsClipped = true);
    }

    /// <summary>Release the selected clipped layer and the contiguous clipped tail above it.</summary>
    public void ReleaseClippingMask()
    {
        if (!CanReleaseClippingMask)
            throw new InvalidOperationException("Select a clipped layer and unlock the clipped tail before releasing it.");
        var start = Document.Layers.IndexOf(Document.ActiveLayer!);
        var end = LayerClipping.FindEndIndex(Document.Layers, start);
        Execute("Release clipping mask", document =>
        {
            for (var i = start; i <= end; i++)
                document.Layers[i].IsClipped = false;
        });
    }

    public void ToggleClippingMask()
    {
        if (Document.ActiveLayer?.IsClipped == true)
            ReleaseClippingMask();
        else
            CreateClippingMask();
    }

    private void ReleaseDeletedBase(int index)
    {
        if (!LayerClipping.IsBase(Document.Layers, index))
            return;
        var end = LayerClipping.FindEndIndex(Document.Layers, index);
        for (var i = index + 1; i <= end; i++)
            if (Document.Layers[i].Locked)
                throw new InvalidOperationException("Unlock clipped layers before deleting their base.");
        for (var i = index + 1; i <= end; i++)
            Document.Layers[i].IsClipped = false;
    }

    /// <summary>Reorder complete clipping units, never silently attach a chain to a different base.</summary>
    private void MoveLayerStack(int delta)
    {
        var layers = Document.Layers;
        var active = Document.ActiveLayer;
        if (active is null || delta == 0)
            return;
        var units = new List<(int Start, int Count)>();
        var selected = -1;
        for (var i = 0; i < layers.Count;)
        {
            var end = LayerClipping.FindEndIndex(layers, i);
            if (layers.GetRange(i, end - i + 1).Any(layer => layer.Id == active.Id))
                selected = units.Count;
            units.Add((i, end - i + 1));
            i = end + 1;
        }
        if (selected < 0)
            return;
        var target = (int)Math.Clamp((long)selected + delta, 0, units.Count - 1);
        if (selected == target)
            return;
        var unit = units[selected];
        if (layers.Skip(unit.Start).Take(unit.Count).Any(layer => layer.Locked))
            throw new InvalidOperationException("Unlock the clipping unit before reordering it.");
        var destination = target < selected ? units[target].Start : units[target].Start + units[target].Count - unit.Count;
        Execute("Reorder layer", document =>
        {
            var moving = document.Layers.GetRange(unit.Start, unit.Count);
            document.Layers.RemoveRange(unit.Start, unit.Count);
            document.Layers.InsertRange(destination, moving);
        });
    }

    private bool DuplicateClippingBase(Layer layer)
    {
        var start = Document.Layers.IndexOf(layer);
        if (!LayerClipping.IsBase(Document.Layers, start))
            return false;
        var end = LayerClipping.FindEndIndex(Document.Layers, start);
        Execute("Duplicate clipping group", document =>
        {
            var copies = document.Layers.GetRange(start, end - start + 1).Select(item =>
            {
                var copy = item.Snapshot();
                copy.Id = Guid.NewGuid();
                copy.Name += " copy";
                return copy;
            }).ToList();
            document.Layers.InsertRange(end + 1, copies);
            document.ActiveLayerId = copies[0].Id;
        });
        return true;
    }
}
