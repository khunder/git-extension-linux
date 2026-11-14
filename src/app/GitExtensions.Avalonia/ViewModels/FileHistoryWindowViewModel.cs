using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.FileHistory;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The File History window: the commits of one file (renames followed), and for the selected commit its
///  Commit info, the file's Diff, the file's View (content) and the file's Blame.
/// </summary>
public sealed partial class FileHistoryWindowViewModel : ObservableObject
{
    private readonly Func<GitModule?> _getModule;
    private readonly List<Task> _pending = [];
    private List<FileHistoryRowViewModel> _all = [];
    private CancellationTokenSource? _historyCancellation;
    private CancellationTokenSource? _diffCancellation;
    private CancellationTokenSource? _viewCancellation;
    private bool _selectingFromBlame;
    private bool _suppressBranchReload;
    private string? _requestedRevision;
    private string? _commitKey;
    private string? _diffKey;
    private string? _viewKey;
    private string? _blameKey;
    private string _blameHash = string.Empty;
    private string _blamePath = string.Empty;

    [ObservableProperty]
    private string _filePath;

    [ObservableProperty]
    private string _repositoryPath;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private FileHistoryRowViewModel? _selectedRow;

    [ObservableProperty]
    private int _selectedTabIndex = (int)FileHistoryTab.Diff;

    [ObservableProperty]
    private FileHistoryBranchOption? _selectedBranch;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private bool _filterByMessage = true;

    [ObservableProperty]
    private bool _filterByAuthor = true;

    [ObservableProperty]
    private bool _filterByHash = true;

    [ObservableProperty]
    private bool _followRenames = true;

    [ObservableProperty]
    private string _diffMessage = string.Empty;

    [ObservableProperty]
    private string _viewMessage = string.Empty;

    public FileHistoryWindowViewModel(Func<GitModule?> getModule, string filePath, string repositoryPath)
    {
        _getModule = getModule;
        _filePath = filePath;
        _repositoryPath = repositoryPath;
        Blame = new BlameViewModel(getModule, OnBlameCommitSelected);
    }

    /// <summary>Gets or sets where "copy" commands put text. Set by the window.</summary>
    public IClipboardService? Clipboard { get; set; }

    public string Title => $"File History - {FilePath} - {RepositoryPath}";

    public FileHistoryColumns Columns { get; } = new();

    /// <summary>The (filtered) commits shown in the grid.</summary>
    public RangeObservableCollection<FileHistoryRowViewModel> Rows { get; } = [];

    public ObservableCollection<FileHistoryBranchOption> BranchOptions { get; } = [];

    public CommitDetailsViewModel CommitDetails { get; } = new();

    public RangeObservableCollection<DiffLine> DiffLines { get; } = [];

    public RangeObservableCollection<FileViewLine> ViewLines { get; } = [];

    public BlameViewModel Blame { get; }

    /// <summary>Gets the number of commits before filtering.</summary>
    public int TotalCount => _all.Count;

    /// <summary>
    ///  Loads the history and shows <paramref name="tab"/>. With <paramref name="revision"/> that commit is selected
    ///  (and blamed) instead of the newest one.
    /// </summary>
    public async Task OpenAsync(FileHistoryTab tab, string? revision = null)
    {
        SelectedTabIndex = (int)tab;
        _requestedRevision = revision;
        await ReloadAsync(revision, allowFallback: revision is not null);
        await WhenIdleAsync();
    }

    /// <summary>
    ///  Selects <paramref name="revision"/> (and switches to <paramref name="tab"/>) in an already loaded window.
    /// </summary>
    public async Task NavigateAsync(FileHistoryTab tab, string? revision)
    {
        SelectedTabIndex = (int)tab;
        if (revision is not null)
        {
            FileHistoryRowViewModel? row = Rows.FirstOrDefault(r => r.Hash.StartsWith(revision, StringComparison.OrdinalIgnoreCase));
            if (row is not null)
            {
                SelectedRow = row;
            }
        }

        await WhenIdleAsync();
    }

    /// <summary>Completes when every load started so far (commit info, diff, view, blame) has finished.</summary>
    public async Task WhenIdleAsync()
    {
        while (true)
        {
            Task[] tasks;
            lock (_pending)
            {
                _pending.RemoveAll(task => task.IsCompleted);
                tasks = [.. _pending];
            }

            if (tasks.Length == 0)
            {
                return;
            }

            await Task.WhenAll(tasks).ConfigureAwait(true);
        }
    }

    /// <summary>Cancels everything in flight (window closing).</summary>
    public async Task CloseAsync()
    {
        await Task.WhenAll(
            _historyCancellation?.CancelAsync() ?? Task.CompletedTask,
            _diffCancellation?.CancelAsync() ?? Task.CompletedTask,
            _viewCancellation?.CancelAsync() ?? Task.CompletedTask,
            CommitDetails.ClearAsync(),
            Blame.CancelAsync());
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _commitKey = _diffKey = _viewKey = _blameKey = null;
        await ReloadAsync(SelectedRow?.Hash, allowFallback: false);
        await WhenIdleAsync();
    }

    [RelayCommand]
    private Task CopyHashAsync()
        => SelectedRow is { } row && Clipboard is { } clipboard ? clipboard.SetTextAsync(row.Hash) : Task.CompletedTask;

    [RelayCommand]
    private Task CopyPathAsync()
        => Clipboard is { } clipboard ? clipboard.SetTextAsync(FilePath) : Task.CompletedTask;

    partial void OnFilePathChanged(string value) => OnPropertyChanged(nameof(Title));

    partial void OnRepositoryPathChanged(string value) => OnPropertyChanged(nameof(Title));

    partial void OnFilterTextChanged(string value) => ApplyFilter(SelectedRow?.Hash);

    partial void OnFilterByMessageChanged(bool value) => ApplyFilter(SelectedRow?.Hash);

    partial void OnFilterByAuthorChanged(bool value) => ApplyFilter(SelectedRow?.Hash);

    partial void OnFilterByHashChanged(bool value) => ApplyFilter(SelectedRow?.Hash);

    partial void OnFollowRenamesChanged(bool value)
    {
        if (!_suppressBranchReload)
        {
            _ = ReloadTrackedAsync();
        }
    }

    partial void OnSelectedBranchChanged(FileHistoryBranchOption? value)
    {
        if (value is not null && !_suppressBranchReload)
        {
            _ = ReloadTrackedAsync();
        }
    }

    partial void OnSelectedTabIndexChanged(int value) => LoadForCurrentTab();

    partial void OnSelectedRowChanged(FileHistoryRowViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        if (!_selectingFromBlame)
        {
            // Selecting a commit in the grid blames the file as of that commit; clicking a blame block keeps the shown blame.
            _blameHash = value.Hash;
            _blamePath = value.Entry.Path;
        }

        Track(LoadCommitAsync(value));
        Track(LoadDiffAsync(value));
        LoadForCurrentTab();
    }

    private Task ReloadTrackedAsync()
    {
        Task task = ReloadAsync(SelectedRow?.Hash, allowFallback: false);
        Track(task);
        return task;
    }

    private void Track(Task task)
    {
        lock (_pending)
        {
            _pending.Add(task);
        }
    }

    private async Task ReloadAsync(string? preferredHash, bool allowFallback)
    {
        if (_historyCancellation is not null)
        {
            await _historyCancellation.CancelAsync();
        }

        CancellationTokenSource cancellation = new();
        _historyCancellation = cancellation;
        CancellationToken cancellationToken = cancellation.Token;

        GitModule? module = _getModule();
        if (module is null)
        {
            Message = "No repository is open.";
            return;
        }

        string path = FilePath;
        string? branch = SelectedBranch?.Value;
        bool follow = FollowRenames;
        Message = "Loading history…";
        IsLoading = true;
        try
        {
            // The git calls block: run both off the UI thread, in parallel.
            Task<(string? Current, IReadOnlyList<string> Branches)> branchesTask = Task.Run(() => FileHistoryGit.GetBranches(module, cancellationToken), cancellationToken);
            Task<IReadOnlyList<FileHistoryEntry>> historyTask = Task.Run(() => FileHistoryGit.GetHistory(module, path, branch, follow, cancellationToken), cancellationToken);
            (string? current, IReadOnlyList<string> branches) = await branchesTask;
            IReadOnlyList<FileHistoryEntry> entries = await historyTask;
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            FillBranches(current, branches);
            DateTimeOffset now = DateTimeOffset.Now;
            _all = [.. entries.Select(entry => new FileHistoryRowViewModel(entry, Columns, path, now))];
            OnPropertyChanged(nameof(TotalCount));

            if (allowFallback && preferredHash is not null
                && !_all.Any(row => row.Hash.StartsWith(preferredHash, StringComparison.OrdinalIgnoreCase))
                && BranchOptions.FirstOrDefault(option => option.Value == FileHistoryGit.AllBranches) is { } all)
            {
                // The commit is not on the current branch: look at every branch instead.
                _suppressBranchReload = true;
                SelectedBranch = all;
                _suppressBranchReload = false;
                await ReloadAsync(preferredHash, allowFallback: false);
                return;
            }

            ApplyFilter(preferredHash);
            if (_all.Count == 0)
            {
                ClearContent();
            }

            IsLoading = false;
        }
        catch (OperationCanceledException)
        {
            // A newer reload replaced this one.
        }
        catch (Exception ex)
        {
            Message = $"Failed to load history: {ex.Message}";
            IsLoading = false;
        }
    }

    private void FillBranches(string? current, IReadOnlyList<string> branches)
    {
        string? previous = SelectedBranch?.Value;
        bool hadSelection = SelectedBranch is not null;
        List<FileHistoryBranchOption> options =
        [
            new("All branches", FileHistoryGit.AllBranches),
            new(current ?? "HEAD", null),
            .. branches.Where(name => name != current).Select(name => new FileHistoryBranchOption(name, name))
        ];

        _suppressBranchReload = true;
        BranchOptions.Clear();
        foreach (FileHistoryBranchOption option in options)
        {
            BranchOptions.Add(option);
        }

        SelectedBranch = hadSelection ? options.FirstOrDefault(option => option.Value == previous) ?? options[1] : options[1];
        _suppressBranchReload = false;
    }

    private void ApplyFilter(string? preferredHash)
    {
        string text = FilterText.Trim();
        IEnumerable<FileHistoryRowViewModel> rows = _all;
        if (text.Length > 0)
        {
            rows = rows.Where(row =>
                (FilterByMessage && row.Subject.Contains(text, StringComparison.OrdinalIgnoreCase))
                || (FilterByAuthor && row.Author.Contains(text, StringComparison.OrdinalIgnoreCase))
                || (FilterByHash && row.Hash.StartsWith(text, StringComparison.OrdinalIgnoreCase)));
        }

        List<FileHistoryRowViewModel> filtered = [.. rows];
        Rows.ReplaceAll(filtered);
        Message = _all.Count == 0 ? "No commits touch this file." : filtered.Count == 0 ? "No commit matches the filter." : string.Empty;

        FileHistoryRowViewModel? selection = preferredHash is null
            ? null
            : filtered.FirstOrDefault(row => row.Hash.StartsWith(preferredHash, StringComparison.OrdinalIgnoreCase));
        selection ??= filtered.FirstOrDefault();

        // The list clears its selection when the items are replaced: select afresh (same-commit loads are skipped by their keys).
        SelectedRow = null;
        SelectedRow = selection;
    }

    private void ClearContent()
    {
        _commitKey = _diffKey = _viewKey = _blameKey = null;
        DiffLines.Clear();
        ViewLines.Clear();
        DiffMessage = string.Empty;
        ViewMessage = string.Empty;
        Track(CommitDetails.ClearAsync());
        Track(Blame.LoadAsync(string.Empty, string.Empty));
    }

    private void LoadForCurrentTab()
    {
        if (SelectedRow is not { } row)
        {
            return;
        }

        switch ((FileHistoryTab)SelectedTabIndex)
        {
            case FileHistoryTab.View:
                Track(LoadViewAsync(row));
                break;
            case FileHistoryTab.Blame:
                Track(EnsureBlameAsync());
                break;
        }
    }

    private void OnBlameCommitSelected(ObjectId id)
    {
        string hash = id.ToString();
        FileHistoryRowViewModel? row = Rows.FirstOrDefault(r => string.Equals(r.Hash, hash, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            _selectingFromBlame = true;
            try
            {
                SelectedRow = row;
            }
            finally
            {
                _selectingFromBlame = false;
            }

            return;
        }

        // A commit that the grid does not show (filtered out, or the file lives under another name): show just its info.
        if (_getModule() is { } module)
        {
            _commitKey = hash;
            Track(CommitDetails.LoadAsync(module, id, string.Empty));
        }
    }

    private Task EnsureBlameAsync()
    {
        string key = _blameHash + ":" + _blamePath;
        if (_blameHash.Length == 0 || key == _blameKey)
        {
            return Task.CompletedTask;
        }

        _blameKey = key;
        return Blame.LoadAsync(_blamePath, _blameHash);
    }

    private async Task LoadCommitAsync(FileHistoryRowViewModel row)
    {
        if (_commitKey == row.Hash || _getModule() is not { } module || !ObjectId.TryParse(row.Hash, out ObjectId id))
        {
            return;
        }

        _commitKey = row.Hash;
        string refs = string.Join(", ", row.Refs.Select(r => r.Name));
        await CommitDetails.LoadAsync(module, id, refs);
    }

    private async Task LoadDiffAsync(FileHistoryRowViewModel row)
    {
        string key = row.Hash + ":" + row.Entry.Path;
        if (key == _diffKey)
        {
            return;
        }

        _diffKey = key;
        if (_diffCancellation is not null)
        {
            await _diffCancellation.CancelAsync();
        }

        CancellationTokenSource cancellation = new();
        _diffCancellation = cancellation;
        DiffLines.Clear();
        DiffMessage = "Loading diff…";
        if (_getModule() is not { } module)
        {
            return;
        }

        try
        {
            IReadOnlyList<DiffLine> lines = await Task.Run(() => FileHistoryGit.GetFileDiff(module, row.Entry, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            DiffLimits.Truncate(ref lines);
            DiffLines.ReplaceAll(lines);
            DiffMessage = lines.Count == 0 ? "No textual changes." : string.Empty;
        }
        catch (OperationCanceledException)
        {
            // Another commit was selected.
        }
        catch (Exception ex)
        {
            DiffMessage = $"Failed to load diff: {ex.Message}";
        }
    }

    private async Task LoadViewAsync(FileHistoryRowViewModel row)
    {
        string key = row.Hash + ":" + row.Entry.Path;
        if (key == _viewKey)
        {
            return;
        }

        _viewKey = key;
        if (_viewCancellation is not null)
        {
            await _viewCancellation.CancelAsync();
        }

        CancellationTokenSource cancellation = new();
        _viewCancellation = cancellation;
        ViewLines.Clear();
        ViewMessage = "Loading file…";
        if (_getModule() is not { } module)
        {
            return;
        }

        try
        {
            (IReadOnlyList<string> lines, string message) = await Task.Run(() => FileHistoryGit.GetFileContent(module, row.Hash, row.Entry.Path, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            ViewLines.ReplaceAll(lines.Select((text, index) => new FileViewLine(index + 1, text)));
            ViewMessage = message;
        }
        catch (OperationCanceledException)
        {
            // Another commit was selected.
        }
        catch (Exception ex)
        {
            ViewMessage = $"Failed to load file: {ex.Message}";
        }
    }
}
