using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensions.Desktop.Services;

public sealed partial class WindowService
{
    private CommitWindow? _commitWindow;

    /// <summary>
    ///  The open Commit window, if any (one per repository, non-modal).
    /// </summary>
    public CommitWindow? CommitWindow => _commitWindow;

    partial void OnShowCommitWindow()
    {
        if (_commitWindow is { DataContext: CommitWindowViewModel existing } open)
        {
            if (Main.OpenWorkingDir is not { } current || string.Equals(existing.WorkingDir, current, StringComparison.Ordinal))
            {
                open.Activate();
                return;
            }

            open.ForceClose();
        }

        CommitWindowViewModel? viewModel = Main.CreateCommitWindowViewModel();
        if (viewModel is null)
        {
            Main.ErrorMessage = "Open a repository before committing.";
            return;
        }

        CommitWindow window = new() { DataContext = viewModel };
        _commitWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_commitWindow, window))
            {
                _commitWindow = null;
            }

            viewModel.Dispose();
        };
        window.Show(Owner);

        // Loading handles its own failures (it reports them in the window and the banner).
        _ = viewModel.LoadAsync();
    }
}
