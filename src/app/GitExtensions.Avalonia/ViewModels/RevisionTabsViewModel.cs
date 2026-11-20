using CommunityToolkit.Mvvm.ComponentModel;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The tab control below the revision grid: Commit | Diff | File tree | Console | Build Report.
///  The Commit tab is <see cref="MainWindowViewModel.CommitDetails"/>; this holds the other tabs and the selected one.
/// </summary>
public sealed partial class RevisionTabsViewModel : ObservableObject
{
    public const int CommitTab = 0;
    public const int DiffTab = 1;
    public const int FileTreeTab = 2;
    public const int ConsoleTab = 3;
    public const int BuildReportTab = 4;

    private readonly Func<string, bool, string?, Task> _openFileHistory;
    private bool _workingTree;

    [ObservableProperty]
    private int _selectedTabIndex;

    public RevisionTabsViewModel(Func<string, bool, string?, Task> openFileHistory, CommandLogService? commandLog = null, ViewOptionsViewModel? options = null)
    {
        _openFileHistory = openFileHistory;
        Console = commandLog ?? new CommandLogService();
        Options = options ?? new ViewOptionsViewModel(new ViewOptionsStore(Path.Combine(Path.GetTempPath(), "ge-no-view-options-" + Guid.NewGuid().ToString("N") + ".json")));
        Options.PropertyChanged += (_, _) => OnOptionsChanged();
    }

    /// <summary>The View menu options that decide which tabs and panes are shown.</summary>
    public ViewOptionsViewModel Options { get; }

    public RevisionDiffViewModel Diff { get; } = new();

    public RevisionFileTreeViewModel FileTree { get; } = new();

    public CommandLogService Console { get; }

    /// <summary>Gets a value indicating whether any tab is still loading in the background.</summary>
    public bool IsBusy => Diff.IsBusy || FileTree.IsBusy;

    /// <summary>Gets a value indicating whether the Build Report tab is shown: only with a configured build server (none is supported yet).</summary>
    public bool ShowBuildReport => Options.ShowBuildReportTab;

    /// <summary>Gets a value indicating whether the Commit tab applies (it does not for the uncommitted changes).</summary>
    public bool ShowCommitTab => !_workingTree && Options.ShowCommitTab && Options.CommitInfoPosition == CommitInfoLayout.Below;

    /// <summary>Gets a value indicating whether the File tree tab applies (it does not for the uncommitted changes).</summary>
    public bool ShowFileTreeTab => !_workingTree && Options.ShowFileTreeTab;

    /// <summary>Gets a value indicating whether the Diff tab is shown (View > Show tabs).</summary>
    public bool ShowDiffTab => Options.ShowDiffTab;

    /// <summary>Gets a value indicating whether the Console tab is shown (View > Show tabs).</summary>
    public bool ShowConsoleTab => Options.ShowConsoleTab;

    /// <summary>Gets a value indicating whether the commit info is a separate pane left of the tabs.</summary>
    public bool ShowCommitInfoLeft => !_workingTree && Options.ShowCommitTab && Options.CommitInfoPosition == CommitInfoLayout.Left;

    /// <summary>Gets a value indicating whether the commit info is a separate pane right of the tabs.</summary>
    public bool ShowCommitInfoRight => !_workingTree && Options.ShowCommitTab && Options.CommitInfoPosition == CommitInfoLayout.Right;

    /// <summary>Gets a value indicating whether any tab is shown.</summary>
    public bool HasVisibleTabs => ShowCommitTab || ShowDiffTab || ShowFileTreeTab || ShowConsoleTab || ShowBuildReport;

    /// <summary>Shows what <paramref name="revision"/> changed and its files.</summary>
    public void ShowCommit(GitModule module, GitRevision revision)
    {
        SetMode(workingTree: false);
        _ = Diff.ShowRevisionAsync(module, revision);
        FileTree.SetRevision(module, revision.ObjectId.ToString());
    }

    /// <summary>Shows the differences between two revisions in the Diff tab.</summary>
    public void ShowDiff(GitModule module, ObjectId from, ObjectId to)
    {
        SetMode(workingTree: false);
        SelectedTabIndex = DiffTab;
        _ = Diff.ShowDiffAsync(module, from, to);
        FileTree.SetRevision(module, to.ToString());
    }

    /// <summary>Shows the uncommitted changes in the Diff tab.</summary>
    public void ShowWorkingTree(GitModule module)
    {
        SetMode(workingTree: true);
        if (SelectedTabIndex is CommitTab or FileTreeTab)
        {
            SelectedTabIndex = DiffTab;
        }

        _ = Diff.ShowWorkingTreeAsync(module);
        FileTree.Clear();
    }

    public void Clear()
    {
        SetMode(workingTree: false);
        Diff.Clear();
        FileTree.Clear();
    }

    /// <summary>Opens the file history window (or blame) of a file, at the revision shown where there is one.</summary>
    public Task OpenFileHistoryAsync(string path, bool blame, string? revision)
        => _openFileHistory(path, blame, revision);

    /// <summary>Switches to the File tree tab and selects <paramref name="path"/> there.</summary>
    public void ShowInFileTree(string path)
    {
        if (!ShowFileTreeTab)
        {
            return;
        }

        FileTree.Reveal(path);
        SelectedTabIndex = FileTreeTab;
    }

    partial void OnSelectedTabIndexChanged(int value)
        => FileTree.IsActive = value == FileTreeTab;

    private void SetMode(bool workingTree)
    {
        bool changed = _workingTree != workingTree;
        _workingTree = workingTree;
        if (changed)
        {
            NotifyTabVisibility();
        }
    }

    private void OnOptionsChanged()
    {
        NotifyTabVisibility();
        bool[] visible = [ShowCommitTab, ShowDiffTab, ShowFileTreeTab, ShowConsoleTab, ShowBuildReport];
        if (SelectedTabIndex is < 0 or > 4 || !visible[SelectedTabIndex])
        {
            int first = Array.IndexOf(visible, true);
            if (first >= 0)
            {
                SelectedTabIndex = first;
            }
        }
    }

    private void NotifyTabVisibility()
    {
        OnPropertyChanged(nameof(ShowCommitTab));
        OnPropertyChanged(nameof(ShowDiffTab));
        OnPropertyChanged(nameof(ShowFileTreeTab));
        OnPropertyChanged(nameof(ShowConsoleTab));
        OnPropertyChanged(nameof(ShowBuildReport));
        OnPropertyChanged(nameof(ShowCommitInfoLeft));
        OnPropertyChanged(nameof(ShowCommitInfoRight));
        OnPropertyChanged(nameof(HasVisibleTabs));
    }
}
