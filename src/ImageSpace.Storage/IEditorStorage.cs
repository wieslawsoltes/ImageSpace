namespace ImageSpace.Storage;

public sealed record OpenedFile(string Name, byte[] Bytes);
public interface IEditorStorage
{
    Task<OpenedFile?> OpenAsync(CancellationToken cancellationToken = default);
    /// <summary>Returns false when the user cancels; true when the host accepted the file write or browser download.</summary>
    Task<bool> SaveAsync(string name, byte[] bytes, string contentType, CancellationToken cancellationToken = default);
    Task<byte[]?> ReadRecoveryAsync(CancellationToken cancellationToken = default);
    Task WriteRecoveryAsync(byte[] bytes, CancellationToken cancellationToken = default);
    Task ClearRecoveryAsync(CancellationToken cancellationToken = default);
}
