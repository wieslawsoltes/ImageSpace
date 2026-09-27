using ImageSpace.Storage;
using Windows.Storage;
using Windows.Storage.Pickers;
namespace ImageSpace.App;

internal sealed class DesktopEditorStorage : IEditorStorage
{
    private static readonly string RecoveryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageSpace", "recovery.imagespace");
    public async Task<OpenedFile?> OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        foreach (var ext in new[] { ".imagespace", ".psd", ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" })
            picker.FileTypeFilter.Add(ext);
        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return null;
        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size > 128UL * 1024 * 1024)
            throw new InvalidDataException("File exceeds 128 MiB.");
        using var stream = await file.OpenStreamForReadAsync();
        using var output = new MemoryStream();
        await stream.CopyToAsync(output, cancellationToken);
        return new(file.Name, output.ToArray());
    }
    public async Task<bool> SaveAsync(string name, byte[] bytes, string contentType, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(name), SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeChoices.Add(contentType, new List<string> { Path.GetExtension(name) });
        var file = await picker.PickSaveFileAsync();
        if (file is null)
            return false;
        await FileIO.WriteBytesAsync(file, bytes);
        return true;
    }
    public async Task<byte[]?> ReadRecoveryAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(RecoveryPath))
            return null;
        var info = new FileInfo(RecoveryPath);
        if (info.Length > 128L * 1024 * 1024)
            throw new InvalidDataException("Recovery file exceeds 128 MiB.");
        return await File.ReadAllBytesAsync(RecoveryPath, cancellationToken);
    }
    public async Task WriteRecoveryAsync(byte[] bytes, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RecoveryPath)!);
        var temporary = RecoveryPath + ".tmp";
        await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
        File.Move(temporary, RecoveryPath, true);
    }
    public Task ClearRecoveryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(RecoveryPath))
            File.Delete(RecoveryPath);
        return Task.CompletedTask;
    }
}
