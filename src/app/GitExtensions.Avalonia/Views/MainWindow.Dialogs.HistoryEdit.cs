using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

// The dialog of the grid's Advanced and Compare commands (fixup, squash, amend, edit, reword, autosquash, reset another branch, compare).
public sealed partial class MainWindow
{
    private void BindHistoryEditDialogs(MainWindowViewModel viewModel)
    {
        HistoryEditViewModel edit = viewModel.HistoryEdit;
        DialogHost.Bind(this, edit, () => new HistoryEditView { DataContext = edit }, "History", 540, 260, canResize: false, describe: () => (
            edit.Title,
            540,
            edit.Kind switch
            {
                HistoryEditKind.Amend or HistoryEditKind.Reword => 380,
                HistoryEditKind.ResetBranch => 250,
                HistoryEditKind.CompareWithRevision => 250,
                _ => 250,
            }));
    }
}
