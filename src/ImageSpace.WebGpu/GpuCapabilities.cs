using ImageSpace.Core;
using ImageSpace.Filters;
namespace ImageSpace.WebGpu;

public sealed record GpuCapabilities(bool Available,string Backend,long MaximumBufferSize,string? Reason=null);
public interface IComputeFilterBackend
{
    Task<GpuCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken=default);
    /// <summary>Returns null for unsupported kernels. Implementations must not mutate source pixels.</summary>
    Task<PixelSurface?> ApplyAsync(PixelSurface source,FilterKind kind,float amount,float secondary,CancellationToken cancellationToken=default);
}
public static class GpuKernels
{
    public static bool Supports(FilterKind kind)=>kind is FilterKind.Invert or FilterKind.Grayscale or FilterKind.Sepia or FilterKind.BrightnessContrast or FilterKind.Saturation or FilterKind.Gamma or FilterKind.Threshold or FilterKind.Posterize;
}
