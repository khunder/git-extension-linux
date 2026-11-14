using CommunityToolkit.Mvvm.ComponentModel;
using GitExtensions.Desktop.Services;

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

    /// <summary>
    ///  Reloads history and changes after the Commit window created a commit, and selects the new commit.
    /// </summary>
    public async Task ReloadAfterCommitAsync()
    {
        await OpenAsync(RepositoryPath);
        SelectedCommit = Commits.FirstOrDefault();
    }

    /// <summary>
    ///  The working directory of the open repository (null while none is open or it is being (re)loaded).
    /// </summary>
    public string? OpenWorkingDir => _module?.WorkingDir;

    /// <summary>
    ///  Pushes the current branch with the remote operations (used by "Commit &amp; push"); reloads the remote state first
    ///  because the repository may just have been reopened.
    /// </summary>
    public async Task PushCurrentBranchAsync()
    {
        await Remote.ReloadStateAsync();
        await Remote.PushCommand.ExecuteAsync(null);
    }

    /// <summary>
    ///  Creates the view-model of the Commit window for the open repository, or <see langword="null"/> when none is open.
    /// </summary>
    public CommitWindowViewModel? CreateCommitWindowViewModel(CommitWindowStore? store = null)
        => _module is { } module ? new CommitWindowViewModel(this, module, _git.Messages, store ?? CommitWindowStore.Current) : null;
}
