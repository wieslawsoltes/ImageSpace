using ImageSpace.Core;
using SkiaSharp;

namespace ImageSpace.Skia;

/// <summary>Renderer-owned, lazy SkSL blenders. Keeps clipping coverage constant across every blend mode.</summary>
internal sealed class ClippingBlenderCache : IDisposable
{
    private const string Source = """
        uniform blender mode;
        half4 main(half4 source, half4 destination) {
            half coverage = destination.a;
            if (coverage <= 0) return half4(0);
            half4 opaque = half4(destination.rgb / coverage, 1);
            half4 result = mode.eval(source, opaque);
            return half4(result.rgb * coverage, coverage);
        }
        """;
    private readonly Dictionary<LayerBlend, SKBlender> _blenders = [];
    private SKRuntimeEffect? _effect;
    private bool _disposed;
    public long Builds
    {
        get; private set;
    }

    public SKBlender Get(LayerBlend blend)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enum.IsDefined(blend))
            throw new ArgumentOutOfRangeException(nameof(blend));
        if (_blenders.TryGetValue(blend, out var cached))
            return cached;
        SKBlender result;
        if (blend == LayerBlend.Normal)
            result = SKBlender.CreateBlendMode(SKBlendMode.SrcATop);
        else
        {
            if (_effect is null)
            {
                _effect = SKRuntimeEffect.CreateBlender(Source, out var error)
                    ?? throw new InvalidOperationException("Unable to compile the clipping blender: " + error);
            }
            using var mode = SKBlender.CreateBlendMode(ImageRenderer.Blend(blend));
            using var children = new SKRuntimeEffectChildren(_effect) { ["mode"] = new SKRuntimeEffectChild(mode) };
            result = _effect.ToBlender(new SKRuntimeEffectUniforms(_effect), children)
                ?? throw new InvalidOperationException("Unable to instantiate the clipping blender.");
        }
        _blenders.Add(blend, result);
        Builds++;
        return result;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var blender in _blenders.Values)
            blender.Dispose();
        _blenders.Clear();
        _effect?.Dispose();
        _effect = null;
    }
}
