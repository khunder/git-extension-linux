namespace GitExtensions.Desktop.ViewModels;

// Branch and tag management wiring (kept out of the main file).
public sealed partial class MainWindowViewModel
{
    private void InitializeBranchManagement()
    {
        Refs.ReportError = message => ErrorMessage = message;
        Refs.SelectedCommitProvider = () => SelectedCommit?.ObjectId;
        Refs.RunRemote = (title, arguments) => Remote.RunGitAsync(title, arguments);
    }
}
