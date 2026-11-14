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

    public RevisionTabsViewModel(Func<string, bool, string?, Task> openFileHistory, CommandLogService? commandLog = null)
    {
        _openFileHistory = openFileHistory;
        Console = commandLog ?? new CommandLogService();
    }

    public RevisionDiffViewModel Diff { get; } = new();

    public RevisionFileTreeViewModel FileTree { get; } = new();

    public CommandLogService Console { get; }

    /// <summary>Gets a value indicating whether any tab is still loading in the background.</summary>
    public bool IsBusy => Diff.IsBusy || FileTree.IsBusy;

    /// <summary>Gets a value indicating whether the Build Report tab is shown: only with a configured build server (none is supported yet).</summary>
    public bool ShowBuildReport => false;

    /// <summary>Gets a value indicating whether the Commit tab applies (it does not for the uncommitted changes).</summary>
    public bool ShowCommitTab => !_workingTree;

    /// <summary>Gets a value indicating whether the File tree tab applies (it does not for the uncommitted changes).</summary>
    public bool ShowFileTreeTab => !_workingTree;

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

    partial void OnSelectedTabIndexChanged(int value)
        => FileTree.IsActive = value == FileTreeTab;

    private void SetMode(bool workingTree)
    {
        bool changed = _workingTree != workingTree;
        _workingTree = workingTree;
        if (changed)
        {
            OnPropertyChanged(nameof(ShowCommitTab));
            OnPropertyChanged(nameof(ShowFileTreeTab));
        }
    }
}
