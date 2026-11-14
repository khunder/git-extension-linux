using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

public enum CommitResetMode
{
    Soft,
    Mixed,
    Hard,
}

public enum CommitPromptKind
{
    None,
    CreateBranch,
    CreateTag,
    ConfirmHardReset,
    Revert,
}

/// <summary>
///  The commit context menu: git operations on a commit of the history list.
///  Git runs on a background thread; failures are reported through <c>reportError</c> (the banner).
///  Operations that need a name or a confirmation open an inline prompt instead of running right away.
/// </summary>
public sealed partial class CommitActionsViewModel : ObservableObject
{
    private readonly Func<GitModule?> _getModule;
    private readonly Func<Task> _refresh;
    private readonly Action<string> _reportError;
    private readonly Action<string> _reportStatus;
    private CommitItemViewModel? _promptCommit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPromptVisible), nameof(IsOpen), nameof(NeedsInput), nameof(PromptTitle), nameof(ConfirmText), nameof(DialogTitle), nameof(ShowResetModes), nameof(ShowBranchOptions), nameof(ShowTagOptions))]
    private CommitPromptKind _promptKind;

    [ObservableProperty]
    private CommitResetMode _promptResetMode = CommitResetMode.Hard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDialogError))]
    private string? _dialogError;

    private bool _confirming;
    private string? _pendingPushTag;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProgressNote))]
    private string _progressNote = string.Empty;

    [ObservableProperty]
    private string _inputText = string.Empty;

    public CommitActionsViewModel(Func<GitModule?> getModule, Func<Task> refresh, Action<string> reportError, Action<string>? reportStatus = null, IClipboardService? clipboard = null)
    {
        _getModule = getModule;
        _refresh = refresh;
        _reportError = reportError;
        _reportStatus = reportStatus ?? (static _ => { });
        Clipboard = clipboard;
    }

    /// <summary>The view attaches the window clipboard once it is in the visual tree.</summary>
    public IClipboardService? Clipboard { get; set; }

    /// <summary>The options of the Create branch dialog (checkout, local changes, orphan).</summary>
    public CreateBranchOptions BranchOptions { get; } = new();

    /// <summary>The options of the Create tag dialog (message, signing, push).</summary>
    public CreateTagOptions TagOptions { get; } = new();

    /// <summary>Runs a network git command (push) with output log and credential prompts; set by the main view-model.</summary>
    public Func<string, IReadOnlyList<string>, Task>? RunRemote { get; set; }

    /// <summary>The outcome of the last command run through <see cref="RunRemote"/>.</summary>
    public Func<(bool Succeeded, string Summary)>? RemoteResult { get; set; }

    public bool HasProgressNote => ProgressNote.Length > 0;

    public bool ShowBranchOptions => PromptKind == CommitPromptKind.CreateBranch;

    public bool ShowTagOptions => PromptKind == CommitPromptKind.CreateTag;

    public bool IsPromptVisible => PromptKind != CommitPromptKind.None;

    /// <summary>Whether the prompt dialog is open; closing the dialog window sets it to false (cancels the prompt).</summary>
    public bool IsOpen
    {
        get => IsPromptVisible;
        set
        {
            if (!value)
            {
                CancelPrompt();
            }
        }
    }

    public bool HasDialogError => !string.IsNullOrEmpty(DialogError);

    public bool ShowResetModes => PromptKind == CommitPromptKind.ConfirmHardReset;

    public string DialogTitle => PromptKind switch
    {
        CommitPromptKind.CreateBranch => "Create branch",
        CommitPromptKind.CreateTag => "Create tag",
        CommitPromptKind.ConfirmHardReset => "Reset current branch",
        CommitPromptKind.Revert => "Revert commit",
        _ => string.Empty,
    };

    public bool IsSoftResetMode
    {
        get => PromptResetMode == CommitResetMode.Soft;
        set => SelectResetMode(value, CommitResetMode.Soft);
    }

    public bool IsMixedResetMode
    {
        get => PromptResetMode == CommitResetMode.Mixed;
        set => SelectResetMode(value, CommitResetMode.Mixed);
    }

    public bool IsHardResetMode
    {
        get => PromptResetMode == CommitResetMode.Hard;
        set => SelectResetMode(value, CommitResetMode.Hard);
    }

    public bool NeedsInput => PromptKind is CommitPromptKind.CreateBranch or CommitPromptKind.CreateTag;

    public string PromptTitle => PromptKind switch
    {
        CommitPromptKind.CreateBranch => $"New branch at {_promptCommit?.ShortHash}:",
        CommitPromptKind.CreateTag => $"New tag at {_promptCommit?.ShortHash}:",
        CommitPromptKind.ConfirmHardReset => $"Reset the current branch to {_promptCommit?.ShortHash} ({_promptCommit?.Subject}).",
        CommitPromptKind.Revert => $"Revert commit {_promptCommit?.ShortHash} ({_promptCommit?.Subject}) by creating a new commit that undoes it?",
        _ => string.Empty,
    };

    public string ConfirmText => PromptKind switch
    {
        CommitPromptKind.CreateBranch => "Create branch",
        CommitPromptKind.CreateTag => "Create tag",
        CommitPromptKind.ConfirmHardReset => PromptResetMode switch { CommitResetMode.Soft => "Reset --soft", CommitResetMode.Mixed => "Reset --mixed", _ => "Reset --hard" },
        CommitPromptKind.Revert => "Revert",
        _ => "OK",
    };

    // ---- prompts ----

    [RelayCommand]
    private void RequestCreateBranch(CommitItemViewModel? commit) => OpenPrompt(CommitPromptKind.CreateBranch, commit);

    [RelayCommand]
    private void RequestCreateTag(CommitItemViewModel? commit) => OpenPrompt(CommitPromptKind.CreateTag, commit);

    [RelayCommand]
    private void RequestHardReset(CommitItemViewModel? commit) => OpenPrompt(CommitPromptKind.ConfirmHardReset, commit);

    [RelayCommand]
    private void RequestRevert(CommitItemViewModel? commit) => OpenPrompt(CommitPromptKind.Revert, commit);

    [RelayCommand]
    private void CancelPrompt()
    {
        if (_confirming)
        {
            // Keep the dialog open while git runs: a failure is shown in it, success closes it afterwards.
            return;
        }

        PromptKind = CommitPromptKind.None;
        _promptCommit = null;
        DialogError = null;
    }

    [RelayCommand]
    public async Task ConfirmPromptAsync()
    {
        CommitItemViewModel? commit = _promptCommit;
        CommitPromptKind kind = PromptKind;
        string name = InputText.Trim();
        if (commit is null || kind == CommitPromptKind.None)
        {
            return;
        }

        if (NeedsInput && name.Length == 0)
        {
            return;
        }

        if (_confirming)
        {
            return;
        }

        _confirming = true;
        DialogError = null;
        bool succeeded;
        try
        {
            succeeded = kind switch
            {
                CommitPromptKind.CreateBranch => await CreateBranchWithOptionsAsync(commit, name),
                CommitPromptKind.CreateTag => await CreateTagWithOptionsAsync(commit, name),
                CommitPromptKind.Revert => await RevertAsync(commit),
                _ => await ResetAsync(commit, PromptResetMode, confirmed: true),
            };
        }
        finally
        {
            _confirming = false;
        }

        // A failure stays in the dialog (shown inline); success, or an operation left in progress on conflicts, closes it.
        if (succeeded || !HasDialogError)
        {
            CancelPrompt();
        }
    }

    private void SelectResetMode(bool selected, CommitResetMode mode)
    {
        if (selected)
        {
            PromptResetMode = mode;
        }
    }

    partial void OnPromptResetModeChanged(CommitResetMode value)
    {
        OnPropertyChanged(nameof(IsSoftResetMode));
        OnPropertyChanged(nameof(IsMixedResetMode));
        OnPropertyChanged(nameof(IsHardResetMode));
        OnPropertyChanged(nameof(ConfirmText));
    }

    private void Report(string message)
    {
        if (_confirming)
        {
            DialogError = message;
        }
        else
        {
            _reportError(message);
        }
    }

    private void OpenPrompt(CommitPromptKind kind, CommitItemViewModel? commit)
    {
        if (commit is null)
        {
            return;
        }

        _promptCommit = commit;
        InputText = string.Empty;
        DialogError = null;
        PromptResetMode = CommitResetMode.Hard;
        ProgressNote = string.Empty;
        _pendingPushTag = null;
        BranchOptions.Reset();
        TagOptions.Reset(_getModule() is { } module ? RemoteNames(module) : []);
        PromptKind = kind;
        OnPropertyChanged(nameof(PromptTitle));
    }

    // ---- clipboard ----

    [RelayCommand]
    private Task CopyHashAsync(CommitItemViewModel? commit) => commit is null || Clipboard is null ? Task.CompletedTask : Clipboard.SetTextAsync(commit.ObjectId.ToString());

    [RelayCommand]
    private Task CopySubjectAsync(CommitItemViewModel? commit) => commit is null || Clipboard is null ? Task.CompletedTask : Clipboard.SetTextAsync(commit.Subject);

    // ---- git operations ----

    public Task<bool> CreateBranchAsync(CommitItemViewModel commit, string name)
        => IsValidName(name) ? RunAsync($"Created branch {name}", new GitArgumentBuilder("branch") { name.Quote(), commit.ObjectId.ToString() })
            : InvalidAsync(name);

    // The dialog's create branch: checkout after create, the local changes choice and orphan; failures stay in the dialog.
    private async Task<bool> CreateBranchWithOptionsAsync(CommitItemViewModel commit, string name)
    {
        GitModule? module = _getModule();
        if (module is null)
        {
            return false;
        }

        if (!IsValidName(name))
        {
            return await InvalidAsync(name);
        }

        CreateBranchOptions options = BranchOptions;
        ProgressNote = options.CheckoutAfterCreate || options.Orphan ? "Creating the branch and switching to it…" : "Creating the branch…";
        string start = commit.ObjectId.ToString();
        string? failure = await Task.Run(() => options.Create(module, name, start));
        ProgressNote = string.Empty;
        if (failure is not null)
        {
            Report(failure);
            await _refresh();
            return false;
        }

        _reportStatus($"Created branch {name}");
        await _refresh();
        return true;
    }

    // The dialog's create tag: optional message/signature, then optionally the push with its result shown in the dialog.
    private async Task<bool> CreateTagWithOptionsAsync(CommitItemViewModel commit, string name)
    {
        GitModule? module = _getModule();
        if (module is null)
        {
            return false;
        }

        CreateTagOptions options = TagOptions;
        string? remote = options.PushTarget;
        if (_pendingPushTag is null)
        {
            if (!IsValidName(name))
            {
                return await InvalidAsync(name);
            }

            string start = commit.ObjectId.ToString();
            string? failure = await Task.Run(() => options.Create(module, name, start));
            if (failure is not null)
            {
                Report(failure);
                return false;
            }

            _reportStatus($"Created tag {name}");
            await _refresh();
        }
        else
        {
            name = _pendingPushTag;
        }

        if (remote is null)
        {
            return true;
        }

        _pendingPushTag = name;
        ProgressNote = $"Pushing tag '{name}' to {remote}…";
        string[] arguments = ["push", "--progress", remote, $"refs/tags/{name}"];
        bool succeeded;
        string summary;
        if (RunRemote is not null)
        {
            await RunRemote($"Push tag {name}", arguments);
            (succeeded, summary) = RemoteResult?.Invoke() ?? (true, string.Empty);
        }
        else
        {
            ExecutionResult result = await Task.Run(() => module.GitExecutable.Execute(new GitArgumentBuilder("push") { "--progress", remote.Quote(), $"refs/tags/{name}" }, throwOnErrorExit: false));
            (succeeded, summary) = (result.ExitedSuccessfully, (string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError).Trim());
        }

        ProgressNote = string.Empty;
        if (succeeded)
        {
            _pendingPushTag = null;
            _reportStatus($"Pushed tag {name} to {remote}");
            return true;
        }

        Report($"The tag '{name}' was created, but pushing it to {remote} failed: {summary}. Confirm again to retry the push.");
        return false;
    }

    private static IReadOnlyList<string> RemoteNames(GitModule module)
    {
        try
        {
            return module.GetRemoteNames();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public Task<bool> CreateTagAsync(CommitItemViewModel commit, string name)
        => IsValidName(name) ? RunAsync($"Created tag {name}", new GitArgumentBuilder("tag") { name.Quote(), commit.ObjectId.ToString() })
            : InvalidAsync(name);

    [RelayCommand]
    public Task<bool> CheckoutAsync(CommitItemViewModel? commit)
        => commit is null ? Task.FromResult(false) : RunAsync($"Checked out {commit.ShortHash} (detached HEAD)", new GitArgumentBuilder("checkout") { "--detach", commit.ObjectId.ToString() });

    [RelayCommand]
    private Task<bool> ResetSoftAsync(CommitItemViewModel? commit) => commit is null ? Task.FromResult(false) : ResetAsync(commit, CommitResetMode.Soft);

    [RelayCommand]
    private Task<bool> ResetMixedAsync(CommitItemViewModel? commit) => commit is null ? Task.FromResult(false) : ResetAsync(commit, CommitResetMode.Mixed);

    /// <summary>
    ///  Resets the current branch. A hard reset throws away work, so it only runs with <paramref name="confirmed"/>;
    ///  otherwise it opens the confirmation prompt and returns <see langword="false"/> without touching the repository.
    /// </summary>
    public async Task<bool> ResetAsync(CommitItemViewModel commit, CommitResetMode mode, bool confirmed = false)
    {
        if (mode == CommitResetMode.Hard && !confirmed)
        {
            OpenPrompt(CommitPromptKind.ConfirmHardReset, commit);
            return false;
        }

        string flag = mode switch { CommitResetMode.Soft => "--soft", CommitResetMode.Mixed => "--mixed", _ => "--hard" };
        return await RunAsync($"Reset ({flag}) to {commit.ShortHash}", new GitArgumentBuilder("reset") { flag, commit.ObjectId.ToString() });
    }

    [RelayCommand]
    public Task<bool> RevertAsync(CommitItemViewModel? commit)
        => commit is null ? Task.FromResult(false) : RunAsync(
            $"Reverted {commit.ShortHash}",
            new GitArgumentBuilder("revert") { "--no-edit", { IsMerge(commit), "-m 1" }, commit.ObjectId.ToString() },
            keepsStateOnConflict: true);

    [RelayCommand]
    public Task<bool> CherryPickAsync(CommitItemViewModel? commit)
        => commit is null ? Task.FromResult(false) : RunAsync(
            $"Cherry-picked {commit.ShortHash}",
            new GitArgumentBuilder("cherry-pick") { { IsMerge(commit), "-m 1" }, commit.ObjectId.ToString() },
            keepsStateOnConflict: true);

    private static bool IsMerge(CommitItemViewModel commit) => commit.Revision.ParentIds?.Count > 1;

    private static bool IsValidName(string name)
        => !string.IsNullOrWhiteSpace(name) && !name.StartsWith('-') && !name.Any(char.IsWhiteSpace) && !name.Contains('"');

    private Task<bool> InvalidAsync(string name)
    {
        Report($"'{name}' is not a valid name.");
        return Task.FromResult(false);
    }

    /// <summary>
    ///  Runs git on a background thread; on success refreshes the lists. A revert/cherry-pick that stops on conflicts
    ///  (<paramref name="keepsStateOnConflict"/>) is left in progress so the operation bar can continue or abort it.
    /// </summary>
    private async Task<bool> RunAsync(string successMessage, ArgumentString arguments, bool keepsStateOnConflict = false)
    {
        GitModule? module = _getModule();
        if (module is null)
        {
            return false;
        }

        try
        {
            (ExecutionResult result, OperationKind state) = await Task.Run(() =>
            {
                ExecutionResult run = module.GitExecutable.Execute(arguments, throwOnErrorExit: false);
                return (run, keepsStateOnConflict && !run.ExitedSuccessfully ? RepositoryOperationState.Detect(module.WorkingDirGitDir) : OperationKind.None);
            });

            if (!result.ExitedSuccessfully)
            {
                if (state != OperationKind.None)
                {
                    _reportStatus($"{arguments.Arguments?.Split(' ')[0]} stopped on conflicts. Resolve them, then continue or abort in the operation bar.");
                    await _refresh();
                    return false;
                }

                string error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
                Report($"git {arguments.Arguments?.Split(' ')[0]} failed: {error.Trim()}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Report($"Git operation failed: {ex.Message}");
            return false;
        }

        _reportStatus(successMessage);
        await _refresh();
        return true;
    }
}
