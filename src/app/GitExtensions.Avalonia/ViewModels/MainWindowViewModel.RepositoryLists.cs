using CommunityToolkit.Mvvm.Input;

namespace GitExtensions.Desktop.ViewModels;

// Start > "Favorite repositories…" (kept out of the main file).
public sealed partial class MainWindowViewModel
{
    private RepositoryListsViewModel? _repositoryLists;

    /// <summary>The "Favorite repositories" dialog; saving reloads the dashboard.</summary>
    public RepositoryListsViewModel RepositoryLists => _repositoryLists ??= new RepositoryListsViewModel(
        _history,
        message => ErrorMessage = message,
        () => Dashboard.LoadAsync());

    [RelayCommand]
    private Task ShowRepositoryListsAsync() => RepositoryLists.OpenAsync();
}
