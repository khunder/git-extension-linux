using Avalonia.Controls;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views.ContextMenus;

namespace GitExtensions.Desktop.Views;

// The context menus of the Commit window: the file lists and the commit message.
public sealed partial class CommitWindow
{
    // The context menus of the two file lists act on the selection (a right click selects the row under the pointer).
    private void AttachContextMenus()
    {
        MessageBox.ContextFlyout = null;
        MenuKit.AttachBuilt(MessageBox, _ => ViewModel is { } viewModel ? MessageMenu(viewModel) : null);
        MenuKit.AttachBuilt(UnstagedList, row => row is ChangedFileViewModel file ? FileListContextMenu.Build(MenuContext(file, staged: false)) : null);
        MenuKit.AttachBuilt(StagedList, row => row is ChangedFileViewModel file ? FileListContextMenu.Build(MenuContext(file, staged: true)) : null);
    }

    private FileMenuContext MenuContext(ChangedFileViewModel row, bool staged)
    {
        CommitWindowViewModel? viewModel = ViewModel;
        IReadOnlyList<ChangedFileViewModel> selection = viewModel is null ? [row] : staged ? viewModel.StagedSelection : viewModel.UnstagedSelection;
        if (!selection.Contains(row))
        {
            selection = [row];
        }

        return new FileMenuContext
        {
            Paths = [.. selection.Select(f => f.Name)],
            WorkingDir = viewModel?.WorkingDir ?? string.Empty,
            Clipboard = new TopLevelClipboardService(this),
            OpenHistory = (_, blame) =>
            {
                if (blame)
                {
                    viewModel?.ShowFileBlame(row);
                }
                else
                {
                    viewModel?.ShowFileHistoryCommand.Execute(row);
                }
            },
            Stage = staged ? null : () => viewModel?.StageSelectedCommand.Execute(null),
            Unstage = staged ? () => viewModel?.UnstageSelectedCommand.Execute(null) : null,
            Reset = () => viewModel?.ResetSelectedCommand.Execute(null),
            Owner = this,
            Changed = viewModel is null ? null : viewModel.RefreshAsync,
            AddToGitIgnore = !staged && viewModel?.CanAddSelectionToGitIgnore == true ? () => viewModel.AddToGitIgnoreCommand.Execute(null) : null,
        };
    }

    // Edit commands of the message box plus the templates and recent messages that the buttons below it offer.
    private List<Control> MessageMenu(CommitWindowViewModel viewModel)
    {
        bool hasText = !string.IsNullOrEmpty(MessageBox.Text);
        bool hasSelection = MessageBox.SelectionStart != MessageBox.SelectionEnd;
        return
        [
            MenuKit.Item("Undo", gesture: "Ctrl+Z", click: MessageBox.Undo),
            MenuKit.Item("Redo", gesture: "Ctrl+Y", click: MessageBox.Redo),
            MenuKit.Sep(),
            MenuKit.Item("Cut", gesture: "Ctrl+X", click: MessageBox.Cut, enabled: hasSelection),
            MenuKit.Item("Copy", "CopyToClipboard", MessageBox.Copy, "Ctrl+C", enabled: hasSelection),
            MenuKit.Item("Paste", gesture: "Ctrl+V", click: MessageBox.Paste),
            MenuKit.Item("Select all", gesture: "Ctrl+A", click: MessageBox.SelectAll, enabled: hasText),
            MenuKit.Sep(),
            MenuKit.Sub(
                "Commit message templates",
                "CommitTemplates",
                viewModel.Templates.Select(t => MenuKit.Item(t.Name, "CommitTemplates", () => viewModel.ApplyTemplate(t))),
                viewModel.Templates.Count > 0,
                "There are no commit message templates"),
            MenuKit.Sub(
                "Recent messages",
                "Message",
                viewModel.RecentMessages.Select(m => MenuKit.Item(Summarize(m), "Message", () => viewModel.UseRecentMessage(m))),
                viewModel.HasRecentMessages,
                "There are no recent messages"),
            MenuKit.Sep(),
            MenuKit.Item("Clear message", "DeleteText", () => viewModel.Commit.Message = string.Empty, enabled: hasText),
        ];
    }
}
