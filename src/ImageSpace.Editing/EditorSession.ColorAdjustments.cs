using ImageSpace.Core;
using ImageSpace.Imaging;

namespace ImageSpace.Editing;

public sealed partial class EditorSession
{
    /// <summary>Insert above the entire active clipping chain. An active selection becomes an editable mask.</summary>
    public Layer AddColorAdjustment(AdjustmentKind kind)
    {
        if (kind is not (AdjustmentKind.ChannelMixer or AdjustmentKind.Exposure))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (IsInTransaction)
            throw new InvalidOperationException("Finish the current gesture before adding an adjustment.");
        if (Document.Layers.Count >= 128)
            throw new InvalidOperationException("The document already contains 128 layers.");
        var layer = new Layer
        {
            Kind = LayerKind.Adjustment,
            Adjustment = kind,
            Name = kind == AdjustmentKind.ChannelMixer ? "Channel Mixer" : "Exposure",
            Width = Document.Width,
            Height = Document.Height
        };
        if (Document.Selection is not null)
            layer.Mask = MaskOperations.FromSelection(Document, layer);
        var active = Document.Layers.IndexOf(Document.ActiveLayer!);
        var index = active >= 0 ? LayerClipping.FindEndIndex(Document.Layers, active) + 1 : Document.Layers.Count;
        Execute("New " + layer.Name + " adjustment", document =>
        {
            document.Layers.Insert(index, layer);
            document.ActiveLayerId = layer.Id;
            document.EditMask = false;
        });
        return layer;
    }

    public void SetChannelMixer(ChannelMixerAdjustment settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var layer = RequireColorAdjustment(AdjustmentKind.ChannelMixer);
        if (layer.ChannelMixer == settings)
            return;
        Execute("Edit Channel Mixer", _ => layer.ChannelMixer = settings);
    }

    public void SetExposure(ExposureAdjustment settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        var layer = RequireColorAdjustment(AdjustmentKind.Exposure);
        if (layer.Exposure == settings)
            return;
        Execute("Edit Exposure", _ => layer.Exposure = settings);
    }

    private Layer RequireColorAdjustment(AdjustmentKind kind)
    {
        if (IsInTransaction || Document.EditMask || Document.ActiveLayer is not { Kind: LayerKind.Adjustment, Locked: false } layer || layer.Adjustment != kind)
            throw new InvalidOperationException("Select an unlocked matching adjustment and finish the current gesture.");
        return layer;
    }

    public ColorAdjustmentGesture BeginColorAdjustmentEdit()
    {
        var kind = Document.ActiveLayer?.Adjustment ?? AdjustmentKind.None;
        if (kind is not (AdjustmentKind.ChannelMixer or AdjustmentKind.Exposure))
            throw new InvalidOperationException("A Channel Mixer or Exposure adjustment is required.");
        var layer = RequireColorAdjustment(kind);
        Begin("Edit " + (kind == AdjustmentKind.ChannelMixer ? "Channel Mixer" : "Exposure"));
        return new ColorAdjustmentGesture(this, layer, _before!);
    }

    /// <summary>
    /// Owns the exact history snapshot opened by BeginColorAdjustmentEdit. Stale pointer callbacks
    /// cannot edit, commit or cancel a replacement transaction after Undo/document switching.
    /// Preview changes deliberately do not notify/rebuild the whole workbench.
    /// </summary>
    public sealed class ColorAdjustmentGesture : IDisposable
    {
        private readonly EditorSession _session;
        private readonly Layer _layer;
        private readonly ImageDocument _snapshot;
        private readonly ImageDocument _document;
        private readonly ChannelMixerAdjustment _mixer;
        private readonly ExposureAdjustment _exposure;
        private bool _closed;
        internal ColorAdjustmentGesture(EditorSession session, Layer layer, ImageDocument snapshot)
        {
            _session = session;
            _layer = layer;
            _snapshot = snapshot;
            _document = session.Document;
            _mixer = layer.ChannelMixer;
            _exposure = layer.Exposure;
        }
        private bool OwnsTransaction => !_closed && ReferenceEquals(_session._before, _snapshot);
        public bool IsActive => OwnsTransaction && ReferenceEquals(_session.Document, _document) &&
            ReferenceEquals(_document.ActiveLayer, _layer) && !_document.EditMask && !_layer.Locked;

        public void Set(ChannelMixerAdjustment settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            settings.Validate();
            if (!IsActive || _layer.Adjustment != AdjustmentKind.ChannelMixer)
                throw new InvalidOperationException("The Channel Mixer gesture is no longer active.");
            _layer.ChannelMixer = settings;
        }
        public void Set(ExposureAdjustment settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            settings.Validate();
            if (!IsActive || _layer.Adjustment != AdjustmentKind.Exposure)
                throw new InvalidOperationException("The Exposure gesture is no longer active.");
            _layer.Exposure = settings;
        }
        public void Commit()
        {
            if (!OwnsTransaction)
            {
                _closed = true;
                return;
            }
            var changed = IsActive && (_layer.ChannelMixer != _mixer || _layer.Exposure != _exposure);
            _closed = true;
            if (changed)
                _session.Commit();
            else
                _session.Cancel();
        }
        public void Dispose()
        {
            if (!OwnsTransaction)
            {
                _closed = true;
                return;
            }
            _closed = true;
            _session.Cancel();
        }
    }
}
