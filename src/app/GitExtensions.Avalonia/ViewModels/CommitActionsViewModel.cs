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
    [NotifyPropertyChangedFor(nameof(IsPromptVisible), nameof(NeedsInput), nameof(PromptTitle), nameof(ConfirmText))]
    private CommitPromptKind _promptKind;

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

    public bool IsPromptVisible => PromptKind != CommitPromptKind.None;

    public bool NeedsInput => PromptKind is CommitPromptKind.CreateBranch or CommitPromptKind.CreateTag;

    public string PromptTitle => PromptKind switch
    {
        CommitPromptKind.CreateBranch => $"New branch at {_promptCommit?.ShortHash}:",
        CommitPromptKind.CreateTag => $"New tag at {_promptCommit?.ShortHash}:",
        CommitPromptKind.ConfirmHardReset => $"Hard reset the current branch to {_promptCommit?.ShortHash}? Uncommitted changes and later commits on this branch will be lost.",
        _ => string.Empty,
    };

    public string ConfirmText => PromptKind switch
    {
        CommitPromptKind.CreateBranch => "Create branch",
        CommitPromptKind.CreateTag => "Create tag",
        CommitPromptKind.ConfirmHardReset => "Reset --hard",
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
    private void CancelPrompt()
    {
        PromptKind = CommitPromptKind.None;
        _promptCommit = null;
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

        if (kind != CommitPromptKind.ConfirmHardReset && name.Length == 0)
        {
            return;
        }

        CancelPrompt();
        _ = kind switch
        {
            CommitPromptKind.CreateBranch => await CreateBranchAsync(commit, name),
            CommitPromptKind.CreateTag => await CreateTagAsync(commit, name),
            _ => await ResetAsync(commit, CommitResetMode.Hard, confirmed: true),
        };
    }

    private void OpenPrompt(CommitPromptKind kind, CommitItemViewModel? commit)
    {
        if (commit is null)
        {
            return;
        }

        _promptCommit = commit;
        InputText = string.Empty;
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
            abortCommand: "revert");

    [RelayCommand]
    public Task<bool> CherryPickAsync(CommitItemViewModel? commit)
        => commit is null ? Task.FromResult(false) : RunAsync(
            $"Cherry-picked {commit.ShortHash}",
            new GitArgumentBuilder("cherry-pick") { { IsMerge(commit), "-m 1" }, commit.ObjectId.ToString() },
            abortCommand: "cherry-pick");

    private static bool IsMerge(CommitItemViewModel commit) => commit.Revision.ParentIds?.Count > 1;

    private static bool IsValidName(string name)
        => !string.IsNullOrWhiteSpace(name) && !name.StartsWith('-') && !name.Any(char.IsWhiteSpace) && !name.Contains('"');

    private Task<bool> InvalidAsync(string name)
    {
        _reportError($"'{name}' is not a valid name.");
        return Task.FromResult(false);
    }

    /// <summary>
    ///  Runs git on a background thread; on success refreshes the lists. When <paramref name="abortCommand"/> is set, a failed
    ///  revert/cherry-pick (typically a conflict) is aborted so the repository is not left half-way, since the UI cannot resolve conflicts yet.
    /// </summary>
    private async Task<bool> RunAsync(string successMessage, ArgumentString arguments, string? abortCommand = null)
    {
        GitModule? module = _getModule();
        if (module is null)
        {
            return false;
        }

        try
        {
            (ExecutionResult result, bool aborted) = await Task.Run(() =>
            {
                ExecutionResult run = module.GitExecutable.Execute(arguments, throwOnErrorExit: false);
                bool abort = false;
                if (!run.ExitedSuccessfully && abortCommand is not null)
                {
                    abort = module.GitExecutable.Execute($"{abortCommand} --abort", throwOnErrorExit: false).ExitedSuccessfully;
                }

                return (run, abort);
            });

            if (!result.ExitedSuccessfully)
            {
                string error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
                _reportError($"git {arguments.Arguments?.Split(' ')[0]} failed: {error.Trim()}{(aborted ? $" ({abortCommand} was aborted; nothing changed.)" : string.Empty)}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _reportError($"Git operation failed: {ex.Message}");
            return false;
        }

        _reportStatus(successMessage);
        await _refresh();
        return true;
    }
}
