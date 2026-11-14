using CommunityToolkit.Mvvm.Input;

namespace GitExtensions.Desktop.ViewModels;

// Repository > Git maintenance > Recover lost objects… (kept out of the main file).
public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private void ShowLostObjects()
    {
        if (!HasOpenedRepository)
        {
            return;
        }

        ToolsContext context = new(() => _module, () => OpenAsync(RepositoryPath), message => ErrorMessage = message, message => Status = message, path => OpenAsync(path));
        LostObjectsViewModel dialog = new(context, Dialogs.Close);
        Dialogs.Show(dialog);
        _ = dialog.ScanAsync();
    }
}
