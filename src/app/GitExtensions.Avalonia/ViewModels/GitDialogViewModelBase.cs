using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  Shared part of the merge, rebase and checkout dialogs: the branch lists, running git with the output shown inline,
///  and the hand-off to the operation bar when git stops on conflicts.
/// </summary>
public abstract partial class GitDialogViewModelBase : ObservableObject
{
    private readonly Func<GitModule?> _getModule;
    private readonly Func<Task> _refresh;
    private readonly Action<string> _reportStatus;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutput))]
    private string _output = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    private bool _isRunning;

    [ObservableProperty]
    private string _currentBranch = string.Empty;

    protected GitDialogViewModelBase(Func<GitModule?> getModule, Func<Task> refresh, Action<string> reportStatus)
    {
        _getModule = getModule;
        _refresh = refresh;
        _reportStatus = reportStatus;
    }

    /// <summary>Gets the local branches (the current one included).</summary>
    public ObservableCollection<string> LocalBranches { get; } = [];

    /// <summary>Gets the remote-tracking branches (<c>origin/main</c>).</summary>
    public ObservableCollection<string> RemoteBranches { get; } = [];

    /// <summary>Gets the local and remote branches, for the "branch to merge / rebase on" lists.</summary>
    public ObservableCollection<string> AllBranches { get; } = [];

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasOutput => !string.IsNullOrWhiteSpace(Output);

    public bool CanRun => !IsRunning;

    protected GitModule? Module => _getModule();

    [RelayCommand]
    public void Close() => IsOpen = false;

    /// <summary>Reads the branches off the UI thread and fills the lists.</summary>
    protected async Task LoadRefsAsync()
    {
        GitModule? module = _getModule();
        if (module is null)
        {
            return;
        }

        (string current, List<string> locals, List<string> remotes) = await Task.Run(() =>
        {
            string head = module.GitExecutable.Execute(new GitArgumentBuilder("branch") { "--show-current" }, throwOnErrorExit: false).StandardOutput.Trim();
            return (head, ReadRefs(module, "refs/heads"), ReadRefs(module, "refs/remotes").Where(n => n.Contains('/', StringComparison.Ordinal) && !n.EndsWith("/HEAD", StringComparison.Ordinal)).ToList());
        });

        CurrentBranch = current;
        Replace(LocalBranches, locals);
        Replace(RemoteBranches, remotes);
        Replace(AllBranches, [.. locals.Where(n => n != current), .. remotes]);
        OnRefsLoaded();
    }

    protected virtual void OnRefsLoaded()
    {
    }

    protected void BeginOpen()
    {
        Error = null;
        Output = string.Empty;
        IsRunning = false;
        IsOpen = true;
    }

    /// <summary>
    ///  Runs <paramref name="body"/> as the dialog's one running operation (no second run while it works); an unexpected
    ///  exception is shown inline.
    /// </summary>
    protected async Task GuardedAsync(Func<GitModule, Task> body)
    {
        GitModule? module = _getModule();
        if (module is null || IsRunning)
        {
            return;
        }

        IsRunning = true;
        Error = null;
        Output = string.Empty;
        try
        {
            await body(module);
        }
        catch (Exception ex)
        {
            Error = $"Git operation failed: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    protected static Task<GitRunResult> ExecAsync(GitModule module, params string[] arguments)
        => GitOperationRunner.RunAsync(module.GitExecutable, arguments);

    /// <summary>
    ///  Handles the outcome of the operation that is the point of the dialog: success closes it, conflicts hand over to the
    ///  operation bar (git left a merge/rebase in progress), any other failure stays inline.
    /// </summary>
    protected async Task<bool> CompleteAsync(GitModule module, GitRunResult result, string successStatus, string label)
    {
        OperationKind state = await Task.Run(() => RepositoryOperationState.Detect(module.WorkingDirGitDir));
        Output = $"{result.Output}\n{result.Error}".Trim();
        if (result.Succeeded)
        {
            IsOpen = false;
            await _refresh();
            _reportStatus(successStatus);
            return true;
        }

        if (state != OperationKind.None)
        {
            IsOpen = false;
            await _refresh();
            _reportStatus($"{label} stopped on conflicts. Resolve them, then continue or abort in the operation bar.");
            return false;
        }

        Error = result.Message.Length > 0 ? result.Message : $"git {label.ToLowerInvariant()} failed.";
        return false;
    }

    /// <summary>Resolves <paramref name="expression"/> (any rev-parse expression) to a commit id, or sets <see cref="Error"/>.</summary>
    protected async Task<string?> ResolveAsync(GitModule module, string expression, string what)
    {
        expression = expression.Trim();
        if (expression.Length == 0 || expression.StartsWith('-'))
        {
            Error = $"Enter {what} (a branch, tag or any commit).";
            return null;
        }

        ExecutionResult found = await Task.Run(() => module.GitExecutable.Execute(
            new GitArgumentBuilder("rev-parse") { "--verify", "--quiet", (expression + "^{commit}").Quote() },
            throwOnErrorExit: false));
        if (!found.ExitedSuccessfully)
        {
            Error = $"'{expression}' is not a known branch, tag or commit.";
            return null;
        }

        return found.StandardOutput.Trim();
    }

    protected void ReportStatus(string message) => _reportStatus(message);

    protected Task RefreshAsync() => _refresh();

    private static List<string> ReadRefs(GitModule module, string prefix)
    {
        ExecutionResult result = module.GitExecutable.Execute(new GitArgumentBuilder("for-each-ref") { "--format=%(refname:short)", "--sort=refname", prefix }, throwOnErrorExit: false);
        return [.. result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private static void Replace(ObservableCollection<string> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (string value in values)
        {
            target.Add(value);
        }
    }
}
