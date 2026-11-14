using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.History;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

// Navigate menu: Go to commit…, backward/forward through the selection history (kept out of the main file).
public sealed partial class MainWindowViewModel
{
    private readonly NavigationHistory _navigation = new();
    private bool _isNavigating;
    private string? _navigationRepository;
    private int _historyLimitOverride;

    /// <summary>The overlay that hosts the go to commit, bisect and lost objects dialogs.</summary>
    public ToolDialogsViewModel Dialogs { get; } = new();

    /// <summary>The history limit in force: the setting, or more after "Go to commit" had to load deeper history.</summary>
    private int EffectiveHistoryLimit => Math.Max(BehaviorSettings.Current.HistoryLimit, _historyLimitOverride);

    private bool CanNavigateBack() => _navigation.CanGoBack;

    private bool CanNavigateForward() => _navigation.CanGoForward;

    private void InitializeNavigation()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectedCommit))
            {
                RecordSelection();
            }
        };
        InitializeBisect();
    }

    private void RecordSelection()
    {
        string? repository = _module?.WorkingDir;
        if (!string.Equals(repository, _navigationRepository, StringComparison.Ordinal))
        {
            _navigationRepository = repository;
            _navigation.Clear();
            _historyLimitOverride = 0;
        }

        if (!_isNavigating && SelectedCommit is { } selected && repository is not null)
        {
            _navigation.Record(selected.ObjectId);
        }

        NavigateBackCommand.NotifyCanExecuteChanged();
        NavigateForwardCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ShowGoToCommit()
    {
        if (!HasOpenedRepository)
        {
            return;
        }

        GoToCommitViewModel dialog = new(() => _module, id => GoToCommitAsync(id), Dialogs.Close);
        Dialogs.Show(dialog);
        _ = dialog.LoadRefsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanNavigateBack))]
    private Task NavigateBackAsync() => NavigateAsync(_navigation.Back());

    [RelayCommand(CanExecute = nameof(CanNavigateForward))]
    private Task NavigateForwardAsync() => NavigateAsync(_navigation.Forward());

    private async Task NavigateAsync(ObjectId? id)
    {
        if (id is { } target)
        {
            string? problem = await GoToCommitAsync(target, recordHistory: false);
            if (problem is not null)
            {
                Status = problem;
            }
        }

        NavigateBackCommand.NotifyCanExecuteChanged();
        NavigateForwardCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    ///  Selects the commit in the grid. A commit that is not in the list is brought in: first by showing all branches without a text
    ///  filter, then by loading deeper history.
    /// </summary>
    /// <returns>An explanation when the commit cannot be shown, otherwise <see langword="null"/>.</returns>
    public async Task<string?> GoToCommitAsync(ObjectId id, bool recordHistory = true)
    {
        if (_module is not { } module)
        {
            return "No repository is open.";
        }

        if (!Commits.Any(c => c.ObjectId == id))
        {
            string? problem = await LoadHistoryUntilAsync(module, id);
            if (problem is not null)
            {
                return problem;
            }
        }

        CommitItemViewModel? item = Commits.FirstOrDefault(c => c.ObjectId == id);
        if (item is null)
        {
            return $"{id.ToShortString()} is not in the history list.";
        }

        bool previous = _isNavigating;
        _isNavigating = !recordHistory;
        try
        {
            SelectedCommit = item;
        }
        finally
        {
            _isNavigating = previous;
        }

        return null;
    }

    private async Task<string?> LoadHistoryUntilAsync(GitModule module, ObjectId id)
    {
        (bool reachable, int total) = await Task.Run(() =>
        {
            string hash = id.ToString();
            bool contained = module.GitExecutable.Execute($"for-each-ref --count=1 --contains {hash} --format=%(refname)", throwOnErrorExit: false) is { ExitedSuccessfully: true, StandardOutput: { } refs }
                && refs.Trim().Length > 0;
            contained |= module.GitExecutable.Execute($"merge-base --is-ancestor {hash} HEAD", throwOnErrorExit: false).ExitedSuccessfully;
            if (!contained)
            {
                return (false, 0);
            }

            ExecutionResult count = module.GitExecutable.Execute("rev-list --count --all HEAD", throwOnErrorExit: false);
            return (true, int.TryParse(count.StandardOutput.Trim(), out int n) ? n : 0);
        });
        if (!reachable)
        {
            return $"{id.ToShortString()} is not reachable from any branch, tag or HEAD, so it has no place in the history list (try Recover lost objects).";
        }

        RevisionFilter filter = CurrentFilter;
        if (filter.IsFiltering || filter.Branches != BranchFilterMode.AllBranches)
        {
            _suppressFilterReload = true;
            SelectedBranchChoice = AllBranchesChoice;
            FilterText = string.Empty;
            ShowMergeCommits = true;
            _suppressFilterReload = false;
            Status = "Showing all branches and every commit to reach the revision…";
            await ReloadHistoryAsync();
            if (Commits.Any(c => c.ObjectId == id))
            {
                return null;
            }
        }

        if (total > Commits.Count && EffectiveHistoryLimit < BehaviorSettings.MaxHistoryLimit)
        {
            _historyLimitOverride = Math.Min(Math.Max(total, EffectiveHistoryLimit), BehaviorSettings.MaxHistoryLimit);
            Status = $"Loading the full history ({_historyLimitOverride} commits) to reach the revision…";
            await ReloadHistoryAsync();
        }

        return Commits.Any(c => c.ObjectId == id) ? null : $"{id.ToShortString()} could not be found in the history list.";
    }
}
