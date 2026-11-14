using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>What the "Checkout branch" dialog does with the local changes.</summary>
public enum BranchChangesMode
{
    /// <summary>Plain checkout: git refuses when the changes would be overwritten.</summary>
    DontChange,
    Merge,
    Stash,
    Reset,
}

/// <summary>What the "Checkout branch" dialog does with a remote branch.</summary>
public enum RemoteBranchAction
{
    CreateLocal,
    ResetLocal,
    Detached,
}

/// <summary>
///  The "Checkout branch" dialog (FormCheckoutBranch): a local or a remote branch (as a new local branch, a reset local
///  branch or a detached HEAD) and what to do with the local changes.
/// </summary>
public sealed partial class CheckoutBranchDialogViewModel : GitDialogViewModelBase
{
    private bool _nameEdited;
    private bool _settingName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalBranch))]
    private bool _isRemoteBranch;

    [ObservableProperty]
    private string _localBranch = string.Empty;

    [ObservableProperty]
    private string _remoteBranch = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreateLocal), nameof(IsResetLocal), nameof(IsDetached), nameof(CanEditNewName))]
    private RemoteBranchAction _remoteAction;

    [ObservableProperty]
    private string _newBranchName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDontChange), nameof(IsMergeChanges), nameof(IsStashChanges), nameof(IsResetChanges), nameof(CanReapplyStash))]
    private BranchChangesMode _changes;

    [ObservableProperty]
    private bool _reapplyStash = true;

    public CheckoutBranchDialogViewModel(Func<GitModule?> getModule, Func<Task> refresh, Action<string> reportStatus)
        : base(getModule, refresh, reportStatus)
    {
    }

    public bool IsLocalBranch
    {
        get => !IsRemoteBranch;
        set => IsRemoteBranch = !value;
    }

    public bool IsCreateLocal
    {
        get => RemoteAction == RemoteBranchAction.CreateLocal;
        set => SetAction(value, RemoteBranchAction.CreateLocal);
    }

    public bool IsResetLocal
    {
        get => RemoteAction == RemoteBranchAction.ResetLocal;
        set => SetAction(value, RemoteBranchAction.ResetLocal);
    }

    public bool IsDetached
    {
        get => RemoteAction == RemoteBranchAction.Detached;
        set => SetAction(value, RemoteBranchAction.Detached);
    }

    public bool CanEditNewName => RemoteAction == RemoteBranchAction.CreateLocal;

    public bool IsDontChange
    {
        get => Changes == BranchChangesMode.DontChange;
        set => SetChanges(value, BranchChangesMode.DontChange);
    }

    public bool IsMergeChanges
    {
        get => Changes == BranchChangesMode.Merge;
        set => SetChanges(value, BranchChangesMode.Merge);
    }

    public bool IsStashChanges
    {
        get => Changes == BranchChangesMode.Stash;
        set => SetChanges(value, BranchChangesMode.Stash);
    }

    public bool IsResetChanges
    {
        get => Changes == BranchChangesMode.Reset;
        set => SetChanges(value, BranchChangesMode.Reset);
    }

    public bool CanReapplyStash => Changes == BranchChangesMode.Stash;

    /// <summary>Opens the dialog; <paramref name="branch"/> (local or remote) is preselected.</summary>
    public async Task OpenAsync(string? branch = null)
    {
        IsRemoteBranch = false;
        LocalBranch = string.Empty;
        RemoteBranch = string.Empty;
        RemoteAction = RemoteBranchAction.CreateLocal;
        Changes = BranchChangesMode.DontChange;
        ReapplyStash = true;
        _nameEdited = false;
        NewBranchName = string.Empty;
        BeginOpen();
        await LoadRefsAsync();

        if (branch is not null && RemoteBranches.Contains(branch))
        {
            IsRemoteBranch = true;
            RemoteBranch = branch;
        }
        else if (branch is not null)
        {
            LocalBranch = branch;
        }
    }

    /// <summary>Gets the checkout arguments for the current options.</summary>
    public string[] BuildArguments(string branch)
    {
        List<string> args = ["checkout"];
        if (Changes == BranchChangesMode.Merge)
        {
            args.Add("--merge");
        }
        else if (Changes == BranchChangesMode.Reset)
        {
            args.Add("--force");
        }

        if (!IsRemoteBranch)
        {
            args.Add(branch);
        }
        else if (RemoteAction == RemoteBranchAction.Detached)
        {
            args.Add("--detach");
            args.Add(branch);
        }
        else
        {
            args.Add(RemoteAction == RemoteBranchAction.ResetLocal ? "-B" : "-b");
            args.Add(RemoteAction == RemoteBranchAction.ResetLocal ? DefaultLocalName(branch) : NewBranchName.Trim());
            args.Add("--track");
            args.Add(branch);
        }

        return [.. args];
    }

    /// <summary>Gets the local name for a remote branch: <c>origin/feature/x</c> becomes <c>feature/x</c>.</summary>
    public static string DefaultLocalName(string remoteBranch)
    {
        int slash = remoteBranch.IndexOf('/', StringComparison.Ordinal);
        return slash >= 0 ? remoteBranch[(slash + 1)..] : remoteBranch;
    }

    partial void OnRemoteBranchChanged(string value)
    {
        if (!_nameEdited)
        {
            _settingName = true;
            NewBranchName = DefaultLocalName(value.Trim());
            _settingName = false;
        }
    }

    partial void OnNewBranchNameChanged(string value)
    {
        if (!_settingName)
        {
            _nameEdited = true;
        }
    }

    [RelayCommand]
    private Task CheckoutAsync()
        => GuardedAsync(async module =>
        {
            string branch = (IsRemoteBranch ? RemoteBranch : LocalBranch).Trim();
            if (await ResolveAsync(module, branch, IsRemoteBranch ? "the remote branch" : "the branch to check out") is null)
            {
                return;
            }

            if (IsRemoteBranch && RemoteAction == RemoteBranchAction.CreateLocal)
            {
                string name = NewBranchName.Trim();
                if (RefNameValidator.PreCheck(name) is { } problem)
                {
                    Error = problem;
                    return;
                }

                if (LocalBranches.Contains(name))
                {
                    Error = $"A local branch named '{name}' already exists. Pick another name or choose \"Reset local branch\".";
                    return;
                }
            }

            bool stashed = false;
            if (Changes == BranchChangesMode.Stash)
            {
                GitRunResult status = await ExecAsync(module, "status", "--porcelain");
                if (status.Succeeded && status.Output.Trim().Length > 0)
                {
                    GitRunResult stash = await ExecAsync(module, "stash", "push", "--include-untracked", "-m", "Stashed by checkout of " + branch);
                    if (!stash.Succeeded)
                    {
                        Error = "Could not stash the local changes: " + stash.Message;
                        return;
                    }

                    stashed = true;
                }
            }

            GitRunResult checkout = await ExecAsync(module, BuildArguments(branch));
            if (!checkout.Succeeded && stashed)
            {
                // Put the changes back: the checkout did not happen.
                await ExecAsync(module, "stash", "pop");
            }

            string done = IsRemoteBranch && RemoteAction == RemoteBranchAction.Detached ? $"Checked out {branch} (detached HEAD)" : $"Checked out {(IsRemoteBranch ? (RemoteAction == RemoteBranchAction.ResetLocal ? DefaultLocalName(branch) : NewBranchName.Trim()) : branch)}";
            if (!await CompleteAsync(module, checkout, done, "Checkout") || !stashed || !ReapplyStash)
            {
                return;
            }

            GitRunResult pop = await ExecAsync(module, "stash", "pop");
            ReportStatus(pop.Succeeded ? $"{done}; the stashed changes were applied again." : $"{done}, but the stash could not be applied again: {pop.Message}");
            await RefreshAsync();
        });

    private void SetAction(bool selected, RemoteBranchAction action)
    {
        if (selected)
        {
            RemoteAction = action;
        }
    }

    private void SetChanges(bool selected, BranchChangesMode mode)
    {
        if (selected)
        {
            Changes = mode;
        }
    }
}
