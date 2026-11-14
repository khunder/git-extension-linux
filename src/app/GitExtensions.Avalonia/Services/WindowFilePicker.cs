using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace GitExtensions.Desktop.Services;

public sealed class WindowFilePicker(TopLevel owner) : IFilePicker
{
    public async Task<string?> PickFileAsync(string title)
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
