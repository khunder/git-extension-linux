using System.Collections.Specialized;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The main toolbar under the menu: refresh, panels, repository and branch lists, pull/push/commit, stash, file explorer, shell and settings.
/// </summary>
public sealed partial class MainToolbarView : UserControl
{
    private MainWindowViewModel? _viewModel;

    public MainToolbarView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as MainWindowViewModel);

        Describe(RefreshButton, "Refresh");
        Describe(LeftPanelButton, "Show or hide the left panel", "Ctrl+B");
        Describe(LayoutButton, "Toggle the split layout");
        Describe(CommitInfoPositionButton, "Position of the commit info");
        Describe(RepoPathButton, "Repository path", "Recent repositories; Ctrl+R shows the list");
        Describe(BranchButton, "Current branch", "Check out another local branch");
        Describe(PullButton, "Pull");
        Describe(PushButton, "Push", "Ctrl+Shift+K");
        Describe(CommitButton, "Commit", "Opens the Commit window (Ctrl+K)");
        Describe(FileExplorerButton, "File explorer");
        Describe(ShellButton, "Shell");
        Describe(SettingsButton, "Settings", "Open the settings (Ctrl+,)");

        RepoPathButton.Flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        BranchButton.Flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        ((MenuFlyout)RepoPathButton.Flyout).Opening += (_, _) => FillRecent();
        ((MenuFlyout)BranchButton.Flyout).Opening += (_, _) => FillBranches();
        ((MenuFlyout)PullButton.Flyout!).Opening += (_, _) => FillRemotes();
    }

    /// <summary>
    ///  The repository path button; its flyout lists the recent repositories.
    /// </summary>
    public Button RepoPath => RepoPathButton;

    /// <summary>
    ///  The branch button; its flyout lists the local branches.
    /// </summary>
    public Button BranchList => BranchButton;

    private static void Describe(Control control, string name, string? help = null)
    {
        AutomationProperties.SetName(control, name);
        if (help is not null)
        {
            AutomationProperties.SetHelpText(control, help);
        }
    }

    private void Attach(MainWindowViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.UiRequested -= OnUiRequested;
        }

        _viewModel = viewModel;
        if (viewModel is not null)
        {
            viewModel.UiRequested += OnUiRequested;
        }
    }

    private void OnUiRequested(UiTarget target)
    {
        switch (target)
        {
            case UiTarget.PathBox:
            case UiTarget.ShowRecent:
                RepoPathButton.Flyout?.ShowAt(RepoPathButton);
                break;
            case UiTarget.ShowBranches:
                BranchButton.Flyout?.ShowAt(BranchButton);
                break;
            case UiTarget.ShowStash:
                if (this.GetVisualDescendants().OfType<StashMenuButton>().FirstOrDefault() is { } stash)
                {
                    stash.Flyout?.ShowAt(stash);
                    _viewModel?.Stash.RequestStashCommand.Execute(null);
                }

                break;
            case UiTarget.ShowTools:
                ToolsButton.Flyout?.ShowAt(RepoPathButton);
                break;
        }
    }

    private void FillRecent()
    {
        MenuFlyout flyout = (MenuFlyout)RepoPathButton.Flyout!;
        flyout.Items.Clear();
        if (_viewModel is null)
        {
            return;
        }

        if (_viewModel.Recent.Items.Count == 0)
        {
            flyout.Items.Add(new MenuItem { Header = "(no recent repositories)", IsEnabled = false });
        }

        foreach (RecentRepositoryItem item in _viewModel.Recent.Items.Take(30))
        {
            bool current = string.Equals(item.Path.TrimEnd('/', '\\'), _viewModel.RepositoryPath.TrimEnd('/', '\\'), StringComparison.Ordinal);
            MenuItem entry = new() { Header = item.Path.Replace("_", "__", StringComparison.Ordinal), Tag = item, ToggleType = MenuItemToggleType.Radio, GroupName = "Repo", IsChecked = current };
            entry.Click += (_, _) => _viewModel.Recent.OpenCommand.Execute(item);
            flyout.Items.Add(entry);
        }

        flyout.Items.Add(new Separator());
        MenuItem open = new() { Header = "_Open…", Command = _viewModel.BrowseCommand };
        flyout.Items.Add(open);
    }

    private void FillBranches()
    {
        MenuFlyout flyout = (MenuFlyout)BranchButton.Flyout!;
        flyout.Items.Clear();
        if (_viewModel is null)
        {
            return;
        }

        if (_viewModel.LocalBranches.Count == 0)
        {
            flyout.Items.Add(new MenuItem { Header = "(no local branches)", IsEnabled = false });
        }

        foreach (string name in _viewModel.LocalBranches)
        {
            MenuItem entry = new() { Header = name.Replace("_", "__", StringComparison.Ordinal), Tag = name, ToggleType = MenuItemToggleType.Radio, GroupName = "Branch", IsChecked = name == _viewModel.Branch };
            entry.Click += (_, _) => _ = _viewModel.CheckoutBranchAsync(name);
            flyout.Items.Add(entry);
        }
    }

    private void FillRemotes()
    {
        RemoteChoiceMenuItem.Items.Clear();
        if (_viewModel is null)
        {
            return;
        }

        foreach (string remote in _viewModel.Remote.Remotes)
        {
            MenuItem entry = new() { Header = remote.Replace("_", "__", StringComparison.Ordinal), ToggleType = MenuItemToggleType.Radio, GroupName = "Remote", IsChecked = remote == _viewModel.Remote.SelectedRemote };
            entry.Click += (_, _) => _viewModel.Remote.SelectedRemote = remote;
            RemoteChoiceMenuItem.Items.Add(entry);
        }
    }
}
