using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitCommands.UserRepositoryHistory;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitExtensions.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    // Enough for browsing; the list is filled incrementally while git is still producing output.
    private const string RevisionFilter = "--max-count=2000 HEAD";

    private readonly GitEnvironment _git;
    private readonly IFolderPicker _folderPicker;
    private CancellationTokenSource? _loadCancellation;
    private GitModule? _module;

    [ObservableProperty]
    private string _repositoryPath = string.Empty;

    [ObservableProperty]
    private string _status = "Enter or browse to a Git repository.";

    [ObservableProperty]
    private string? _branch;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private FileDiffViewModel? _diff;

    [ObservableProperty]
    private ChangedFileViewModel? _selectedChangedFile;

    [ObservableProperty]
    private CommitItemViewModel? _selectedCommit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStartView))]
    private bool _hasOpenedRepository;

    public MainWindowViewModel(GitEnvironment git, IFolderPicker folderPicker)
    {
        _git = git;
        _folderPicker = folderPicker;
        Recent = new RecentRepositoriesViewModel(RepositoryHistoryManager.Locals, OpenAsync);
        git.Messages.MessageReported += message => ErrorMessage = message;
        Refs = new RefsTreeViewModel(message => Status = message, SelectCommit, () => OpenAsync(RepositoryPath));
    }

    public RecentRepositoriesViewModel Recent { get; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    ///  The "Recent repositories" start view replaces the empty lists until a repository has been opened.
    /// </summary>
    public bool ShowStartView => !HasOpenedRepository;

    [RelayCommand]
    private void DismissError() => ErrorMessage = null;

    public RefsTreeViewModel Refs { get; }

    /// <summary>
    ///  Selects the commit in the history list if it is loaded.
    /// </summary>
    public void SelectCommit(ObjectId id)
        => SelectedCommit = Commits.FirstOrDefault(commit => commit.ObjectId == id) ?? SelectedCommit;

    public ObservableCollection<CommitItemViewModel> Commits { get; } = [];

    public CommitDetailsViewModel CommitDetails { get; } = new();

    public ObservableCollection<ChangedFileViewModel> ChangedFiles { get; } = [];

    /// <summary>
    ///  Opens the repository containing <paramref name="path"/> and loads its branch, history and changes.
    /// </summary>
    public async Task OpenAsync(string path)
    {
        if (_loadCancellation is not null)
        {
            await _loadCancellation.CancelAsync();
        }

        _loadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _loadCancellation.Token;

        RepositoryPath = path;
        _module = null;
        SelectedCommit = null;
        Commits.Clear();
        ChangedFiles.Clear();
        Diff = null;
        Branch = null;
        Refs.Clear();
        IsBusy = true;
        Status = $"Opening {path}…";

        try
        {
            GitModule? module = await Task.Run(() => _git.OpenRepository(path), cancellationToken);
            if (module is null)
            {
                Status = $"{path} is not inside a Git repository.";
                return;
            }

            RepositoryPath = module.WorkingDir;
            _module = module;
            Diff = new FileDiffViewModel(module, RefreshChangesAsync);
            HasOpenedRepository = true;
            _ = RecordRecentAsync(module.WorkingDir);

            // The git calls block, so keep them off the UI thread.
            (string branch, IReadOnlyList<ChangedFileViewModel> changes, Dictionary<ObjectId, string> refs) = await Task.Run(
                () => (
                    module.GetSelectedBranch(),
                    module.GetAllChangedFiles().Select(item => new ChangedFileViewModel(item)).ToList(),
                    GetRefsByCommit(module)),
                cancellationToken);

            Branch = branch;
            await Refs.LoadAsync(module);
            foreach (ChangedFileViewModel change in changes)
            {
                ChangedFiles.Add(change);
            }

            Status = "Loading history…";
            await Task.Run(() => LoadHistory(module, refs, cancellationToken), cancellationToken);

            // Let the batches still queued for the UI thread land before reporting the final count.
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

            Status = $"{Commits.Count} commits shown · {ChangedFiles.Count} changed files";
        }
        catch (OperationCanceledException)
        {
            // A newer open request replaced this one.
        }
        catch (Exception ex)
        {
            Status = $"Failed to open {path}: {ex.Message}";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsBusy = false;
            }
        }
    }

    /// <summary>
    ///  Reloads the list of changed files (e.g. after staging) and keeps the selection when possible.
    /// </summary>
    public async Task RefreshChangesAsync()
    {
        GitModule? module = _module;
        if (module is null)
        {
            return;
        }

        ChangedFileViewModel? previous = SelectedChangedFile;
        List<ChangedFileViewModel> changes = await Task.Run(() => module.GetAllChangedFiles().Select(item => new ChangedFileViewModel(item)).ToList());

        ChangedFiles.Clear();
        foreach (ChangedFileViewModel change in changes)
        {
            ChangedFiles.Add(change);
        }

        // Prefer the same entry; a file that just moved between index and work tree keeps its name.
        SelectedChangedFile = previous is null
            ? null
            : changes.FirstOrDefault(c => c.Name == previous.Name && c.IsStaged == previous.IsStaged)
                ?? changes.FirstOrDefault(c => c.Name == previous.Name);
        if (SelectedChangedFile is null && Diff is { } diff)
        {
            await diff.ShowAsync(null);
        }
    }

    partial void OnSelectedChangedFileChanged(ChangedFileViewModel? value)
    {
        // Started from the UI thread on purpose (not ThreadHelper.FileAndForget, which hops to a background thread):
        // ShowAsync updates collections bound to the view. It does its own error handling and cancellation.
        if (Diff is { } diff)
        {
            _ = diff.ShowAsync(value);
        }
    }

    // The details load asynchronously and cancel each other; failures are reported by the details view-model itself.
    partial void OnSelectedCommitChanged(CommitItemViewModel? value)
        => _ = value is not null && _module is not null
            ? CommitDetails.LoadAsync(_module, value.ObjectId, value.Refs)
            : CommitDetails.ClearAsync();

    private async Task RecordRecentAsync(string workingDir)
    {
        try
        {
            await Task.Run(() => RepositoryHistoryManager.Locals.AddAsMostRecentAsync(workingDir));
            await Recent.LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not update the recent repositories: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task OpenCurrentAsync()
        => string.IsNullOrWhiteSpace(RepositoryPath) ? Task.CompletedTask : OpenAsync(RepositoryPath.Trim());

    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? folder = await _folderPicker.PickFolderAsync("Open Git repository");
        if (folder is not null)
        {
            await OpenAsync(folder);
        }
    }

    // git log does not report which branches and tags point at a commit, so look them up once and join by commit id.
    private static Dictionary<ObjectId, string> GetRefsByCommit(GitModule module)
        => module.GetRefs(RefsFilter.Heads | RefsFilter.Tags | RefsFilter.Remotes)
            .GroupBy(gitRef => gitRef.ObjectId)
            .ToDictionary(group => group.Key, group => string.Join("  ", group.Select(gitRef => gitRef.LocalName)));

    private void LoadHistory(GitModule module, Dictionary<ObjectId, string> refs, CancellationToken cancellationToken)
    {
        RevisionReader reader = new(module);
        BatchObserver observer = new(batch => Dispatcher.UIThread.Post(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            foreach (GitRevision revision in batch)
            {
                Commits.Add(new CommitItemViewModel(revision, refs.GetValueOrDefault(revision.ObjectId, string.Empty)));
            }

            Status = $"Loading history… {Commits.Count} commits";
        }));

        reader.GetLog(observer, RevisionFilter, pathFilter: string.Empty, hasNotes: false, autostashLabel: "autostash", cancellationToken);
    }

    private sealed class BatchObserver(Action<IReadOnlyList<GitRevision>> onNext) : IObserver<IReadOnlyList<GitRevision>>
    {
        public void OnNext(IReadOnlyList<GitRevision> value) => onNext(value);

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}
