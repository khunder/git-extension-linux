using CommunityToolkit.Mvvm.ComponentModel;

namespace GitExtensions.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private CommitViewModel? _commit;

    partial void OnCommitChanged(CommitViewModel? oldValue, CommitViewModel? newValue) => oldValue?.Dispose();

    private CommitViewModel CreateCommitViewModel(GitCommands.GitModule module)
    {
        // Keep a message being typed when the repository is reloaded (e.g. after a checkout).
        string draft = Commit is { } previous && _commitModuleDir == module.WorkingDir ? previous.Message : string.Empty;
        _commitModuleDir = module.WorkingDir;
        CommitViewModel viewModel = new(module, ChangedFiles, _git.Messages, ReloadAfterCommitAsync) { Message = draft };
        _ = viewModel.LoadAsync();
        return viewModel;
    }

    private string? _commitModuleDir;

    private async Task ReloadAfterCommitAsync()
    {
        await OpenAsync(RepositoryPath);
        SelectedCommit = Commits.FirstOrDefault();
    }
}
