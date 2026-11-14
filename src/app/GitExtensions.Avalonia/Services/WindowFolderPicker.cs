using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace GitExtensions.Desktop.Services;

public sealed class WindowFolderPicker(TopLevel owner) : IFolderPicker
{
    public async Task<string?> PickFolderAsync(string title)
    {
        IReadOnlyList<IStorageFolder> folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
