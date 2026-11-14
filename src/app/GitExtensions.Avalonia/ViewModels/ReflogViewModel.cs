using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

public enum ReflogPromptKind
{
    None,
    CreateBranch,
    ConfirmHardReset,
}

/// <summary>
///  The reflog of HEAD or of a local branch. An entry can be checked out, turned into a branch or the current branch can be reset to it
///  (the way to recover commits that are no longer on any branch). A hard reset asks for an inline confirmation first.
/// </summary>
public sealed partial class ReflogViewModel : ObservableObject
{
    private const int MaxEntries = 500;
    private readonly ToolsContext _context;
    private int _loadVersion;
    private bool _suppressRefChange;

    [ObservableProperty]
    private string _selectedRef = "HEAD";

    [ObservableProperty]
    private ReflogEntry? _selectedEntry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPromptVisible), nameof(NeedsInput), nameof(PromptText), nameof(ConfirmText))]
    private ReflogPromptKind _promptKind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PromptText))]
    private ReflogEntry? _promptEntry;

    [ObservableProperty]
    private string _branchName = string.Empty;

    [ObservableProperty]
    private string _emptyText = string.Empty;

    public ReflogViewModel(ToolsContext context) => _context = context;

    public ObservableCollection<ReflogEntry> Entries { get; } = [];

    public ObservableCollection<string> Refs { get; } = ["HEAD"];

    public CommitDetailsViewModel Details { get; } = new();

    public bool HasSelection => SelectedEntry is not null;

    public bool IsPromptVisible => PromptKind != ReflogPromptKind.None;

    public bool NeedsInput => PromptKind == ReflogPromptKind.CreateBranch;

    public string PromptText => PromptKind switch
    {
        ReflogPromptKind.CreateBranch => $"New branch at {PromptEntry?.ShortHash}:",
        ReflogPromptKind.ConfirmHardReset => $"Hard reset the current branch to {PromptEntry?.Selector} ({PromptEntry?.ShortHash})? All uncommitted changes in the working tree and index will be lost, and the commits after it will leave this branch (they stay reachable through the reflog).",
        _ => string.Empty,
    };

    public string ConfirmText => PromptKind == ReflogPromptKind.ConfirmHardReset ? "Reset --hard" : "Create branch";

    public void Clear()
    {
        _loadVersion++;
        Entries.Clear();
        SelectedEntry = null;
        PromptKind = ReflogPromptKind.None;
    }

    /// <summary>Reads the list of local branches and the reflog of <see cref="SelectedRef"/> off the UI thread.</summary>
    public async Task LoadAsync()
    {
        GitModule? module = _context.Module;
        if (module is null)
        {
            Clear();
            return;
        }

        int version = ++_loadVersion;
        string reference = SelectedRef;
        try
        {
            (List<string> branches, IReadOnlyList<ReflogEntry> entries) = await _context.RunBusyAsync(() => (ReadBranches(module), ReadReflog(module, reference)));
            if (version != _loadVersion)
            {
                return;
            }

            _suppressRefChange = true;
            try
            {
                Refs.Clear();
                Refs.Add("HEAD");
                foreach (string branch in branches)
                {
                    Refs.Add(branch);
                }

                if (!Refs.Contains(reference))
                {
                    // The branch is gone: fall back to HEAD.
                    SelectedRef = "HEAD";
                    _suppressRefChange = false;
                    await LoadAsync();
                    return;
                }

                SelectedRef = reference;
            }
            finally
            {
                _suppressRefChange = false;
            }

            ApplyEntries(entries);
        }
        catch (Exception ex)
        {
            _context.ReportError($"Could not read the reflog: {ex.Message}");
        }
    }

    private void ApplyEntries(IReadOnlyList<ReflogEntry> entries)
    {
        string? previousSelector = SelectedEntry?.Selector;
        Entries.Clear();
        foreach (ReflogEntry entry in entries)
        {
            Entries.Add(entry);
        }

        EmptyText = entries.Count == 0 ? "The reflog is empty." : string.Empty;
        SelectedEntry = previousSelector is null ? null : Entries.FirstOrDefault(e => e.Selector == previousSelector);
    }

    private static List<string> ReadBranches(GitModule module)
    {
        ExecutionResult result = module.GitExecutable.Execute("for-each-ref --format=%(refname:short) refs/heads", throwOnErrorExit: false);
        return result.ExitedSuccessfully
            ? result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(b => b.Trim()).Where(b => b.Length > 0).ToList()
            : [];
    }

    private static IReadOnlyList<ReflogEntry> ReadReflog(GitModule module, string reference)
    {
        string name = reference == "HEAD" ? "HEAD" : "refs/heads/" + reference;
        ExecutionResult result = module.GitExecutable.Execute(
            new GitArgumentBuilder("reflog") { "show", $"--format={ReflogParser.Format}", $"--max-count={MaxEntries}", name.Quote(), "--" },
            throwOnErrorExit: false);
        return result.ExitedSuccessfully ? ReflogParser.Parse(result.StandardOutput) : [];
    }

    partial void OnSelectedRefChanged(string value)
    {
        if (!_suppressRefChange)
        {
            SelectedEntry = null;
            _ = LoadAsync();
        }
    }

    partial void OnSelectedEntryChanged(ReflogEntry? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        GitModule? module = _context.Module;
        _ = value is not null && module is not null && ObjectId.TryParse(value.Hash, out ObjectId id)
            ? Details.LoadAsync(module, id, value.Selector)
            : Details.ClearAsync();
    }

    // ---- commands ----

    [RelayCommand]
    private Task CopyHashAsync(ReflogEntry? entry)
        => (entry ?? SelectedEntry) is { } target && _context.Clipboard is { } clipboard ? clipboard.SetTextAsync(target.Hash) : Task.CompletedTask;

    [RelayCommand]
    public Task<bool> CheckoutAsync(ReflogEntry? entry)
        => (entry ?? SelectedEntry) is { } target
            ? RunAsync($"Checked out {target.ShortHash} (detached HEAD)", new GitArgumentBuilder("checkout") { "--detach", target.Hash })
            : Task.FromResult(false);

    [RelayCommand]
    private void RequestCreateBranch(ReflogEntry? entry) => OpenPrompt(ReflogPromptKind.CreateBranch, entry ?? SelectedEntry);

    [RelayCommand]
    private void RequestHardReset(ReflogEntry? entry) => OpenPrompt(ReflogPromptKind.ConfirmHardReset, entry ?? SelectedEntry);

    [RelayCommand]
    private Task<bool> ResetSoftAsync(ReflogEntry? entry) => (entry ?? SelectedEntry) is { } target ? ResetAsync(target, CommitResetMode.Soft) : Task.FromResult(false);

    [RelayCommand]
    private Task<bool> ResetMixedAsync(ReflogEntry? entry) => (entry ?? SelectedEntry) is { } target ? ResetAsync(target, CommitResetMode.Mixed) : Task.FromResult(false);

    [RelayCommand]
    private void CancelPrompt() => PromptKind = ReflogPromptKind.None;

    [RelayCommand]
    private async Task ConfirmPromptAsync()
    {
        ReflogEntry? entry = PromptEntry;
        if (entry is null)
        {
            return;
        }

        if (PromptKind == ReflogPromptKind.CreateBranch)
        {
            if (await CreateBranchAsync(entry, BranchName.Trim()))
            {
                PromptKind = ReflogPromptKind.None;
            }

            return;
        }

        PromptKind = ReflogPromptKind.None;
        _ = await ResetAsync(entry, CommitResetMode.Hard, confirmed: true);
    }

    private void OpenPrompt(ReflogPromptKind kind, ReflogEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        BranchName = string.Empty;
        PromptEntry = entry;
        PromptKind = kind;
    }

    // ---- git operations ----

    public Task<bool> CreateBranchAsync(ReflogEntry entry, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('-') || name.Any(char.IsWhiteSpace) || name.Contains('"'))
        {
            _context.ReportError($"'{name}' is not a valid branch name.");
            return Task.FromResult(false);
        }

        return RunAsync($"Created branch {name}", new GitArgumentBuilder("branch") { name.Quote(), entry.Hash });
    }

    /// <summary>Resets the current branch; a hard reset only runs with <paramref name="confirmed"/>, otherwise it opens the inline confirmation.</summary>
    public Task<bool> ResetAsync(ReflogEntry entry, CommitResetMode mode, bool confirmed = false)
    {
        if (mode == CommitResetMode.Hard && !confirmed)
        {
            OpenPrompt(ReflogPromptKind.ConfirmHardReset, entry);
            return Task.FromResult(false);
        }

        string flag = mode switch { CommitResetMode.Soft => "--soft", CommitResetMode.Mixed => "--mixed", _ => "--hard" };
        return RunAsync($"Reset ({flag}) to {entry.ShortHash}", new GitArgumentBuilder("reset") { flag, entry.Hash });
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
                _context.ReportError($"git {arguments.Arguments?.Split(' ')[0]} failed: {ToolsContext.Describe(result)}");
                return false;
            }

            _context.ReportStatus(successMessage);
            await _context.RefreshAsync();
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
