using System.Numerics;
using ImageSpace.Core;

namespace ImageSpace.Editor;

public sealed partial class ImageViewport
{
    private bool IsIndependentMask(Layer layer) =>
        Session.Document.EditMask && layer.Mask is not null && !layer.MaskLinked;

    private Vector2 FrameDimensions(Layer layer) => IsIndependentMask(layer)
        ? new Vector2(layer.Mask!.Width, layer.Mask.Height) : new Vector2(layer.Width, layer.Height);

    private Vector2 FrameToDocument(Layer layer, Vector2 local) => IsIndependentMask(layer)
        ? Vector2.Transform(local, layer.MaskDocumentTransform) : layer.ToDocument(local);

    private bool TransformIndependentMask(Layer layer, Vector2 world, bool constrained)
    {
        if (!IsIndependentMask(layer) || _original is null || _gesture is not ("move" or "resize" or "rotate"))
            return false;
        try
        {
            var original = _original.MaskDocumentTransform;
            var dimensions = new Vector2(_original.Mask!.Width, _original.Mask.Height);
            var result = original;
            if (_gesture == "move")
            {
                var delta = world - _start;
                if (constrained)
                {
                    if (Math.Abs(delta.X) > Math.Abs(delta.Y))
                        delta.Y = 0;
                    else
                        delta.X = 0;
                }
                result *= Matrix3x2.CreateTranslation(delta);
            }
            else if (_gesture == "resize")
                result = MaskGeometry.Resize(original, dimensions, Handles[_handle], world, constrained);
            else
                result = MaskGeometry.Rotate(original, dimensions, _start, world, constrained);
            layer.MaskPlacement = AffinePlacement.FromMatrix(result);
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or InvalidOperationException)
        {
            CancelGesture();
            Status?.Invoke(error.Message);
        }
        return true;
    }
}
