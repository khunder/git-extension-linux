using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace GitExtensions.Desktop.Services;

public sealed class WindowFileSavePicker(TopLevel owner) : IFileSavePicker
{
    public async Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension)
    {
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
        });

        return file?.TryGetLocalPath();
    }
}
