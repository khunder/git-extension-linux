using CommunityToolkit.Mvvm.Input;

namespace GitExtensions.Desktop.ViewModels;

// Commands menu: cherry pick, checkout revision, delete branch, archive, reset, clean (kept out of the main file).
public sealed partial class MainWindowViewModel
{
    private RevisionOperationsViewModel? _revisionOps;

    /// <summary>The inline dialogs of the revision operations.</summary>
    public RevisionOperationsViewModel RevisionOps => _revisionOps ??= new RevisionOperationsViewModel(
        () => _module,
        () => OpenAsync(RepositoryPath),
        message => ErrorMessage = message,
        message => Status = message,
        (title, arguments) => Remote.RunGitAsync(title, arguments),
        () => SelectedCommit?.ObjectId.ToString());

    [RelayCommand]
    private void ShowCherryPick() => RevisionOps.OpenCherryPick();

    [RelayCommand]
    private void ShowCheckoutRevision() => RevisionOps.OpenCheckoutRevision();

    [RelayCommand]
    private Task ShowDeleteBranchAsync() => RevisionOps.OpenDeleteBranchAsync();

    [RelayCommand]
    private void ShowArchive() => RevisionOps.OpenArchive();

    [RelayCommand]
    private void ShowReset() => RevisionOps.OpenReset();

    [RelayCommand]
    private void ShowClean() => RevisionOps.OpenClean();
}
