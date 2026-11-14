using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

// Entry points used by the file list context menus of the Commit window.
public sealed partial class CommitWindowViewModel
{
    /// <summary>Opens the blame of a file (in the file history window when the application provides one).</summary>
    public void ShowFileBlame(ChangedFileViewModel file)
    {
        if (_main.WindowService is IFileHistoryWindowService windows)
        {
            windows.ShowFileHistoryWindow(file.Name, revision: null, FileHistoryTab.Blame);
        }
        else
        {
            _ = _main.FileHistory.OpenAsync(file.Name, blame: true);
        }
    }

    /// <summary>The unstaged selection can be added to .gitignore: it is not empty and has only untracked files.</summary>
    public bool CanAddSelectionToGitIgnore => UnstagedSelection.Count > 0 && UnstagedSelection.All(f => !f.Item.IsTracked);
}
