using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility;

namespace GitExtensions.Desktop.ViewModels;

public enum MergeMode
{
    /// <summary>Fast-forward when possible, otherwise a merge commit.</summary>
    Default,
    NoFastForward,
    FastForwardOnly,
}

/// <summary>
///  Starts merges and rebases and drives an operation that is in progress (merge, rebase, cherry-pick, revert):
///  Continue, Skip (rebase) and Abort (after a confirmation). Git never opens an editor.
/// </summary>
public sealed partial class OperationBarViewModel : ObservableObject
{
    private readonly Func<GitModule?> _getModule;
    private readonly Func<Task> _refresh;
    private readonly Action<string> _reportError;
    private readonly Action<string> _reportStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive), nameof(Title), nameof(CanSkip), nameof(Detail), nameof(CanContinue), nameof(CanAbort))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand), nameof(SkipCommand), nameof(RequestAbortCommand))]
    private OperationKind _kind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail), nameof(CanContinue))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private int _unmergedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanContinue), nameof(CanAbort), nameof(CanSkip))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand), nameof(SkipCommand), nameof(RequestAbortCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isConfirmingAbort;

    public OperationBarViewModel(Func<GitModule?> getModule, Func<Task> refresh, Action<string> reportError, Action<string>? reportStatus = null)
    {
        _getModule = getModule;
        _refresh = refresh;
        _reportError = reportError;
        _reportStatus = reportStatus ?? (static _ => { });
    }

    public bool IsActive => Kind != OperationKind.None;

    public string Name => Kind switch
    {
        OperationKind.Merge => "Merge",
        OperationKind.Rebase => "Rebase",
        OperationKind.CherryPick => "Cherry-pick",
        OperationKind.Revert => "Revert",
        _ => string.Empty,
    };

    public string Title => Kind == OperationKind.None ? string.Empty : $"{Name} in progress";

    public string Detail => UnmergedCount > 0
        ? $"{UnmergedCount} conflicting {(UnmergedCount == 1 ? "file" : "files")} to resolve"
        : "All conflicts resolved. Continue to finish.";

    public string AbortConfirmText => $"Abort the {RepositoryOperationState.CommandName(Kind)}? Conflict resolutions made so far are discarded.";

    public bool CanContinue => IsActive && UnmergedCount == 0 && !IsRunning;

    public bool CanSkip => Kind == OperationKind.Rebase && !IsRunning;

    public bool CanAbort => IsActive && !IsRunning;

    /// <summary>Re-reads the operation state and the unmerged files (git runs off the UI thread).</summary>
    public async Task RefreshAsync()
    {
        GitModule? module = _getModule();
        if (module is null)
        {
            Kind = OperationKind.None;
            UnmergedCount = 0;
            return;
        }

        try
        {
            (OperationKind kind, int count) = await Task.Run(() => ReadState(module));
            if (ReferenceEquals(module, _getModule()))
            {
                Kind = kind;
                UnmergedCount = count;
                if (kind == OperationKind.None)
                {
                    IsConfirmingAbort = false;
                }
            }
        }
        catch (Exception ex)
        {
            _reportError($"Could not read the repository state: {ex.Message}");
        }
    }

    private static (OperationKind Kind, int Count) ReadState(GitModule module)
    {
        OperationKind kind = RepositoryOperationState.Detect(module.WorkingDirGitDir);
        if (kind == OperationKind.None)
        {
            return (kind, 0);
        }

        ExecutionResult result = module.GitExecutable.Execute("diff --name-only --diff-filter=U -z", throwOnErrorExit: false);
        int count = result.ExitedSuccessfully ? result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries).Length : 0;
        return (kind, count);
    }

    // ---- start ----

    public Task<bool> MergeAsync(string branch, MergeMode mode = MergeMode.Default)
    {
        string[] args = mode switch
        {
            MergeMode.NoFastForward => ["merge", "--no-edit", "--no-ff", branch],
            MergeMode.FastForwardOnly => ["merge", "--no-edit", "--ff-only", branch],
            _ => ["merge", "--no-edit", branch],
        };
        return RunAsync($"Merged {branch}", "Merge", args);
    }

    public Task<bool> RebaseAsync(string onto) => RunAsync($"Rebased onto {onto}", "Rebase", ["rebase", onto]);

    // ---- drive ----

    [RelayCommand(CanExecute = nameof(CanContinue))]
    public Task<bool> ContinueAsync()
        => Kind == OperationKind.None
            ? Task.FromResult(false)
            : RunAsync($"{Name} finished", Name, [RepositoryOperationState.CommandName(Kind), "--continue"]);

    [RelayCommand(CanExecute = nameof(CanSkip))]
    public Task<bool> SkipAsync() => RunAsync("Skipped the current commit", "Rebase", ["rebase", "--skip"]);

    [RelayCommand(CanExecute = nameof(CanAbort))]
    private void RequestAbort()
    {
        OnPropertyChanged(nameof(AbortConfirmText));
        IsConfirmingAbort = true;
    }

    [RelayCommand]
    private void CancelAbort() => IsConfirmingAbort = false;

    [RelayCommand]
    private Task<bool> ConfirmAbortAsync() => AbortAsync(confirmed: true);

    /// <summary>
    ///  Aborts the operation and restores the state before it started. That throws away resolutions, so it only runs
    ///  with <paramref name="confirmed"/>; otherwise it opens the confirmation and returns <see langword="false"/>.
    /// </summary>
    public async Task<bool> AbortAsync(bool confirmed = false)
    {
        if (Kind == OperationKind.None)
        {
            return false;
        }

        if (!confirmed)
        {
            RequestAbort();
            return false;
        }

        IsConfirmingAbort = false;
        return await RunAsync("Operation aborted", "Abort", [RepositoryOperationState.CommandName(Kind), "--abort"]);
    }

    private async Task<bool> RunAsync(string successMessage, string label, string[] arguments)
    {
        GitModule? module = _getModule();
        if (module is null || IsRunning)
        {
            return false;
        }

        IsRunning = true;
        bool succeeded;
        try
        {
            GitRunResult result = await GitOperationRunner.RunAsync(module.GitExecutable, arguments);
            succeeded = result.Succeeded;
            await RefreshAsync();
            if (succeeded)
            {
                _reportStatus(successMessage);
            }
            else if (IsActive && UnmergedCount > 0)
            {
                // Not an error: git stopped to let the user resolve conflicts.
                _reportStatus($"{label} stopped on conflicts. Resolve the {UnmergedCount} conflicting {(UnmergedCount == 1 ? "file" : "files")}, then continue.");
            }
            else
            {
                _reportError($"git {arguments[0]} failed: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _reportError($"Git operation failed: {ex.Message}");
            succeeded = false;
        }
        finally
        {
            IsRunning = false;
        }

        await _refresh();
        await RefreshAsync();
        return succeeded;
    }
}
