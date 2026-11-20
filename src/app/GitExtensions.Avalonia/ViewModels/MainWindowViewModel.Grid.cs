using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.History;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>The branches and tags that point at one commit: the joined text (for the details) and the structured badges.</summary>
public readonly record struct CommitRefs(string Joined, IReadOnlyList<RefBadge> Badges);

// The revision grid: filter bar state, the "Uncommitted changes" row and the hook that reloads the history for a filter.
public sealed partial class MainWindowViewModel
{
    public const string AllBranchesChoice = "All branches";
    public const string CurrentBranchChoice = "Current branch";

    /// <summary>The branch filter a repository opens with: all branches, as in the original.</summary>
    public const string DefaultBranchChoice = AllBranchesChoice;

    private Dictionary<ObjectId, CommitRefs> _refsByCommit = [];
    private string? _currentUser;
    private string? _gridRepository;
    private CancellationTokenSource? _reloadCancellation;
    private CancellationTokenSource? _debounceCancellation;
    private int _historyGeneration;
    private bool _suppressFilterReload;

    [ObservableProperty]
    private bool _isWorkingTreeSelected;

    [ObservableProperty]
    private string? _selectedBranchChoice = DefaultBranchChoice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFilterText))]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private TextFilterMode _filterMode = TextFilterMode.CommitMessage;

    [ObservableProperty]
    private bool _showRemoteBranches = true;

    [ObservableProperty]
    private bool _showTags = true;

    [ObservableProperty]
    private bool _showMergeCommits = true;

    public RevisionGridColumns Columns { get; } = new();

    /// <summary>How long typing in the filter box waits before the history is reloaded.</summary>
    public TimeSpan FilterDebounce { get; set; } = TimeSpan.FromMilliseconds(300);

    public RangeObservableCollection<string> BranchChoices { get; } = [AllBranchesChoice, CurrentBranchChoice];

    public bool HasFilterText => FilterText.Length > 0;

    /// <summary>The "Uncommitted changes (N)" row is shown above the history while the working tree has changes and no filter narrows the list.</summary>
    public bool ShowUncommittedRow => ChangedFiles.Count > 0 && !CurrentFilter.IsFiltering;

    public string UncommittedChangesText => $"Uncommitted changes ({ChangedFiles.Count})";

    /// <summary>The filter the filter bar currently describes.</summary>
    public RevisionFilter CurrentFilter => new()
    {
        Branches = SelectedBranchChoice switch
        {
            null or AllBranchesChoice => BranchFilterMode.AllBranches,
            CurrentBranchChoice => BranchFilterMode.CurrentBranch,
            _ => BranchFilterMode.SpecificBranch,
        },
        Branch = SelectedBranchChoice is null or AllBranchesChoice or CurrentBranchChoice ? null : SelectedBranchChoice,
        ShowRemoteBranches = ShowRemoteBranches,
        ShowTags = ShowTags,
        ShowMergeCommits = ShowMergeCommits,
        Text = FilterText,
        TextMode = FilterMode,
    };

    private void InitializeGrid()
    {
        ChangedFiles.CollectionChanged += OnChangedFilesChanged;
        Columns.Apply(GridColumnStore.Current.Load());
    }

    private void OnChangedFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ShowUncommittedRow));
        OnPropertyChanged(nameof(UncommittedChangesText));
        if (ChangedFiles.Count == 0 && IsWorkingTreeSelected)
        {
            IsWorkingTreeSelected = false;
        }
    }

    partial void OnIsWorkingTreeSelectedChanged(bool value)
    {
        if (value)
        {
            SelectedCommit = null;
        }
    }

    partial void OnFilterTextChanged(string value) => ScheduleReload(FilterDebounce);

    partial void OnFilterModeChanged(TextFilterMode value)
    {
        if (HasFilterText)
        {
            ScheduleReload(TimeSpan.Zero);
        }
    }

    partial void OnSelectedBranchChoiceChanged(string? value)
    {
        if (value is not null)
        {
            ScheduleReload(TimeSpan.Zero);
        }
    }

    partial void OnShowRemoteBranchesChanged(bool value) => ScheduleReload(TimeSpan.Zero);

    partial void OnShowTagsChanged(bool value) => ScheduleReload(TimeSpan.Zero);

    partial void OnShowMergeCommitsChanged(bool value) => ScheduleReload(TimeSpan.Zero);

    [RelayCommand]
    private void ClearFilter()
    {
        _suppressFilterReload = true;
        FilterText = string.Empty;
        _suppressFilterReload = false;
        ScheduleReload(TimeSpan.Zero);
    }

    /// <summary>Called when a repository was opened: remembers the refs, fills the branch combo, resets a filter that belongs to another repository.</summary>
    private void PrepareGrid(string workingDir, Dictionary<ObjectId, CommitRefs> refs, string? user)
    {
        bool sameRepository = string.Equals(_gridRepository, workingDir, StringComparison.Ordinal);
        _gridRepository = workingDir;
        _refsByCommit = refs;
        _currentUser = user;

        List<string> locals = refs.Values
            .SelectMany(r => r.Badges)
            .Where(badge => badge.Kind == RefBadgeKind.LocalBranch)
            .Select(badge => badge.Name)
            .Distinct()
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? previous = SelectedBranchChoice;
        _suppressFilterReload = true;
        BranchChoices.ReplaceAll([AllBranchesChoice, CurrentBranchChoice, .. locals]);
        SelectedBranchChoice = sameRepository && previous is not null && BranchChoices.Contains(previous) ? previous : DefaultBranchChoice;
        if (!sameRepository)
        {
            FilterText = string.Empty;
        }

        _suppressFilterReload = false;
        OnPropertyChanged(nameof(ShowUncommittedRow));
    }

    private void CancelHistoryReload()
    {
        _debounceCancellation?.Cancel();
        _reloadCancellation?.Cancel();
    }

    private void ScheduleReload(TimeSpan delay)
    {
        OnPropertyChanged(nameof(ShowUncommittedRow));
        if (_suppressFilterReload || _module is null)
        {
            return;
        }

        _debounceCancellation?.Cancel();
        CancellationTokenSource debounce = new();
        _debounceCancellation = debounce;
        _ = ReloadAfterDelayAsync(delay, debounce.Token);
    }

    private async Task ReloadAfterDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            await ReloadHistoryAsync();
        }
        catch (OperationCanceledException)
        {
            // Typing went on: a newer reload replaces this one.
        }
    }

    /// <summary>Reloads the history list for the current filter, cancelling a load that is still running.</summary>
    public async Task ReloadHistoryAsync()
    {
        GitModule? module = _module;
        if (module is null)
        {
            return;
        }

        if (_debounceCancellation is not null)
        {
            await _debounceCancellation.CancelAsync();
        }

        if (_reloadCancellation is not null)
        {
            await _reloadCancellation.CancelAsync();
        }

        CancellationTokenSource reload = new();
        _reloadCancellation = reload;
        CancellationToken cancellationToken = reload.Token;

        ObjectId? selected = SelectedCommit?.ObjectId;
        RevisionFilter filter = CurrentFilter;
        Dictionary<ObjectId, CommitRefs> refs = _refsByCommit;
        string? user = _currentUser;
        int generation = Interlocked.Increment(ref _historyGeneration);

        Commits.Clear();
        GraphLaneCount = 1;
        IsBusy = true;
        Status = "Loading history…";
        try
        {
            await Task.Run(() => LoadHistory(module, refs, filter, user, generation, cancellationToken), cancellationToken);
            await _dispatcher.InvokeAsync(static () => { }, Avalonia.Threading.DispatcherPriority.Background);
            if (!cancellationToken.IsCancellationRequested)
            {
                if (selected is { } id)
                {
                    SelectCommit(id);
                }

                Status = filter.IsFiltering ? $"{Commits.Count} matching commits" : $"{Commits.Count} commits shown · {ChangedFiles.Count} changed files";
            }
        }
        catch (OperationCanceledException)
        {
            // A newer reload or open replaced this one.
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Status = $"Could not filter the history: {ex.Message}";
            }
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsBusy = false;
            }
        }
    }

    // git log does not report which branches and tags point at a commit, so look them up once and join by commit id.
    private static Dictionary<ObjectId, CommitRefs> GetRefsByCommit(GitModule module)
    {
        string current = module.GetSelectedBranch(emptyIfDetached: true);
        return module.GetRefs(RefsFilter.Heads | RefsFilter.Tags | RefsFilter.Remotes)
            .GroupBy(gitRef => gitRef.ObjectId)
            .ToDictionary(
                group => group.Key,
                group => new CommitRefs(
                    string.Join("  ", group.Select(gitRef => gitRef.LocalName)),
                    group
                        .Where(gitRef => !(gitRef.IsRemote && gitRef.Name.EndsWith("/HEAD", StringComparison.Ordinal)))
                        .Select(gitRef => new RefBadge(
                            gitRef.IsTag ? RefBadgeKind.Tag : gitRef.IsRemote ? RefBadgeKind.RemoteBranch : RefBadgeKind.LocalBranch,
                            gitRef.IsTag ? gitRef.LocalName : gitRef.IsRemote ? gitRef.Name : gitRef.LocalName,
                            gitRef.IsHead && gitRef.LocalName == current)
                        {
                            IsCurrentUpstream = gitRef.IsRemote && current.Length > 0 && gitRef.Name.IndexOf('/') is var slash and > 0 && string.Equals(gitRef.Name[(slash + 1)..], current, StringComparison.Ordinal),
                        })
                        .ToList()));
    }

    // Like the original: the label of the ref, what kind of ref it is, then the message of the commit it points at.
    private static string BadgeToolTip(RefBadge badge, GitRevision revision)
    {
        string kind = badge.Kind switch
        {
            RefBadgeKind.Tag => "is a tag",
            RefBadgeKind.RemoteBranch => "is a remote branch",
            RefBadgeKind.Stash => "is a stash",
            _ => badge.IsCurrent ? "is the checked out branch" : "is a local branch",
        };
        string message = string.IsNullOrWhiteSpace(revision.Body) ? revision.Subject : revision.Body.Trim();
        return $"[{badge.Name}]{Environment.NewLine}{kind}{Environment.NewLine}{Environment.NewLine}{message}";
    }

    private static CommitItemViewModel CreateItem(GitRevision revision, Dictionary<ObjectId, CommitRefs> refs, RevisionFilter filter, string? user, bool alternate, CommitActionsViewModel actions)
    {
        CommitRefs found = refs.GetValueOrDefault(revision.ObjectId);
        IReadOnlyList<RefBadge> badges = found.Badges is null
            ? []
            : [.. RefBadgeBuilder.Arrange(found.Badges.Where(b => (b.Kind != RefBadgeKind.RemoteBranch || filter.ShowRemoteBranches) && (b.Kind != RefBadgeKind.Tag || filter.ShowTags))).Select(b => b.IsOverflow || b.ToolTip is not null ? b : b with { ToolTip = BadgeToolTip(b, revision) })];
        return new CommitItemViewModel(revision, found.Joined ?? string.Empty, badges)
        {
            Actions = actions,
            IsMine = !string.IsNullOrEmpty(user) && string.Equals(revision.Author, user, StringComparison.Ordinal),
            IsAlternate = alternate,
        };
    }
}
