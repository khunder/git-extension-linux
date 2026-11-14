using CommunityToolkit.Mvvm.Input;

namespace GitExtensions.Desktop.ViewModels;

// Repository > "Remote repositories…" (kept out of the main file; "Remote" is the fetch/pull/push state).
public sealed partial class MainWindowViewModel
{
    private RemotesViewModel? _remotes;

    /// <summary>The "Remote repositories" dialog. Changing remotes reloads the refs of the open repository.</summary>
    public RemotesViewModel Remotes => _remotes ??= new RemotesViewModel(
        () => _module?.WorkingDir,
        message => Status = message,
        async () =>
        {
            if (HasOpenedRepository)
            {
                await OpenAsync(RepositoryPath);
            }
        });

    [RelayCommand]
    private Task ShowRemotesAsync() => Remotes.OpenAsync();
}
