using CommunityToolkit.Mvvm.ComponentModel;
using GitCommands;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.Revision;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The "Diff" tab: the files changed by a revision (or between two revisions, or in the working tree) and the diff of one of them. Read only.
/// </summary>
public sealed partial class RevisionDiffViewModel : ObservableObject
{
    private readonly List<ChangedFileViewModel> _allFiles = [];
    private int _running;
    private CancellationTokenSource? _filesCancellation;
    private CancellationTokenSource? _fileCancellation;
    private Func<ChangedFileViewModel, CancellationToken, Task<IReadOnlyList<DiffLine>>>? _diffLoader;
    private readonly HashSet<string> _collapsedFolders = new(StringComparer.Ordinal);
    private string _headerText = string.Empty;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private ChangedFileViewModel? _selectedFile;

    [ObservableProperty]
    private string _notice = string.Empty;

    [ObservableProperty]
    private string _message = "Select a commit to see its changes.";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _tabHeader = "Diff";

    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>Gets a value indicating whether a load (file list or diff) is still running.</summary>
    public bool IsBusy => _running > 0;

    /// <summary>The files shown (after filtering).</summary>
    public RangeObservableCollection<ChangedFileViewModel> Files { get; } = [];

    /// <summary>The files shown as a tree (when <see cref="ShowTree"/>): the header node holding folders or extension groups.</summary>
    public RangeObservableCollection<ChangeTreeNode> Tree { get; } = [];

    /// <summary>How the files are listed: flat, by folder or by extension; remembered for the next start.</summary>
    public DiffGrouping Grouping
    {
        get => DiffListLayout.Grouping;
        set
        {
            if (DiffListLayout.Grouping != value)
            {
                DiffListLayout.Grouping = value;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowTree));
            OnPropertyChanged(nameof(GroupByPath));
            OnPropertyChanged(nameof(GroupByExtension));
            OnPropertyChanged(nameof(GroupByNone));
            ApplyFilter();
        }
    }

    public bool ShowTree => Grouping != DiffGrouping.None;

    public bool GroupByNone
    {
        get => Grouping == DiffGrouping.None;
        set
        {
            if (value)
            {
                Grouping = DiffGrouping.None;
            }
        }
    }

    public bool GroupByPath
    {
        get => Grouping == DiffGrouping.Path;
        set
        {
            if (value)
            {
                Grouping = DiffGrouping.Path;
            }
        }
    }

    public bool GroupByExtension
    {
        get => Grouping == DiffGrouping.Extension;
        set
        {
            if (value)
            {
                Grouping = DiffGrouping.Extension;
            }
        }
    }

    /// <summary>The diff of <see cref="SelectedFile"/>.</summary>
    public RangeObservableCollection<DiffLine> Lines { get; } = [];

    /// <summary>The revision whose side of the diff is shown (for blame and history), or <see langword="null"/> for the working tree.</summary>
    public string? Revision { get; private set; }

    /// <summary>The other side of the diff when two revisions are compared, or null when a commit is compared with its first parent.</summary>
    public string? BaseRevision { get; private set; }

    /// <summary>Shows what a commit changed (against its first parent; a root commit lists what it added).</summary>
    public Task ShowRevisionAsync(GitModule module, GitRevision revision)
    {
        string to = revision.ObjectId.ToString();
        bool hasParent = revision.HasParent;
        string notice = revision.ParentIds is { Count: > 1 } ? "Merge commit: showing the changes against the first parent." : string.Empty;
        BaseRevision = null;
        string parent = revision.HasParent ? revision.FirstParentId.ToShortString() : "nothing";
        _headerText = $"Diff with A {parent}: {revision.Subject}";
        return ShowAsync(
            to,
            notice,
            $"{revision.ObjectId.ToShortString()}",
            cancellationToken => RevisionGit.GetChangedFiles(module, null, to, hasParent, cancellationToken),
            (file, cancellationToken) => Task.Run(() => RevisionGit.GetFileDiff(module, null, to, hasParent, file.Item, cancellationToken), cancellationToken));
    }

    /// <summary>Shows the differences between two revisions (<paramref name="from"/> is the older side).</summary>
    public Task ShowDiffAsync(GitModule module, ObjectId from, ObjectId to)
    {
        string fromSha = from.ToString();
        string toSha = to.ToString();
        BaseRevision = fromSha;
        _headerText = $"Diff A {from.ToShortString()} ↔ B {to.ToShortString()}";
        return ShowAsync(
            toSha,
            string.Empty,
            $"{from.ToShortString()} ↔ {to.ToShortString()}",
            cancellationToken => RevisionGit.GetChangedFiles(module, fromSha, toSha, hasParent: true, cancellationToken),
            (file, cancellationToken) => Task.Run(() => RevisionGit.GetFileDiff(module, fromSha, toSha, hasParent: true, file.Item, cancellationToken), cancellationToken));
    }

    /// <summary>Shows the uncommitted changes (staged and unstaged) of the working tree.</summary>
    public Task ShowWorkingTreeAsync(GitModule module)
    {
        // The existing working tree diff (untracked files, staged versus unstaged, size guards) does the rendering.
        _workingModule = module;
        BaseRevision = null;
        _headerText = "Uncommitted changes";
        FileDiffViewModel workingDiff = new(module, () => Task.CompletedTask);
        return ShowAsync(
            null,
            string.Empty,
            "Uncommitted changes",
            cancellationToken =>
            {
                (List<GitItemStatus> items, string? error) = GitErrorMessages.SplitStatusError(module.GetAllChangedFiles(), module.WorkingDir);
                return error is null ? items : [new GitItemStatus(string.Empty) { IsStatusOnly = true, ErrorMessage = error }];
            },
            async (file, cancellationToken) =>
            {
                await workingDiff.ShowAsync(file);
                cancellationToken.ThrowIfCancellationRequested();
                return workingDiff.Lines.Count == 0 && workingDiff.Message.Length > 0
                    ? [new DiffLine(DiffLineKind.Info, workingDiff.Message, null, null)]
                    : [.. workingDiff.Lines];
            });
    }

    /// <summary>Empties the tab and cancels loads in flight.</summary>
    public void Clear()
    {
        CancelAll();
        _diffLoader = null;
        Revision = null;
        BaseRevision = null;
        _workingModule = null;
        CancelRevert();
        _allFiles.Clear();
        Files.Clear();
        Tree.Clear();
        Lines.Clear();
        SelectedFile = null;
        Notice = Title = string.Empty;
        TabHeader = "Diff";
        IsLoading = false;
        Message = "Select a commit to see its changes.";
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnSelectedFileChanged(ChangedFileViewModel? value) => _ = ShowFileAsync(value);

    private async Task ShowAsync(
        string? revision,
        string notice,
        string title,
        Func<CancellationToken, IReadOnlyList<GitItemStatus>> loadFiles,
        Func<ChangedFileViewModel, CancellationToken, Task<IReadOnlyList<DiffLine>>> loadDiff)
    {
        CancelAll();
        CancellationTokenSource cancellation = new();
        _filesCancellation = cancellation;
        CancellationToken cancellationToken = cancellation.Token;

        Revision = revision;
        _diffLoader = loadDiff;
        _allFiles.Clear();
        Files.Clear();
        Tree.Clear();
        Lines.Clear();
        SelectedFile = null;
        Notice = notice;
        Title = title;
        TabHeader = "Diff";
        IsLoading = true;
        Message = "Loading…";

        _running++;
        try
        {
            // Keystroke-fast selection changes cancel each other before any git process is started.
            await Task.Delay(60, cancellationToken);

            // The git calls block, so keep them off the UI thread.
            IReadOnlyList<GitItemStatus> items = await Task.Run(() => loadFiles(cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            string? error = null;
            foreach (GitItemStatus item in items)
            {
                if (item.IsStatusOnly)
                {
                    error = item.ErrorMessage ?? item.Name;
                }
                else
                {
                    _allFiles.Add(new ChangedFileViewModel(item));
                }
            }

            TabHeader = _allFiles.Count == 0 ? "Diff" : $"Diff ({_allFiles.Count})";
            Message = error ?? (_allFiles.Count == 0 ? "No changes." : string.Empty);
            IsLoading = false;
            ApplyFilter();
            SelectedFile = Files.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            // A newer selection replaced this one and owns the state now.
        }
        catch (Exception ex)
        {
            Message = $"Failed to load the changes: {ex.Message}";
            IsLoading = false;
        }
        finally
        {
            _running--;
        }
    }

    private async Task ShowFileAsync(ChangedFileViewModel? file)
    {
        if (_fileCancellation is not null)
        {
            await _fileCancellation.CancelAsync();
        }

        CancellationTokenSource cancellation = new();
        _fileCancellation = cancellation;
        Lines.Clear();
        if (file is null || _diffLoader is null)
        {
            return;
        }

        _running++;
        try
        {
            IReadOnlyList<DiffLine> lines = await _diffLoader(file, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            DiffLimits.Truncate(ref lines);
            Lines.ReplaceAll(lines);
            if (lines.Count == 0)
            {
                Lines.Add(new DiffLine(DiffLineKind.Info, "No textual changes.", null, null));
            }
        }
        catch (OperationCanceledException)
        {
            // A newer file replaced this one.
        }
        catch (Exception ex)
        {
            Lines.ReplaceAll([new DiffLine(DiffLineKind.Info, $"Failed to load the diff: {ex.Message}", null, null)]);
        }
        finally
        {
            _running--;
        }
    }

    private void ApplyFilter()
    {
        string filter = FilterText.Trim();
        IEnumerable<ChangedFileViewModel> matches = filter.Length == 0
            ? _allFiles
            : _allFiles.Where(file => file.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        List<ChangedFileViewModel> shown = [.. matches];
        Files.ReplaceAll(shown);
        RebuildTree(shown);
        if (SelectedFile is { } selected && !shown.Contains(selected))
        {
            SelectedFile = shown.FirstOrDefault();
        }
    }

    private void RebuildTree(List<ChangedFileViewModel> shown)
    {
        if (!ShowTree || shown.Count == 0)
        {
            Tree.Clear();
            return;
        }

        List<ChangeTreeNode> children = Grouping == DiffGrouping.Extension
            ? ChangeTreeNode.BuildByExtension(shown, _collapsedFolders)
            : ChangeTreeNode.Build(shown, _collapsedFolders);
        Tree.ReplaceAll([ChangeTreeNode.Header($"({shown.Count}) {_headerText}", children)]);
    }

    private void CancelAll()
    {
        _filesCancellation?.Cancel();
        _fileCancellation?.Cancel();
    }
}
