using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;

namespace GitExtensions.Desktop.ViewModels;

public enum PullMode
{
    Merge,
    Rebase,
    Fetch,
}

public enum PullTagMode
{
    Default,
    NoTags,
    AllTags,
}

/// <summary>
///  The "Pull" dialog (FormPull): source (remote or URL), the remote branch, merge / rebase / fetch only, tag, prune and
///  auto stash options. It runs through <see cref="RemoteOperationsViewModel"/> (live output, credential prompts); when the
///  pull stops with conflicts the dialog closes so the operation bar of the main window takes over.
/// </summary>
public sealed partial class PullDialogViewModel : ObservableObject
{
    private readonly RemoteOperationsViewModel _remote;
    private readonly Func<GitModule?> _moduleProvider;
    private readonly Func<Task> _afterFailureAsync;
    private readonly Func<bool> _isOperationInProgress;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string? _selectedRemote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesRemote))]
    private bool _useUrl;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _remoteBranch = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMerge), nameof(IsRebase), nameof(IsFetch), nameof(CanPrune))]
    private PullMode _mode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDefaultTags), nameof(IsNoTags), nameof(IsAllTags))]
    private PullTagMode _tagMode;

    [ObservableProperty]
    private bool _fetchAll;

    [ObservableProperty]
    private bool _prune;

    [ObservableProperty]
    private bool _pruneTags;

    [ObservableProperty]
    private bool _autoStash;

    [ObservableProperty]
    private bool _unshallow;

    [ObservableProperty]
    private bool _isShallow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _error = string.Empty;

    public PullDialogViewModel(RemoteOperationsViewModel remote, Func<GitModule?> moduleProvider, Func<Task> afterFailureAsync, Func<bool> isOperationInProgress)
    {
        _remote = remote;
        _moduleProvider = moduleProvider;
        _afterFailureAsync = afterFailureAsync;
        _isOperationInProgress = isOperationInProgress;
        remote.PropertyChanged += OnRemotePropertyChanged;
    }

    public ObservableCollection<string> RemoteBranches { get; } = [];

    public ObservableCollection<string> Remotes { get; } = [];

    public string LocalBranch => _remote.CurrentBranch ?? "(detached HEAD)";

    public string OutputText => _remote.OutputText;

    public bool IsRunning => _remote.IsRunning;

    public bool HasError => Error.Length > 0;

    public bool UsesRemote => !UseUrl;

    public bool CanPrune => IsFetch;

    public bool IsMerge
    {
        get => Mode == PullMode.Merge;
        set => SetMode(value, PullMode.Merge);
    }

    public bool IsRebase
    {
        get => Mode == PullMode.Rebase;
        set => SetMode(value, PullMode.Rebase);
    }

    public bool IsFetch
    {
        get => Mode == PullMode.Fetch;
        set => SetMode(value, PullMode.Fetch);
    }

    public bool IsDefaultTags
    {
        get => TagMode == PullTagMode.Default;
        set => SetTags(value, PullTagMode.Default);
    }

    public bool IsNoTags
    {
        get => TagMode == PullTagMode.NoTags;
        set => SetTags(value, PullTagMode.NoTags);
    }

    public bool IsAllTags
    {
        get => TagMode == PullTagMode.AllTags;
        set => SetTags(value, PullTagMode.AllTags);
    }

    /// <summary>Opens the dialog: re-reads the remotes and defaults to the current branch's remote, branch and the default pull action.</summary>
    public async Task OpenAsync(PullMode mode = PullMode.Merge)
    {
        Mode = mode;
        Error = string.Empty;
        await _remote.ReloadStateAsync();
        GitModule? module = _moduleProvider();
        if (module is null)
        {
            return;
        }

        // The dialog keeps its own list: the shared one is rebuilt after every operation, which would reset the selection.
        Remotes.Clear();
        foreach (string name in _remote.Remotes)
        {
            Remotes.Add(name);
        }

        string? branch = _remote.CurrentBranch;
        (string? upstreamRemote, string? upstreamBranch, bool shallow) = await Task.Run(() => (
            branch is null ? null : GitRemoteHelpers.UpstreamRemote(module, branch),
            branch is null ? null : GitRemoteHelpers.UpstreamBranch(module, branch, remote: null),
            GitRemoteHelpers.IsShallow(module)));
        IsShallow = shallow;
        Unshallow = false;
        UseUrl = false;
        SelectedRemote = upstreamRemote is not null && Remotes.Contains(upstreamRemote) ? upstreamRemote : _remote.SelectedRemote;
        await LoadRemoteBranchesAsync(module);
        RemoteBranch = upstreamBranch ?? branch ?? string.Empty;
        OnPropertyChanged(nameof(LocalBranch));
        IsOpen = true;
    }

    /// <summary>Builds the git command line for the current choices; null (with <see cref="Error"/> set) when it cannot run.</summary>
    public List<string>? BuildArguments()
    {
        string? source = UseUrl ? Url.Trim() : SelectedRemote;
        bool all = FetchAll && IsFetch && !UseUrl;
        if (!all && string.IsNullOrEmpty(source))
        {
            Error = UseUrl ? "Please enter the URL of the repository to pull from." : "Please select a remote repository.";
            return null;
        }

        if (!IsFetch && _remote.CurrentBranch is null)
        {
            Error = "HEAD is detached; there is no branch to pull into. Use \"Do not merge, only fetch\" instead.";
            return null;
        }

        List<string> args = IsFetch ? ["fetch", "--progress"] : ["pull", "--progress", "--no-edit", IsRebase ? "--rebase" : "--no-rebase"];
        if (!IsFetch && AutoStash)
        {
            args.Add("--autostash");
        }

        if (IsFetch && (Prune || PruneTags))
        {
            args.Add("--prune");
            if (PruneTags)
            {
                args.Add("--prune-tags");
            }
        }

        if (Unshallow && IsShallow)
        {
            args.Add("--unshallow");
        }

        switch (TagMode)
        {
            case PullTagMode.NoTags:
                args.Add("--no-tags");
                break;
            case PullTagMode.AllTags:
                args.Add("--tags");
                break;
        }

        if (all)
        {
            args.Add("--all");
        }
        else
        {
            args.Add(source!);
            if (!string.IsNullOrWhiteSpace(RemoteBranch))
            {
                args.Add(RemoteBranch.Trim());
            }
        }

        Error = string.Empty;
        return args;
    }

    [RelayCommand(CanExecute = nameof(CanPull))]
    private async Task PullAsync()
    {
        List<string>? args = BuildArguments();
        if (args is null)
        {
            return;
        }

        string title = IsFetch ? "Fetch" : IsRebase ? "Pull (rebase)" : "Pull (merge)";
        await _remote.RunGitAsync(title, args);
        if (!_remote.LastRunSucceeded)
        {
            // A pull that stops with conflicts leaves a merge or rebase in progress: hand over to the operation bar.
            await _afterFailureAsync();
            if (_isOperationInProgress())
            {
                IsOpen = false;
            }
        }
    }

    private bool CanPull() => !IsRunning && _remote.HasRepository;

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void Cancel() => _remote.CancelCommand.Execute(null);

    partial void OnSelectedRemoteChanged(string? value)
    {
        if (_moduleProvider() is { } module && !UseUrl)
        {
            _ = LoadRemoteBranchesAsync(module);
        }
    }

    private async Task LoadRemoteBranchesAsync(GitModule module)
    {
        string? remote = SelectedRemote;
        IReadOnlyList<string> branches = remote is null ? [] : await Task.Run(() => GitRemoteHelpers.RemoteBranches(module, remote));
        if (remote != SelectedRemote)
        {
            return;
        }

        if (RemoteBranches.SequenceEqual(branches))
        {
            return;
        }

        RemoteBranches.Clear();
        foreach (string branch in branches)
        {
            RemoteBranches.Add(branch);
        }
    }

    private void SetMode(bool selected, PullMode mode)
    {
        if (selected)
        {
            Mode = mode;
        }
    }

    private void SetTags(bool selected, PullTagMode mode)
    {
        if (selected)
        {
            TagMode = mode;
        }
    }

    private void OnRemotePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(RemoteOperationsViewModel.OutputText):
                OnPropertyChanged(nameof(OutputText));
                break;
            case nameof(RemoteOperationsViewModel.IsRunning):
                OnPropertyChanged(nameof(IsRunning));
                PullCommand.NotifyCanExecuteChanged();
                break;
            case nameof(RemoteOperationsViewModel.HasRepository):
                PullCommand.NotifyCanExecuteChanged();
                break;
        }
    }
}
