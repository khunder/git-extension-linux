using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>The revision operations of the Commands menu that need a small dialog.</summary>
public enum RevisionOperation
{
    None,
    CherryPick,
    CheckoutRevision,
    DeleteBranch,
    Archive,
    Reset,
    Clean,
}

/// <summary>What "Checkout revision" does with local changes.</summary>
public enum LocalChangesMode
{
    /// <summary>Plain checkout: git refuses when the changes would be overwritten.</summary>
    Keep,
    Merge,
    Reset,
}

/// <summary>Which ignored files "Clean working directory" removes.</summary>
public enum CleanIgnoredMode
{
    None,
    Include,
    Only,
}

/// <summary>A branch offered by "Delete branch".</summary>
public sealed partial class BranchChoice : ObservableObject
{
    public BranchChoice(string name, bool isRemote, bool isCurrent)
    {
        Name = name;
        IsRemote = isRemote;
        IsCurrent = isCurrent;
    }

    public string Name { get; }

    public bool IsRemote { get; }

    public bool IsCurrent { get; }

    public bool CanSelect => !IsCurrent;

    public string Display => IsCurrent ? Name + " (current)" : IsRemote ? Name + " (remote)" : Name;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
///  Cherry pick, checkout revision, delete branch, archive, reset and clean working directory.
///  The dialog is a modal window: the user picks the options, then confirms.
///  Git runs on a background thread; failures are reported through <c>reportError</c> (the banner).
/// </summary>
public sealed partial class RevisionOperationsViewModel : ObservableObject
{
    private readonly Func<GitModule?> _getModule;
    private readonly Func<Task> _refresh;
    private readonly Action<string> _reportError;
    private readonly Action<string> _reportStatus;
    private readonly Func<string, IReadOnlyList<string>, Task> _runRemote;
    private readonly Func<string?> _selectedRevision;
    private bool _hardConfirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(DialogTitle), nameof(Title), nameof(ConfirmText), nameof(ShowRevision), nameof(IsCherryPick), nameof(IsCheckout), nameof(IsDeleteBranch), nameof(IsArchive), nameof(IsReset), nameof(IsClean))]
    private RevisionOperation _operation;

    [ObservableProperty]
    private string _revision = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning), nameof(HasWarning), nameof(ConfirmText))]
    private string? _validationError;

    // cherry pick
    [ObservableProperty]
    private bool _autoCommit = true;

    [ObservableProperty]
    private bool _addReference;

    /// <summary>Parent number for <c>-m</c>; 0 means "not a merge".</summary>
    [ObservableProperty]
    private int _mainlineParent;

    // checkout
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning), nameof(HasWarning), nameof(ConfirmText))]
    private LocalChangesMode _localChanges;

    // delete branch
    [ObservableProperty]
    private bool _forceDelete;

    // archive
    [ObservableProperty]
    private string _archiveFormat = "zip";

    [ObservableProperty]
    private string _archiveFile = string.Empty;

    [ObservableProperty]
    private string _archivePath = string.Empty;

    // reset
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Warning), nameof(HasWarning), nameof(ConfirmText))]
    private CommitResetMode _resetMode = CommitResetMode.Mixed;

    // clean
    [ObservableProperty]
    private bool _includeDirectories = true;

    [ObservableProperty]
    private CleanIgnoredMode _ignored;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(Warning), nameof(HasWarning), nameof(ConfirmText), nameof(PreviewSummary))]
    private bool _previewed;

    [ObservableProperty]
    private bool _isRunning;

    public RevisionOperationsViewModel(
        Func<GitModule?> getModule,
        Func<Task> refresh,
        Action<string> reportError,
        Action<string>? reportStatus = null,
        Func<string, IReadOnlyList<string>, Task>? runRemote = null,
        Func<string?>? selectedRevision = null)
    {
        _getModule = getModule;
        _refresh = refresh;
        _reportError = reportError;
        _reportStatus = reportStatus ?? (static _ => { });
        _runRemote = runRemote ?? (static (_, _) => Task.CompletedTask);
        _selectedRevision = selectedRevision ?? (static () => null);
    }

    /// <summary>The view attaches the window's save-file dialog once it is in the visual tree.</summary>
    public IFileSavePicker? SavePicker { get; set; }

    public ObservableCollection<BranchChoice> Branches { get; } = [];

    public ObservableCollection<string> CleanPreview { get; } = [];

    public IReadOnlyList<string> ArchiveFormats { get; } = ["zip", "tar", "tar.gz"];

    public IReadOnlyList<int> MainlineChoices { get; } = [0, 1, 2];

    /// <summary>Whether the operation dialog is open; closing the dialog window sets it to false (cancels the operation).</summary>
    public bool IsOpen
    {
        get => Operation != RevisionOperation.None;
        set
        {
            if (!value && IsOpen)
            {
                Cancel();
            }
        }
    }

    /// <summary>The window title of the dialog, as in the original Git Extensions.</summary>
    public string DialogTitle => Operation switch
    {
        RevisionOperation.CherryPick => "Cherry pick commit",
        RevisionOperation.CheckoutRevision => "Checkout revision",
        RevisionOperation.DeleteBranch => "Delete branches",
        RevisionOperation.Archive => "Archive revision",
        RevisionOperation.Reset => "Reset current branch",
        RevisionOperation.Clean => "Clean working directory",
        _ => string.Empty,
    };

    public bool IsCherryPick => Operation == RevisionOperation.CherryPick;

    public bool IsCheckout => Operation == RevisionOperation.CheckoutRevision;

    public bool IsDeleteBranch => Operation == RevisionOperation.DeleteBranch;

    public bool IsArchive => Operation == RevisionOperation.Archive;

    public bool IsReset => Operation == RevisionOperation.Reset;

    public bool IsClean => Operation == RevisionOperation.Clean;

    public bool ShowRevision => Operation is RevisionOperation.CherryPick or RevisionOperation.CheckoutRevision or RevisionOperation.Archive or RevisionOperation.Reset;

    public bool HasPreview => Previewed;

    public bool IsKeepLocalChanges
    {
        get => LocalChanges == LocalChangesMode.Keep;
        set
        {
            if (value)
            {
                LocalChanges = LocalChangesMode.Keep;
            }
        }
    }

    public bool IsMergeLocalChanges
    {
        get => LocalChanges == LocalChangesMode.Merge;
        set
        {
            if (value)
            {
                LocalChanges = LocalChangesMode.Merge;
            }
        }
    }

    public bool IsResetLocalChanges
    {
        get => LocalChanges == LocalChangesMode.Reset;
        set
        {
            if (value)
            {
                LocalChanges = LocalChangesMode.Reset;
            }
        }
    }

    public bool IsSoftReset
    {
        get => ResetMode == CommitResetMode.Soft;
        set
        {
            if (value)
            {
                ResetMode = CommitResetMode.Soft;
            }
        }
    }

    public bool IsMixedReset
    {
        get => ResetMode == CommitResetMode.Mixed;
        set
        {
            if (value)
            {
                ResetMode = CommitResetMode.Mixed;
            }
        }
    }

    public bool IsHardReset
    {
        get => ResetMode == CommitResetMode.Hard;
        set
        {
            if (value)
            {
                ResetMode = CommitResetMode.Hard;
            }
        }
    }

    public bool IsIgnoredNone
    {
        get => Ignored == CleanIgnoredMode.None;
        set
        {
            if (value)
            {
                Ignored = CleanIgnoredMode.None;
            }
        }
    }

    public bool IsIgnoredInclude
    {
        get => Ignored == CleanIgnoredMode.Include;
        set
        {
            if (value)
            {
                Ignored = CleanIgnoredMode.Include;
            }
        }
    }

    public bool IsIgnoredOnly
    {
        get => Ignored == CleanIgnoredMode.Only;
        set
        {
            if (value)
            {
                Ignored = CleanIgnoredMode.Only;
            }
        }
    }

    public string Title => Operation switch
    {
        RevisionOperation.CherryPick => "Cherry pick",
        RevisionOperation.CheckoutRevision => "Checkout revision (detached HEAD)",
        RevisionOperation.DeleteBranch => "Delete branches",
        RevisionOperation.Archive => "Archive revision",
        RevisionOperation.Reset => "Reset current branch to a revision",
        RevisionOperation.Clean => "Clean working directory",
        _ => string.Empty,
    };

    public string ConfirmText => Operation switch
    {
        RevisionOperation.CherryPick => "Cherry pick",
        RevisionOperation.CheckoutRevision => LocalChanges == LocalChangesMode.Reset ? "Checkout and discard changes" : "Checkout",
        RevisionOperation.DeleteBranch => "Delete selected",
        RevisionOperation.Archive => "Archive…",
        RevisionOperation.Reset => ResetMode == CommitResetMode.Hard ? (_hardConfirmed ? "Yes, reset --hard" : "Reset --hard…") : ResetMode == CommitResetMode.Soft ? "Reset --soft" : "Reset --mixed",
        RevisionOperation.Clean => !Previewed ? "Preview" : CleanPreview.Count > 0 ? $"Delete {CleanPreview.Count} item(s)" : "Nothing to clean",
        _ => "OK",
    };

    public string PreviewSummary => !Previewed ? string.Empty : CleanPreview.Count == 0 ? "Nothing to clean." : $"{CleanPreview.Count} item(s) will be permanently deleted:";

    /// <summary>The inline confirmation text of destructive options.</summary>
    public string? Warning => Operation switch
    {
        RevisionOperation.Reset when ResetMode == CommitResetMode.Hard && _hardConfirmed => "This discards all uncommitted changes and later commits of the current branch. Press the button again to continue.",
        RevisionOperation.Reset when ResetMode == CommitResetMode.Hard => "A hard reset discards all uncommitted changes.",
        RevisionOperation.CheckoutRevision when LocalChanges == LocalChangesMode.Reset => "Local changes will be discarded.",
        RevisionOperation.Clean when Previewed && CleanPreview.Count > 0 => "Deleted files cannot be recovered.",
        _ => null,
    };

    public bool HasWarning => Warning is not null;

    // ---- opening the dialog ----

    [RelayCommand]
    public void OpenCherryPick()
    {
        Open(RevisionOperation.CherryPick);
        Revision = _selectedRevision() ?? string.Empty;
        AutoCommit = true;
        AddReference = false;
        MainlineParent = 0;
    }

    [RelayCommand]
    public void OpenCheckoutRevision()
    {
        Open(RevisionOperation.CheckoutRevision);
        Revision = _selectedRevision() ?? string.Empty;
        LocalChanges = LocalChangesMode.Keep;
    }

    [RelayCommand]
    public async Task OpenDeleteBranchAsync()
    {
        Open(RevisionOperation.DeleteBranch);
        ForceDelete = false;
        Branches.Clear();
        GitModule? module = _getModule();
        if (module is null)
        {
            return;
        }

        List<BranchChoice> found = await Task.Run(() => ListBranches(module));
        if (Operation == RevisionOperation.DeleteBranch)
        {
            foreach (BranchChoice branch in found)
            {
                Branches.Add(branch);
            }
        }
    }

    [RelayCommand]
    public void OpenArchive()
    {
        Open(RevisionOperation.Archive);
        Revision = _selectedRevision() ?? "HEAD";
        ArchiveFile = string.Empty;
        ArchivePath = string.Empty;
    }

    [RelayCommand]
    public void OpenReset()
    {
        Open(RevisionOperation.Reset);
        Revision = _selectedRevision() ?? "HEAD";
        ResetMode = CommitResetMode.Mixed;
    }

    [RelayCommand]
    public void OpenClean()
    {
        Open(RevisionOperation.Clean);
        IncludeDirectories = true;
        Ignored = CleanIgnoredMode.None;
    }

    [RelayCommand]
    public void UseSelectedCommit()
    {
        if (_selectedRevision() is { } revision)
        {
            Revision = revision;
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        Operation = RevisionOperation.None;
        CleanPreview.Clear();
        Branches.Clear();
        Previewed = false;
        _hardConfirmed = false;
        ValidationError = null;
    }

    private void Open(RevisionOperation operation)
    {
        Cancel();
        Operation = operation;
    }

    partial void OnResetModeChanged(CommitResetMode value)
    {
        _hardConfirmed = false;
        OnPropertyChanged(nameof(IsSoftReset));
        OnPropertyChanged(nameof(IsMixedReset));
        OnPropertyChanged(nameof(IsHardReset));
    }

    partial void OnLocalChangesChanged(LocalChangesMode value)
    {
        OnPropertyChanged(nameof(IsKeepLocalChanges));
        OnPropertyChanged(nameof(IsMergeLocalChanges));
        OnPropertyChanged(nameof(IsResetLocalChanges));
    }

    partial void OnIgnoredChanged(CleanIgnoredMode value)
    {
        InvalidatePreview();
        OnPropertyChanged(nameof(IsIgnoredNone));
        OnPropertyChanged(nameof(IsIgnoredInclude));
        OnPropertyChanged(nameof(IsIgnoredOnly));
    }

    partial void OnIncludeDirectoriesChanged(bool value) => InvalidatePreview();

    partial void OnRevisionChanged(string value) => ValidationError = null;

    private void InvalidatePreview()
    {
        if (Previewed)
        {
            Previewed = false;
            CleanPreview.Clear();
        }
    }

    [RelayCommand]
    private async Task BrowseArchiveFileAsync()
    {
        if (SavePicker is null)
        {
            return;
        }

        string suggested = "archive." + ArchiveFormat;
        string? file = await SavePicker.PickSaveFileAsync("Archive revision", suggested, ArchiveFormat);
        if (file is not null)
        {
            ArchiveFile = file;
        }
    }

    // ---- confirm ----

    [RelayCommand]
    public async Task ConfirmAsync()
    {
        if (IsRunning || !IsOpen)
        {
            return;
        }

        IsRunning = true;
        try
        {
            switch (Operation)
            {
                case RevisionOperation.CherryPick:
                    await CherryPickAsync();
                    break;
                case RevisionOperation.CheckoutRevision:
                    await CheckoutRevisionAsync();
                    break;
                case RevisionOperation.DeleteBranch:
                    await DeleteBranchesAsync();
                    break;
                case RevisionOperation.Archive:
                    await ArchiveAsync();
                    break;
                case RevisionOperation.Reset:
                    await ResetAsync();
                    break;
                case RevisionOperation.Clean:
                    await CleanAsync();
                    break;
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task CherryPickAsync()
    {
        if (await ResolveAsync() is not { } sha)
        {
            return;
        }

        int parents = await ParentCountAsync(sha);
        int mainline = MainlineParent;
        if (parents > 1 && mainline == 0)
        {
            ValidationError = $"{Short(sha)} is a merge commit: choose the parent to keep the mainline of.";
            MainlineParent = 1;
            return;
        }

        if (parents <= 1)
        {
            mainline = 0;
        }

        ArgumentString args = new GitArgumentBuilder("cherry-pick")
        {
            { !AutoCommit, "--no-commit" },
            { AddReference, "-x" },
            { mainline > 0, $"-m {mainline}" },
            sha,
        };
        Cancel();
        await RunAsync($"Cherry-picked {Short(sha)}", args, keepsStateOnConflict: true);
    }

    private async Task CheckoutRevisionAsync()
    {
        if (await ResolveAsync() is not { } sha)
        {
            return;
        }

        LocalChangesMode mode = LocalChanges;
        ArgumentString args = new GitArgumentBuilder("checkout")
        {
            { mode == LocalChangesMode.Merge, "--merge" },
            { mode == LocalChangesMode.Reset, "--force" },
            "--detach",
            sha,
        };
        Cancel();
        await RunAsync($"Checked out {Short(sha)} (detached HEAD)", args);
    }

    private async Task DeleteBranchesAsync()
    {
        List<BranchChoice> selected = [.. Branches.Where(b => b.IsSelected && !b.IsCurrent)];
        if (selected.Count == 0)
        {
            ValidationError = "Select at least one branch.";
            return;
        }

        bool force = ForceDelete;
        Cancel();
        List<string> local = [.. selected.Where(b => !b.IsRemote).Select(b => b.Name)];
        if (local.Count > 0)
        {
            ArgumentString args = new GitArgumentBuilder("branch") { force ? "-D" : "-d", { true, "--" }, string.Join(' ', local.Select(n => n.Quote())) };
            await RunAsync($"Deleted {string.Join(", ", local)}", args);
        }

        foreach (BranchChoice remote in selected.Where(b => b.IsRemote))
        {
            int slash = remote.Name.IndexOf('/', StringComparison.Ordinal);
            if (slash <= 0)
            {
                continue;
            }

            await _runRemote($"Delete {remote.Name}", ["push", "--progress", remote.Name[..slash], "--delete", remote.Name[(slash + 1)..]]);
        }
    }

    private async Task ArchiveAsync()
    {
        if (await ResolveAsync() is not { } sha)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ArchiveFile))
        {
            await BrowseArchiveFileAsync();
            if (string.IsNullOrWhiteSpace(ArchiveFile))
            {
                ValidationError = "Choose the file to write.";
                return;
            }
        }

        string file = ArchiveFile.Trim();
        string path = ArchivePath.Trim();
        GitArgumentBuilder args = new("archive") { $"--format={ArchiveFormat}", $"--output={file.Quote()}", sha };
        if (path.Length > 0)
        {
            args.Add("--");
            args.Add(path.Quote());
        }

        Cancel();
        await RunAsync($"Archived {Short(sha)} to {file}", args, refresh: false);
    }

    private async Task ResetAsync()
    {
        if (await ResolveAsync() is not { } sha)
        {
            return;
        }

        if (ResetMode == CommitResetMode.Hard && !_hardConfirmed)
        {
            _hardConfirmed = true;
            OnPropertyChanged(nameof(Warning));
            OnPropertyChanged(nameof(HasWarning));
            OnPropertyChanged(nameof(ConfirmText));
            return;
        }

        string flag = ResetMode switch { CommitResetMode.Soft => "--soft", CommitResetMode.Mixed => "--mixed", _ => "--hard" };
        Cancel();
        await RunAsync($"Reset ({flag}) to {Short(sha)}", new GitArgumentBuilder("reset") { flag, sha });
    }

    private async Task CleanAsync()
    {
        GitModule? module = _getModule();
        if (module is null)
        {
            return;
        }

        if (!Previewed)
        {
            ArgumentString dry = CleanArguments("-n");
            ExecutionResult result = await Task.Run(() => module.GitExecutable.Execute(dry, throwOnErrorExit: false));
            if (!result.ExitedSuccessfully)
            {
                ValidationError = "git clean failed: " + result.StandardError.Trim();
                return;
            }

            CleanPreview.Clear();
            foreach (string line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.StartsWith("Would remove ", StringComparison.Ordinal))
                {
                    CleanPreview.Add(line["Would remove ".Length..]);
                }
            }

            Previewed = true;
            return;
        }

        if (CleanPreview.Count == 0)
        {
            return;
        }

        int count = CleanPreview.Count;
        ArgumentString args = CleanArguments("-f");
        Cancel();
        await RunAsync($"Cleaned {count} item(s)", args);
    }

    private ArgumentString CleanArguments(string mode) => new GitArgumentBuilder("clean")
    {
        mode,
        { IncludeDirectories, "-d" },
        { Ignored == CleanIgnoredMode.Include, "-x" },
        { Ignored == CleanIgnoredMode.Only, "-X" },
    };

    // ---- helpers ----

    private static string Short(string sha) => sha.Length > 8 ? sha[..8] : sha;

    private static List<BranchChoice> ListBranches(GitModule module)
    {
        string current = module.GitExecutable.GetOutput(new GitArgumentBuilder("branch") { "--show-current" }).Trim();
        List<BranchChoice> result = [];
        ExecutionResult local = module.GitExecutable.Execute(new GitArgumentBuilder("for-each-ref") { "--format=%(refname:short)", "refs/heads" }, throwOnErrorExit: false);
        foreach (string name in Lines(local.StandardOutput))
        {
            result.Add(new BranchChoice(name, isRemote: false, isCurrent: name == current));
        }

        ExecutionResult remote = module.GitExecutable.Execute(new GitArgumentBuilder("for-each-ref") { "--format=%(refname:short)", "refs/remotes" }, throwOnErrorExit: false);
        foreach (string name in Lines(remote.StandardOutput).Where(n => n.Contains('/', StringComparison.Ordinal) && !n.EndsWith("/HEAD", StringComparison.Ordinal)))
        {
            result.Add(new BranchChoice(name, isRemote: true, isCurrent: false));
        }

        return result;
    }

    private static IEnumerable<string> Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Resolves <see cref="Revision"/> (any rev-parse expression) to a commit id, or reports the problem inline.</summary>
    private async Task<string?> ResolveAsync()
    {
        string expression = Revision.Trim();
        GitModule? module = _getModule();
        if (module is null)
        {
            return null;
        }

        if (expression.Length == 0 || expression.StartsWith('-'))
        {
            ValidationError = "Enter a revision (commit, branch, tag or any rev-parse expression).";
            return null;
        }

        ExecutionResult result = await Task.Run(() => module.GitExecutable.Execute(new GitArgumentBuilder("rev-parse") { "--verify", "--quiet", (expression + "^{commit}").Quote() }, throwOnErrorExit: false));
        string sha = result.StandardOutput.Trim();
        if (!result.ExitedSuccessfully || sha.Length == 0)
        {
            ValidationError = $"'{expression}' is not a valid revision.";
            return null;
        }

        return sha;
    }

    private async Task<int> ParentCountAsync(string sha)
    {
        GitModule? module = _getModule();
        if (module is null)
        {
            return 0;
        }

        ExecutionResult result = await Task.Run(() => module.GitExecutable.Execute(new GitArgumentBuilder("rev-list") { "--parents", "-n 1", sha }, throwOnErrorExit: false));
        return Math.Max(0, result.StandardOutput.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length - 1);
    }

    private async Task RunAsync(string successMessage, ArgumentString arguments, bool keepsStateOnConflict = false, bool refresh = true)
    {
        GitModule? module = _getModule();
        if (module is null)
        {
            return;
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
                string verb = arguments.Arguments?.Split(' ')[0] ?? "git";
                if (state != OperationKind.None)
                {
                    _reportStatus($"{verb} stopped on conflicts. Resolve them, then continue or abort in the operation bar.");
                    await _refresh();
                    return;
                }

                string error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
                _reportError($"git {verb} failed: {error.Trim()}");
                return;
            }
        }
        catch (Exception ex)
        {
            _reportError($"Git operation failed: {ex.Message}");
            return;
        }

        if (refresh)
        {
            await _refresh();
        }

        _reportStatus(successMessage);
    }
}
