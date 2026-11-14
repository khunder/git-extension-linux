using CommunityToolkit.Mvvm.Input;

namespace GitExtensions.Desktop.ViewModels;

public sealed partial class RefsTreeViewModel
{
    /// <summary>Fetches a remote and removes the remote-tracking branches that no longer exist on it.</summary>
    [RelayCommand]
    private async Task FetchAndPruneRemoteAsync(RefNodeViewModel? node)
    {
        if (_module is { } module && node?.RemoteName is { } remote)
        {
            await RunNetworkAsync($"Fetch and prune {remote}", ["fetch", "--progress", "--prune", remote], module);
        }
    }
}
