using ImageSpace.Skia;

namespace ImageSpace.Editor;

public sealed partial class ImageViewport
{
    private readonly MaskPreviewRenderer _maskPreviewRenderer = new();
    private MaskPreviewMode _maskPreview;
    private Guid _previewLayerId;
    public MaskPreviewMode MaskPreview => _maskPreview;

    public void SetMaskPreview(MaskPreviewMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        _maskPreview = Session.Document.EditMask && Session.Document.ActiveLayer?.Mask is not null
            ? mode : MaskPreviewMode.Composite;
        _previewLayerId = Session.Document.ActiveLayerId;
        Invalidate();
        ViewChanged?.Invoke();
    }
}
