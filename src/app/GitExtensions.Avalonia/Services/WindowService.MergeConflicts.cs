using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensions.Desktop.Services;

// The merge conflicts window: one non-modal window, activated again when it is already open for the repository.
public sealed partial class WindowService : IMergeConflictsWindowService
{
    private MergeConflictsWindow? _mergeConflictsWindow;

    /// <summary>The open merge conflicts window, if any.</summary>
    public MergeConflictsWindow? MergeConflictsWindow => _mergeConflictsWindow;

    public void ShowMergeConflictsWindow()
    {
        if (_mergeConflictsWindow is { DataContext: MergeConflictsViewModel existing } open)
        {
            if (Main.OpenWorkingDir is { } current && string.Equals(existing.WorkingDir, current, StringComparison.Ordinal))
            {
                open.Activate();
                _ = existing.LoadAsync();
                return;
            }

            open.Close();
        }

        MergeConflictsViewModel? viewModel = Main.CreateMergeConflictsViewModel();
        if (viewModel is null)
        {
            Main.ErrorMessage = "Open a repository before solving merge conflicts.";
            return;
        }

        MergeConflictsWindow window = new() { DataContext = viewModel };
        _mergeConflictsWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_mergeConflictsWindow, window))
            {
                _mergeConflictsWindow = null;
            }

            viewModel.Dispose();
        };
        window.Show(Owner);
        _ = viewModel.LoadAsync();
    }
}
