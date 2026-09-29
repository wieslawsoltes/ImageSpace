using ImageSpace.Core;
using ImageSpace.Filters;
namespace ImageSpace.WebGpu;

public sealed record GpuCapabilities(bool Available, string Backend, long MaximumBufferSize, string? Reason = null);
public interface IComputeFilterBackend
{
    Task<GpuCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
    Task<PixelSurface?> ApplyAsync(PixelSurface source, FilterKind kind, float amount, float secondary, CancellationToken cancellationToken = default);
}
public static class GpuKernels
{
    // Seeded Noise retains the existing .NET Random sequence on the CPU; do not
    // silently replace it with a different GPU random distribution/sequence.
    public static bool Supports(FilterKind kind) => Enum.IsDefined(kind) && kind != FilterKind.Noise;
}
