using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

// The dialogs of the revision grid and the refs tree: revision operations (cherry pick, checkout, delete branches, archive,
// reset, clean), the commit context menu prompts, the refs tree prompts and the commit notes.
// The title and size of a dialog depend on the operation, so they are supplied each time it opens.
public sealed partial class MainWindow
{
    private void BindRevisionDialogs(MainWindowViewModel viewModel)
    {
        // The prompts' view-models copy to the clipboard through the main window.
        viewModel.Actions.Clipboard ??= new TopLevelClipboardService(this);
        viewModel.Refs.Clipboard ??= new TopLevelClipboardService(this);

        RevisionOperationsViewModel ops = viewModel.RevisionOps;
        DialogHost.Bind(this, ops, () => new RevisionOperationPromptView { DataContext = ops }, "Revision operation", 540, 330, canResize: false, describe: () => (
            ops.DialogTitle,
            540,
            ops.Operation switch
            {
                RevisionOperation.CherryPick => 330,
                RevisionOperation.CheckoutRevision => 320,
                RevisionOperation.DeleteBranch => 420,
                RevisionOperation.Archive => 280,
                RevisionOperation.Reset => 320,
                _ => 460,
            }));

        CommitActionsViewModel actions = viewModel.Actions;
        DialogHost.Bind(this, actions, () => new CommitActionPromptView { DataContext = actions }, "Commit", 460, 190, canResize: false, describe: () => (
            actions.DialogTitle,
            actions.PromptKind == CommitPromptKind.ConfirmHardReset ? 540 : 460,
            actions.PromptKind switch { CommitPromptKind.ConfirmHardReset => 250, CommitPromptKind.Revert => 160, _ => 170 }));

        RefsTreeViewModel refs = viewModel.Refs;
        DialogHost.Bind(this, refs, () => new RefsPromptView { DataContext = refs }, "Branches and tags", 480, 220, canResize: false, describe: () => (
            refs.DialogTitle,
            refs.PromptKind == RefPromptKind.NewTag ? 520 : 480,
            refs.PromptKind switch
            {
                RefPromptKind.NewTag => 300,
                RefPromptKind.NewBranch => 240,
                RefPromptKind.SetUpstream => 190,
                RefPromptKind.RenameBranch => 170,
                _ => 190,
            }));

        CommitDetailsViewModel details = viewModel.CommitDetails;
        DialogHost.Bind(this, details, () => new CommitNotesView { DataContext = details }, "Commit notes", 520, 300, describe: () => ("Commit notes", 520, details.IsConfirmingNoteDelete ? 160 : 300));
    }
}
