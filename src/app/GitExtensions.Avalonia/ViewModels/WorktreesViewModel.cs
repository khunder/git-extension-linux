using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Extensibility;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

public enum WorktreePromptKind
{
    None,
    Add,
    ConfirmRemove,
    ConfirmForceRemove,
}

/// <summary>
///  Worktrees of the open repository: add (new or existing branch), open, remove and prune. Removing asks for an inline confirmation;
///  a worktree with uncommitted changes needs a second, explicit "force" confirmation that names what is lost.
/// </summary>
public sealed partial class WorktreesViewModel : ObservableObject
{
    private readonly ToolsContext _context;
    private int _loadVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanRemoveSelected))]
    private WorktreeInfo? _selectedWorktree;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAddFormVisible), nameof(IsRemovePromptVisible), nameof(RemovePromptText), nameof(RemoveConfirmText))]
    private WorktreePromptKind _promptKind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemovePromptText))]
    private WorktreeInfo? _promptWorktree;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemovePromptText))]
    private string _lossDescription = string.Empty;

    [ObservableProperty]
    private string _newPath = string.Empty;

    [ObservableProperty]
    private string _newBranch = string.Empty;

    [ObservableProperty]
    private bool _createNewBranch = true;

    [ObservableProperty]
    private string _emptyText = string.Empty;

    public WorktreesViewModel(ToolsContext context) => _context = context;

    public ObservableCollection<WorktreeInfo> Items { get; } = [];

    public bool HasSelection => SelectedWorktree is not null;

    public bool CanRemoveSelected => SelectedWorktree is { IsMain: false };

    public bool IsAddFormVisible => PromptKind == WorktreePromptKind.Add;

    public bool IsRemovePromptVisible => PromptKind is WorktreePromptKind.ConfirmRemove or WorktreePromptKind.ConfirmForceRemove;

    public string RemoveConfirmText => PromptKind == WorktreePromptKind.ConfirmForceRemove ? "Force remove" : "Remove worktree";

    public string RemovePromptText => PromptKind switch
    {
        WorktreePromptKind.ConfirmRemove => $"Remove the worktree {PromptWorktree?.Path}? The folder is deleted; its branch is kept.",
        WorktreePromptKind.ConfirmForceRemove => $"{PromptWorktree?.Path} has changes that will be LOST for good: {LossDescription}. Force remove anyway?",
        _ => string.Empty,
    };

    public void Clear()
    {
        _loadVersion++;
        Items.Clear();
        SelectedWorktree = null;
        PromptKind = WorktreePromptKind.None;
    }

    public async Task LoadAsync()
    {
        GitModule? module = _context.Module;
        if (module is null)
        {
            Clear();
            return;
        }

        int version = ++_loadVersion;
        try
        {
            IReadOnlyList<WorktreeInfo> items = await _context.RunBusyAsync(() =>
            {
                ExecutionResult result = module.GitExecutable.Execute("worktree list --porcelain", throwOnErrorExit: false);
                return result.ExitedSuccessfully ? WorktreeParser.Parse(result.StandardOutput) : [];
            });
            if (version != _loadVersion)
            {
                return;
            }

            string? selectedPath = SelectedWorktree?.Path;
            Items.Clear();
            foreach (WorktreeInfo item in items)
            {
                Items.Add(item);
            }

            EmptyText = items.Count == 0 ? "No worktrees." : string.Empty;
            SelectedWorktree = selectedPath is null ? null : Items.FirstOrDefault(i => i.Path == selectedPath);
        }
        catch (Exception ex)
        {
            _context.ReportError($"Could not list the worktrees: {ex.Message}");
        }
    }

    // ---- add ----

    [RelayCommand]
    private void RequestAdd()
    {
        NewPath = string.Empty;
        NewBranch = string.Empty;
        CreateNewBranch = true;
        PromptKind = WorktreePromptKind.Add;
    }

    [RelayCommand]
    private void CancelPrompt() => PromptKind = WorktreePromptKind.None;

    [RelayCommand]
    private async Task ConfirmAddAsync()
    {
        if (await AddAsync(NewPath, NewBranch, CreateNewBranch))
        {
            PromptKind = WorktreePromptKind.None;
        }
    }

    /// <summary>Adds a worktree at <paramref name="path"/> (relative paths are resolved next to the repository) on a new or an existing branch.</summary>
    public async Task<bool> AddAsync(string path, string branch, bool newBranch)
    {
        GitModule? module = _context.Module;
        path = path.Trim();
        branch = branch.Trim();
        if (module is null)
        {
            return false;
        }

        if (path.Length == 0 || branch.Length == 0 || branch.StartsWith('-') || branch.Any(char.IsWhiteSpace))
        {
            _context.ReportError("Enter a folder and a valid branch name for the new worktree.");
            return false;
        }

        string parent = Path.GetDirectoryName(module.WorkingDir.TrimEnd('/', '\\')) ?? module.WorkingDir;
        string fullPath = Path.GetFullPath(Path.Combine(parent, path));
        GitArgumentBuilder arguments = new("worktree") { "add" };
        if (newBranch)
        {
            arguments.Add("-b");
            arguments.Add(branch.Quote());
            arguments.Add(fullPath.Quote());
        }
        else
        {
            arguments.Add(fullPath.Quote());
            arguments.Add(branch.Quote());
        }

        return await RunAsync($"Added worktree {fullPath}", arguments);
    }

    // ---- open / prune ----

    [RelayCommand]
    public async Task<bool> OpenAsync(WorktreeInfo? worktree)
    {
        if ((worktree ?? SelectedWorktree) is not { } target)
        {
            return false;
        }

        if (!Directory.Exists(target.Path))
        {
            _context.ReportError($"The folder {target.Path} does not exist (prune the stale worktree).");
            return false;
        }

        await _context.OpenRepositoryAsync(target.Path);
        return true;
    }

    [RelayCommand]
    public Task<bool> PruneAsync() => RunAsync("Pruned stale worktrees", new GitArgumentBuilder("worktree") { "prune", "--verbose" });

    // ---- remove ----

    [RelayCommand]
    private void RequestRemove(WorktreeInfo? worktree)
    {
        if ((worktree ?? SelectedWorktree) is not { } target)
        {
            return;
        }

        if (target.IsMain)
        {
            _context.ReportError("The main worktree cannot be removed.");
            return;
        }

        PromptWorktree = target;
        PromptKind = WorktreePromptKind.ConfirmRemove;
    }

    [RelayCommand]
    private async Task ConfirmRemoveAsync()
    {
        if (PromptWorktree is not { } target)
        {
            return;
        }

        bool force = PromptKind == WorktreePromptKind.ConfirmForceRemove;
        PromptKind = WorktreePromptKind.None;
        _ = await RemoveAsync(target, force);
    }

    /// <summary>
    ///  Removes a worktree. Without <paramref name="force"/> git refuses when the worktree has changes, and the view-model then asks for the
    ///  second confirmation (<see cref="WorktreePromptKind.ConfirmForceRemove"/>) instead of forcing.
    /// </summary>
    public async Task<bool> RemoveAsync(WorktreeInfo worktree, bool force = false)
    {
        GitModule? module = _context.Module;
        if (module is null)
        {
            return false;
        }

        if (worktree.IsMain)
        {
            _context.ReportError("The main worktree cannot be removed.");
            return false;
        }

        GitArgumentBuilder arguments = new("worktree") { "remove", { force, "--force" }, "--", worktree.Path.Quote() };
        try
        {
            ExecutionResult result = await _context.RunGitAsync(module, arguments);
            if (result.ExitedSuccessfully)
            {
                _context.ReportStatus($"Removed worktree {worktree.Path}");
                await _context.RefreshAsync();
                await LoadAsync();
                return true;
            }

            string changes = force || !Directory.Exists(worktree.Path) ? string.Empty : await DescribeChangesAsync(module, worktree.Path);
            if (changes.Length > 0)
            {
                PromptWorktree = worktree;
                LossDescription = changes;
                PromptKind = WorktreePromptKind.ConfirmForceRemove;
            }
            else
            {
                _context.ReportError($"git worktree remove failed: {ToolsContext.Describe(result)}");
            }

            return false;
        }
        catch (Exception ex)
        {
            _context.ReportError($"Git operation failed: {ex.Message}");
            return false;
        }
    }

    private async Task<string> DescribeChangesAsync(GitModule module, string path)
    {
        ExecutionResult status = await _context.RunGitAsync(module, $"-C {path.Quote()} status --porcelain");
        if (!status.ExitedSuccessfully)
        {
            return string.Empty;
        }

        string[] lines = status.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int untracked = lines.Count(l => l.StartsWith("??", StringComparison.Ordinal));
        int tracked = lines.Length - untracked;
        return lines.Length == 0 ? string.Empty : $"{tracked} modified/staged and {untracked} untracked file(s)";
    }

    private async Task<bool> RunAsync(string successMessage, ArgumentString arguments)
    {
        GitModule? module = _context.Module;
        if (module is null)
        {
            return false;
        }

        try
        {
            ExecutionResult result = await _context.RunGitAsync(module, arguments);
            if (!result.ExitedSuccessfully)
            {
                _context.ReportError($"git worktree failed: {ToolsContext.Describe(result)}");
                return false;
            }

            _context.ReportStatus(successMessage);
            await LoadAsync();
            return true;
        }
        catch (Exception ex)
        {
            _context.ReportError($"Git operation failed: {ex.Message}");
            return false;
        }
    }
}
