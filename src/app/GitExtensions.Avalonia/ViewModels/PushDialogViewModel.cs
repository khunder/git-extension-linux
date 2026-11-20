using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The "Push" dialog (FormPush): remote or URL, the local branch and the remote branch it goes to, push all branches or tags,
///  force (with lease, or plain with a warning) and "set upstream". It runs through <see cref="RemoteOperationsViewModel"/>
///  (live output, credential prompts).
/// </summary>
public sealed partial class PushDialogViewModel : ObservableObject
{
    private readonly RemoteOperationsViewModel _remote;
    private readonly Func<GitModule?> _moduleProvider;

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
    private string? _localBranch;

    [ObservableProperty]
    private string _remoteBranch = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseBranch))]
    private bool _pushAllBranches;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseBranch))]
    private bool _pushAllTags;

    [ObservableProperty]
    private bool _forceWithLease;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowForceWarning))]
    private bool _forcePush;

    [ObservableProperty]
    private bool _setUpstream;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _error = string.Empty;

    public PushDialogViewModel(RemoteOperationsViewModel remote, Func<GitModule?> moduleProvider)
    {
        _remote = remote;
        _moduleProvider = moduleProvider;
        remote.PropertyChanged += OnRemotePropertyChanged;
    }

    public ObservableCollection<string> LocalBranches { get; } = [];

    public ObservableCollection<string> RemoteBranches { get; } = [];

    public ObservableCollection<string> Remotes { get; } = [];

    public string OutputText => _remote.OutputText;

    public bool IsRunning => _remote.IsRunning;

    public bool HasError => Error.Length > 0;

    public bool UsesRemote => !UseUrl;

    public bool CanChooseBranch => !PushAllBranches && !PushAllTags;

    public bool ShowForceWarning => ForcePush;

    /// <summary>Opens the dialog on the current branch and its remote.</summary>
    public async Task OpenAsync()
    {
        Error = string.Empty;
        await _remote.ReloadStateAsync();
        GitModule? module = _moduleProvider();
        if (module is null)
        {
            return;
        }

        Remotes.Clear();
        foreach (string name in _remote.Remotes)
        {
            Remotes.Add(name);
        }

        string? branch = _remote.CurrentBranch;
        (IReadOnlyList<string> locals, string? upstreamRemote) = await Task.Run(() => (
            GitRemoteHelpers.LocalBranches(module),
            branch is null ? null : GitRemoteHelpers.UpstreamRemote(module, branch)));
        LocalBranches.Clear();
        foreach (string local in locals)
        {
            LocalBranches.Add(local);
        }

        UseUrl = false;
        PushAllBranches = false;
        PushAllTags = false;
        ForcePush = false;
        ForceWithLease = false;
        SelectedRemote = upstreamRemote is not null && Remotes.Contains(upstreamRemote) ? upstreamRemote : _remote.SelectedRemote;
        LocalBranch = branch;
        await UpdateRemoteBranchAsync(module);
        SetUpstream = !_remote.HasUpstream;
        IsOpen = true;
    }

    /// <summary>Builds the git command line for the current choices; null (with <see cref="Error"/> set) when it cannot run.</summary>
    public List<string>? BuildArguments()
    {
        string? target = UseUrl ? Url.Trim() : SelectedRemote;
        if (string.IsNullOrEmpty(target))
        {
            Error = UseUrl ? "Please enter the URL of the repository to push to." : "Please select a remote repository.";
            return null;
        }

        if (CanChooseBranch && string.IsNullOrEmpty(LocalBranch))
        {
            Error = "Please select the branch to push.";
            return null;
        }

        List<string> args = ["push", "--progress"];
        if (ForcePush)
        {
            args.Add("--force");
        }
        else if (ForceWithLease)
        {
            args.Add("--force-with-lease");
        }

        if (SetUpstream && CanChooseBranch)
        {
            args.Add("--set-upstream");
        }

        if (PushAllBranches)
        {
            args.Add("--all");
        }
        else if (PushAllTags)
        {
            args.Add("--tags");
        }

        args.Add(target);
        if (CanChooseBranch)
        {
            string remoteBranch = string.IsNullOrWhiteSpace(RemoteBranch) ? LocalBranch! : RemoteBranch.Trim();
            args.Add($"{LocalBranch}:{remoteBranch}");
        }

        Error = string.Empty;
        return args;
    }

    [RelayCommand(CanExecute = nameof(CanPush))]
    private async Task PushAsync()
    {
        List<string>? args = BuildArguments();
        if (args is not null)
        {
            await _remote.RunGitAsync("Push", args);

            // Like the original: the dialog goes away once the push went through; on failure it stays to show git's output.
            if (_remote.LastRunSucceeded)
            {
                IsOpen = false;
            }
        }
    }

    private bool CanPush() => !IsRunning && _remote.HasRepository;

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void Cancel() => _remote.CancelCommand.Execute(null);

    partial void OnForcePushChanged(bool value)
    {
        if (value)
        {
            ForceWithLease = false;
        }
    }

    partial void OnForceWithLeaseChanged(bool value)
    {
        if (value)
        {
            ForcePush = false;
        }
    }

    partial void OnLocalBranchChanged(string? value)
    {
        if (_moduleProvider() is { } module)
        {
            _ = UpdateRemoteBranchAsync(module);
        }
    }

    partial void OnSelectedRemoteChanged(string? value)
    {
        if (_moduleProvider() is { } module)
        {
            _ = UpdateRemoteBranchAsync(module);
        }
    }

    // The remote branch defaults to the branch the local one tracks on that remote, else to the same name.
    private async Task UpdateRemoteBranchAsync(GitModule module)
    {
        string? local = LocalBranch;
        string? remote = UseUrl ? null : SelectedRemote;
        (string? upstream, IReadOnlyList<string> known) = await Task.Run(() => (
            local is null ? null : GitRemoteHelpers.UpstreamBranch(module, local, remote),
            remote is null ? [] : GitRemoteHelpers.RemoteBranches(module, remote)));
        if (local != LocalBranch || remote != (UseUrl ? null : SelectedRemote))
        {
            return;
        }

        if (!RemoteBranches.SequenceEqual(known))
        {
            RemoteBranches.Clear();
            foreach (string branch in known)
            {
                RemoteBranches.Add(branch);
            }
        }

        RemoteBranch = upstream ?? local ?? string.Empty;
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
                PushCommand.NotifyCanExecuteChanged();
                break;
            case nameof(RemoteOperationsViewModel.HasRepository):
                PushCommand.NotifyCanExecuteChanged();
                break;
        }
    }
}
