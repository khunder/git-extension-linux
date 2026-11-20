using System.ComponentModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The Commit window (like FormCommit): unstaged and staged lists, the diff of the selected file, the message and
///  the commit actions. It owns its own view of the working tree so it keeps working while the main window reloads.
/// </summary>
public sealed partial class CommitWindowViewModel : ObservableObject, IDisposable
{
    private readonly MainWindowViewModel _main;
    private readonly GitModule _module;
    private readonly IMessageDialogs _messages;
    private readonly CommitWindowStore _store;
    private readonly string _workingDir;
    private int _refreshVersion;
    private bool _syncingSelection;
    private bool _disposed;
    private Func<Task>? _pendingReset;
    private readonly HashSet<string> _collapsedFolders = [];
    private IReadOnlyList<ChangedFileViewModel> _unstagedSelection = [];
    private IReadOnlyList<ChangedFileViewModel> _stagedSelection = [];

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string? _branch;

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private bool _showUntracked = true;

    [ObservableProperty]
    private ChangedFileViewModel? _selectedUnstaged;

    [ObservableProperty]
    private ChangedFileViewModel? _selectedStaged;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private bool _pushAfterCommit;

    [ObservableProperty]
    private bool _closeAfterCommit;

    [ObservableProperty]
    private bool _isConfirmingReset;

    [ObservableProperty]
    private string _resetConfirmationText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isConfirmingClose;

    [ObservableProperty]
    private bool _isPushing;

    [ObservableProperty]
    private bool _showTree;

    /// <param name="main">The main window view-model that is refreshed after changes.</param>
    public CommitWindowViewModel(MainWindowViewModel main, GitModule module, IMessageDialogs messages, CommitWindowStore store)
    {
        _main = main;
        _module = module;
        _messages = messages;
        _store = store;
        _workingDir = module.WorkingDir;
        RepositoryName = new DirectoryInfo(module.WorkingDir.TrimEnd('/', '\\')).Name;
        _title = $"Commit to {RepositoryName}";
        _closeAfterCommit = store.CloseAfterCommit;
        _showTree = store.ShowTree;

        Commit = new CommitViewModel(module, AllChanges, messages, () => Task.CompletedTask);
        Commit.Committed += store.AddRecent;
        Commit.Committed += _ => LoadRecent();
        Diff = new FileDiffViewModel(module, AfterIndexChangeAsync, ReportError);
        Resolver = new ConflictResolverViewModel(() => _module, AfterIndexChangeAsync, ReportError);
        LoadRecent();
        BuildTemplates();
        UpdateStatus();
        _main.PropertyChanged += OnMainPropertyChanged;
    }

    /// <summary>
    ///  Raised when the window should close (commit done, repository closed or switched).
    /// </summary>
    public event Action? CloseRequested;

    public string RepositoryName { get; }

    /// <summary>The working directory of the repository this window commits to.</summary>
    public string WorkingDir => _workingDir;

    public bool HasNoUnstaged => Unstaged.Count == 0;

    public bool HasNoStaged => Staged.Count == 0;

    public CommitViewModel Commit { get; }

    public FileDiffViewModel Diff { get; }

    /// <summary>All changes, unfiltered.</summary>
    public RangeObservableCollection<ChangedFileViewModel> AllChanges { get; } = [];

    public RangeObservableCollection<ChangedFileViewModel> Unstaged { get; } = [];

    public RangeObservableCollection<ChangedFileViewModel> Staged { get; } = [];

    /// <summary>The unstaged files grouped by folder (the tree layout of the list).</summary>
    public RangeObservableCollection<ChangeTreeNode> UnstagedTree { get; } = [];

    /// <summary>The staged files grouped by folder (the tree layout of the list).</summary>
    public RangeObservableCollection<ChangeTreeNode> StagedTree { get; } = [];

    /// <summary>The flat list layout is shown (the opposite of <see cref="ShowTree"/>).</summary>
    public bool ShowList => !ShowTree;

    /// <summary>The last messages that were committed, newest first.</summary>
    public RangeObservableCollection<string> RecentMessages { get; } = [];

    /// <summary>Message templates: the git <c>commit.template</c> (if any) and conventional-commit prefixes.</summary>
    public RangeObservableCollection<CommitTemplate> Templates { get; } = [];

    public bool HasRecentMessages => RecentMessages.Count > 0;

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public string UnstagedHeader => $"Unstaged changes ({Unstaged.Count})";

    public string StagedHeader => $"Staged changes ({Staged.Count})";

    /// <summary>
    ///  A message was typed that is neither committed nor remembered: closing would lose it.
    /// </summary>
    public bool HasUnsavedMessage => !string.IsNullOrWhiteSpace(Commit.Message);

    /// <summary>The selection of the unstaged list (set by the view; the single selected item is used otherwise).</summary>
    public IReadOnlyList<ChangedFileViewModel> UnstagedSelection
    {
        get => _unstagedSelection.Count > 0 ? _unstagedSelection : SelectedUnstaged is { } one ? [one] : [];
        set => _unstagedSelection = value;
    }

    /// <summary>The selection of the staged list (set by the view; the single selected item is used otherwise).</summary>
    public IReadOnlyList<ChangedFileViewModel> StagedSelection
    {
        get => _stagedSelection.Count > 0 ? _stagedSelection : SelectedStaged is { } one ? [one] : [];
        set => _stagedSelection = value;
    }

    /// <summary>
    ///  Reads the changes, the branch and the pending merge message.
    /// </summary>
    public async Task LoadAsync()
    {
        await Commit.LoadAsync();
        ApplyConfiguredTemplate();
        await RefreshAsync();
    }

    /// <summary>
    ///  Re-reads the changed files and keeps the selection when possible. Concurrent calls: the newest wins.
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_disposed)
        {
            return;
        }

        int version = ++_refreshVersion;
        IsBusy = true;
        try
        {
            (List<ChangedFileViewModel> changes, string? error, string branch) = await Task.Run(() =>
            {
                (List<GitItemStatus> items, string? statusError) = GitErrorMessages.SplitStatusError(_module.GetAllChangedFiles(), _module.WorkingDir);
                return (items.Select(item => new ChangedFileViewModel(item)).ToList(), statusError, _module.GetSelectedBranch());
            });

            if (version != _refreshVersion || _disposed)
            {
                return;
            }

            Branch = branch;
            Title = TitleFor(branch);
            if (error is not null)
            {
                ReportError(error);
            }

            ChangedFileViewModel? previousUnstaged = SelectedUnstaged;
            ChangedFileViewModel? previousStaged = SelectedStaged;
            AllChanges.ReplaceAll(changes);
            ApplyFilter();

            ChangedFileViewModel? unstaged = previousUnstaged is null ? null : Unstaged.FirstOrDefault(c => c.Name == previousUnstaged.Name);
            ChangedFileViewModel? staged = previousStaged is null ? null : Staged.FirstOrDefault(c => c.Name == previousStaged.Name);
            _syncingSelection = true;
            try
            {
                SelectedUnstaged = unstaged;
                SelectedStaged = unstaged is null ? staged : null;
            }
            finally
            {
                _syncingSelection = false;
            }

            await ShowSelectedAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ReportError($"Could not read the changes of {_module.WorkingDir}: {ex.Message}");
        }
        finally
        {
            if (version == _refreshVersion)
            {
                IsBusy = false;
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _main.PropertyChanged -= OnMainPropertyChanged;
        Commit.Dispose();
    }

    /// <summary>Applies a message template (replaces the message when it is empty, otherwise inserts it in front).</summary>
    public void ApplyTemplate(CommitTemplate template)
        => Commit.Message = string.IsNullOrWhiteSpace(Commit.Message) ? template.Text : template.Text + Commit.Message;

    public void UseRecentMessage(string message) => Commit.Message = message;

    /// <summary>
    ///  Called when the user closes the window with a message that was neither committed nor saved and confirms the loss:
    ///  the message goes into the recent list so it can be restored.
    /// </summary>
    public void KeepDraftAsRecent()
    {
        if (HasUnsavedMessage)
        {
            _store.AddRecent(Commit.Message);
        }
    }

    public Task StageFilesAsync(IReadOnlyList<ChangedFileViewModel> files)
        => ChangeIndexAsync(module => WorkingTreeIndex.Stage(module, files), files.Count > 0);

    public Task UnstageFilesAsync(IReadOnlyList<ChangedFileViewModel> files)
        => ChangeIndexAsync(module => WorkingTreeIndex.Unstage(module, files), files.Count > 0);

    [RelayCommand]
    private Task StageSelectedAsync() => StageFilesAsync(UnstagedSelection);

    [RelayCommand]
    private Task UnstageSelectedAsync() => UnstageFilesAsync(StagedSelection);

    [RelayCommand]
    private Task StageAllAsync() => StageFilesAsync([.. Unstaged]);

    [RelayCommand]
    private Task UnstageAllAsync() => UnstageFilesAsync([.. Staged]);

    [RelayCommand]
    private void ShowFileHistory(ChangedFileViewModel? file)
    {
        if (file is not null)
        {
            _main.ShowFileHistoryWindowCommand.Execute(file.Name);
        }
    }

    /// <summary>
    ///  Asks to throw away the changes of <paramref name="files"/> (shown as a confirmation bar in the window).
    /// </summary>
    public void BeginReset(IReadOnlyList<ChangedFileViewModel> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        List<ChangedFileViewModel> copy = [.. files];
        ResetConfirmationText = copy.Count == 1
            ? $"Discard all changes to {copy[0].Name}? This cannot be undone."
            : $"Discard all changes to {copy.Count} files? This cannot be undone.";
        _pendingReset = () => ResetFilesAsync(copy);
        IsConfirmingReset = true;
    }

    [RelayCommand]
    private void ResetSelected() => BeginReset([.. UnstagedSelection, .. StagedSelection]);

    [RelayCommand]
    private void ResetAll()
    {
        ResetConfirmationText = "Reset all changes to HEAD? Staged and unstaged changes of tracked files are lost and cannot be recovered. Untracked files are kept.";
        _pendingReset = ResetEverythingAsync;
        IsConfirmingReset = true;
    }

    [RelayCommand]
    private async Task ConfirmResetAsync()
    {
        Func<Task>? reset = _pendingReset;
        CancelReset();
        if (reset is not null)
        {
            await reset();
        }
    }

    [RelayCommand]
    private void CancelReset()
    {
        _pendingReset = null;
        IsConfirmingReset = false;
    }

    /// <summary>
    ///  Escape: closes the window, but asks first when a typed message would be lost (a second Escape cancels the question).
    /// </summary>
    public void RequestEscape()
    {
        if (IsConfirmingClose)
        {
            IsConfirmingClose = false;
        }
        else if (HasUnsavedMessage)
        {
            IsConfirmingClose = true;
        }
        else
        {
            CloseRequested?.Invoke();
        }
    }

    /// <summary>
    ///  The window is being closed by the user: returns <see langword="false"/> (and asks) when a message would be lost.
    /// </summary>
    public bool CanCloseWithoutAsking()
    {
        if (!HasUnsavedMessage)
        {
            return true;
        }

        IsConfirmingClose = true;
        return false;
    }

    [RelayCommand]
    private void DiscardAndClose()
    {
        KeepDraftAsRecent();
        IsConfirmingClose = false;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void KeepEditing() => IsConfirmingClose = false;

    [RelayCommand]
    private Task CommitNowAsync() => CommitCoreAsync(PushAfterCommit);

    [RelayCommand]
    private Task CommitAndPushAsync() => CommitCoreAsync(push: true);

    partial void OnSelectedUnstagedChanged(ChangedFileViewModel? value)
    {
        if (_syncingSelection)
        {
            return;
        }

        if (value is not null && SelectedStaged is not null)
        {
            _syncingSelection = true;
            SelectedStaged = null;
            _syncingSelection = false;
        }

        // Started from the UI thread on purpose: ShowAsync updates collections bound to the view and handles its own errors.
        _ = ShowSelectedAsync();
    }

    partial void OnSelectedStagedChanged(ChangedFileViewModel? value)
    {
        if (_syncingSelection)
        {
            return;
        }

        if (value is not null && SelectedUnstaged is not null)
        {
            _syncingSelection = true;
            SelectedUnstaged = null;
            _syncingSelection = false;
        }

        _ = ShowSelectedAsync();
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnShowUntrackedChanged(bool value) => ApplyFilter();

    partial void OnCloseAfterCommitChanged(bool value) => _store.SetCloseAfterCommit(value);

    partial void OnShowTreeChanged(bool value)
    {
        _store.SetShowTree(value);
        OnPropertyChanged(nameof(ShowList));
    }

    partial void OnErrorTextChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private async Task ShowSelectedAsync()
    {
        ChangedFileViewModel? file = SelectedUnstaged ?? SelectedStaged;
        await Resolver.ShowAsync(file, AllChanges);
        await Diff.ShowAsync(file);
    }

    private void ReportError(string message)
    {
        ErrorText = message;
        _main.ErrorMessage = message;
    }

    private async Task AfterIndexChangeAsync()
    {
        await RefreshAsync();
        await _main.RefreshChangesAsync();
    }

    private async Task ChangeIndexAsync(Func<GitModule, string?> change, bool any)
    {
        if (!any)
        {
            return;
        }

        ErrorText = null;
        try
        {
            string? error = await Task.Run(() => change(_module));
            if (error is not null)
            {
                ReportError($"Failed to update the index: {GitErrorMessages.Describe(string.IsNullOrWhiteSpace(error) ? "git reported an error." : error, _module.WorkingDir)}");
            }
        }
        catch (Exception ex)
        {
            ReportError($"Failed to update the index: {ex.Message}");
        }

        await AfterIndexChangeAsync();
    }

    private async Task ResetFilesAsync(IReadOnlyList<ChangedFileViewModel> files)
    {
        ErrorText = null;
        try
        {
            string? error = await Task.Run(() => ResetFiles(files));
            if (error is not null)
            {
                ReportError($"Failed to reset the files: {error}");
            }
        }
        catch (Exception ex)
        {
            ReportError($"Failed to reset the files: {ex.Message}");
        }

        await AfterIndexChangeAsync();
    }

    private string? ResetFiles(IReadOnlyList<ChangedFileViewModel> files)
        => WorkingTreeReset.ResetFiles(_module, files.Select(f => f.Item));

    private async Task ResetEverythingAsync()
    {
        ErrorText = null;
        try
        {
            string? error = await Task.Run(() =>
            {
                ExecutionResult result = _module.GitExecutable.Execute(new GitArgumentBuilder("reset") { "--hard" }, throwOnErrorExit: false);
                return result.ExitedSuccessfully ? null : (string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError).Trim();
            });
            if (error is not null)
            {
                ReportError($"Failed to reset the changes: {error}");
            }
        }
        catch (Exception ex)
        {
            ReportError($"Failed to reset the changes: {ex.Message}");
        }

        await AfterIndexChangeAsync();
    }

    private async Task CommitCoreAsync(bool push)
    {
        ErrorText = null;
        if (string.IsNullOrWhiteSpace(Commit.Message))
        {
            ErrorText = "Please enter commit message";
            return;
        }

        if (Staged.Count == 0 && !(Commit.Amend && Commit.HasHead))
        {
            ErrorText = "There are no staged changes";
            return;
        }

        if (Commit.IsCommitting)
        {
            return;
        }

        if (!await Commit.TryCommitAsync())
        {
            // The commit view-model already reported the failure in the banner; show it inline too.
            ErrorText = Commit.ErrorText;
            return;
        }

        await RefreshAsync();
        if (push)
        {
            IsPushing = true;
            try
            {
                // The push reloads the main window itself when it is done.
                await _main.PushCurrentBranchAsync();
            }
            catch (Exception ex)
            {
                ReportError($"Push failed: {ex.Message}");
            }
            finally
            {
                IsPushing = false;
            }
        }
        else
        {
            await _main.ReloadAfterCommitAsync();
        }

        if (CloseAfterCommit && !_disposed)
        {
            CloseRequested?.Invoke();
        }
    }

    private void ApplyFilter()
    {
        string filter = Filter.Trim();
        Regex? regex = null;
        if (filter.Length > 0)
        {
            try
            {
                regex = new Regex(filter, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
            }
            catch (ArgumentException)
            {
                regex = new Regex(Regex.Escape(filter), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
        }

        bool Matches(ChangedFileViewModel file)
        {
            if (!ShowUntracked && !file.Item.IsTracked && !file.IsStaged)
            {
                return false;
            }

            try
            {
                return regex is null || regex.IsMatch(file.Name);
            }
            catch (RegexMatchTimeoutException)
            {
                return true;
            }
        }

        Unstaged.ReplaceAll(AllChanges.Where(c => !c.IsStaged && Matches(c)));
        Staged.ReplaceAll(AllChanges.Where(c => c.IsStaged && Matches(c)));
        UnstagedTree.ReplaceAll(ChangeTreeNode.Build(Unstaged, _collapsedFolders, "unstaged"));
        StagedTree.ReplaceAll(ChangeTreeNode.Build(Staged, _collapsedFolders, "staged"));
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        int unstaged = AllChanges.Count(c => !c.IsStaged);
        int staged = AllChanges.Count(c => c.IsStaged);
        StatusText = $"{unstaged} {(unstaged == 1 ? "file" : "files")} unstaged, {staged} staged";
        OnPropertyChanged(nameof(UnstagedHeader));
        OnPropertyChanged(nameof(StagedHeader));
        OnPropertyChanged(nameof(HasNoUnstaged));
        OnPropertyChanged(nameof(HasNoStaged));
        OnPropertyChanged(nameof(HasConflicts));
    }

    private void LoadRecent()
    {
        RecentMessages.ReplaceAll(_store.RecentMessages);
        OnPropertyChanged(nameof(HasRecentMessages));
    }

    private void BuildTemplates()
    {
        List<CommitTemplate> templates = [];
        try
        {
            ExecutionResult result = _module.GitExecutable.Execute(new GitArgumentBuilder("config") { "--path", "--get", "commit.template" }, throwOnErrorExit: false);
            string path = result.ExitedSuccessfully ? result.StandardOutput.Trim() : string.Empty;
            if (path.Length > 0)
            {
                string full = Path.IsPathRooted(path) ? path : Path.Combine(_module.WorkingDir, path);
                if (File.Exists(full))
                {
                    templates.Add(new CommitTemplate($"Template: {Path.GetFileName(full)}", string.Join('\n', File.ReadAllLines(full).Where(l => !l.StartsWith('#'))).TrimStart('\n')));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable template is simply not offered.
        }

        foreach (string kind in new[] { "feat", "fix", "docs", "refactor", "test", "chore" })
        {
            templates.Add(new CommitTemplate($"{kind}: …", $"{kind}: "));
        }

        Templates.ReplaceAll(templates);
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.HasOpenedRepository) && !_main.HasOpenedRepository)
        {
            CloseRequested?.Invoke();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.RepositoryPath) && !IsInside(_main.RepositoryPath, _workingDir))
        {
            CloseRequested?.Invoke();
        }
    }

    private static bool IsInside(string path, string workingDir)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        static string Normalize(string value) => Path.GetFullPath(value).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return Normalize(path).StartsWith(Normalize(workingDir), comparison);
    }
}

/// <summary>A commit message template offered in the Commit window.</summary>
public sealed record CommitTemplate(string Name, string Text);
