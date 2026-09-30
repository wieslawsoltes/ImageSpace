namespace ImageSpace.Filters;

/// <summary>
/// Requested array payload for one streaming Gaussian evaluation. Excludes source/result
/// tiles, object headers, pool bucket rounding, retained pool buffers and async machinery.
/// </summary>
public readonly record struct GaussianBlurWorkspace(int Radius, int CachedRows, long IntermediateBytes,
    long RowBufferBytes, long KernelBytes)
{
    public long RequestedScratchBytes => IntermediateBytes + RowBufferBytes + KernelBytes;
}
