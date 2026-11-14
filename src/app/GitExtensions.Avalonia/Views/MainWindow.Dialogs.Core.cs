using System.ComponentModel;
using Avalonia.Controls;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

// The dialogs of settings, clone/init, favorites, credentials, shortcuts, stashes, the repository tools and the command log.
public sealed partial class MainWindow
{
    private void BindCoreDialogs(MainWindowViewModel viewModel)
    {
        DialogHost.Bind(this, viewModel.Settings, () => new SettingsView { DataContext = viewModel.Settings }, "Settings", 980, 720);
        DialogHost.Bind(this, viewModel.Clone, () => new CloneView { DataContext = viewModel.Clone }, "Clone repository", 620, 500);
        DialogHost.Bind(this, viewModel.Init, () => new InitView { DataContext = viewModel.Init }, "Create new repository", 580, 360);
        DialogHost.Bind(this, viewModel.RepositoryLists, () => new RepositoryListsView { DataContext = viewModel.RepositoryLists }, "Favorite repositories", 820, 640);
        DialogHost.Bind(this, viewModel.ShortcutsHelp, () => new ShortcutsHelpView { DataContext = viewModel.ShortcutsHelp }, "Keyboard shortcuts", 640, 600);
        DialogHost.Bind(this, viewModel.Stash, () => new StashView { DataContext = viewModel.Stash }, "Manage stashes", 900, 620);

        viewModel.Tools.Clipboard = new TopLevelClipboardService(this);
        DialogHost.Bind(this, viewModel.Tools.Reflog, () => new ReflogTabView { DataContext = viewModel.Tools.Reflog }, "Reflog", 1040, 640);
        DialogHost.Bind(this, viewModel.Tools.Submodules, () => new SubmodulesTabView { DataContext = viewModel.Tools.Submodules }, "Submodules", 840, 480);
        DialogHost.Bind(this, viewModel.Tools.Worktrees, () => new WorktreesTabView { DataContext = viewModel.Tools.Worktrees }, "Worktrees", 880, 480);
        DialogHost.Bind(this, viewModel.CommandLogDialog, () => new CommandLogDialogView { DataContext = viewModel.CommandLogDialog }, "Command log", 880, 540);

        BindCredentialDialog(viewModel.Credentials);
    }

    // Git and ssh can ask for a credential while another dialog (clone, a remote operation...) is open: the prompt then
    // opens on top of that dialog, because the owner window is blocked by it. Closing the window answers "cancel".
    private void BindCredentialDialog(CredentialPromptViewModel credentials)
    {
        DialogWindow? dialog = null;

        void Sync()
        {
            if (credentials.IsOpen && dialog is null)
            {
                Window owner = DialogWindow.Open.LastOrDefault() ?? (Window)this;
                DialogWindow created = new("Authentication required", new CredentialPromptView { DataContext = credentials }, 460, 230, canResize: false);
                dialog = created;
                created.Closed += (_, _) =>
                {
                    if (ReferenceEquals(dialog, created))
                    {
                        dialog = null;
                    }

                    if (credentials.IsOpen)
                    {
                        // Closed with the title bar or Esc: answer the waiting git/ssh process with "cancel".
                        credentials.CancelCommand.Execute(null);
                    }
                };
                _ = created.ShowDialog(owner);
            }
            else if (!credentials.IsOpen && dialog is not null)
            {
                DialogWindow closing = dialog;
                dialog = null;
                closing.Close();
            }
        }

        credentials.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CredentialPromptViewModel.IsOpen))
            {
                Sync();
            }
        };
    }
}
