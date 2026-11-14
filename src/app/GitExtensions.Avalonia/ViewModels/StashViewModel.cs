using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Diff;
using GitExtensions.Extensibility;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The stash list and its operations. Git runs on a background thread; state is updated on the UI-thread continuation.
///  Failures (nothing to stash, conflicts, ...) go to <c>reportError</c> (the banner). Drop asks for an inline confirmation.
/// </summary>
public sealed partial class StashViewModel : ObservableObject
{
    private readonly Func<GitModule?> _getModule;
    private readonly Func<Task> _refresh;
    private readonly Action<string> _reportError;
    private readonly Action<string> _reportStatus;
    private CancellationTokenSource? _detailsCancellation;
    private int _loadVersion;

    [ObservableProperty]
    private StashItemViewModel? _selectedStash;

    [ObservableProperty]
    private ChangedFileViewModel? _selectedFile;

    [ObservableProperty]
    private bool _isStashFormVisible;

    [ObservableProperty]
    private string _stashMessage = string.Empty;

    [ObservableProperty]
    private bool _includeUntracked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDropPending), nameof(DropPrompt))]
    private StashItemViewModel? _pendingDrop;

    [ObservableProperty]
    private string _diffMessage = "Select a stash to see its files.";

    public StashViewModel(Func<GitModule?> getModule, Func<Task> refresh, Action<string> reportError, Action<string>? reportStatus = null)
    {
        _getModule = getModule;
        _refresh = refresh;
        _reportError = reportError;
        _reportStatus = reportStatus ?? (static _ => { });
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ButtonText));
    }

    public ObservableCollection<StashItemViewModel> Items { get; } = [];

    public ObservableCollection<ChangedFileViewModel> Files { get; } = [];

    public RangeObservableCollection<DiffLine> DiffLines { get; } = [];

    public string ButtonText => Items.Count == 0 ? "Stash ▾" : $"Stash ({Items.Count}) ▾";

    public bool IsDropPending => PendingDrop is not null;

    public string DropPrompt => PendingDrop is { } drop ? $"Drop {drop.Name} \"{Truncate(drop.Message)}\"? Its changes will be lost for good." : string.Empty;

    public bool HasSelection => SelectedStash is not null;

    private static string Truncate(string text) => text.Length > 60 ? text[..60] + "…" : text;

    /// <summary>Clears everything when no repository is open.</summary>
    public void Clear()
    {
        _loadVersion++;
        Items.Clear();
        Files.Clear();
        DiffLines.Clear();
        SelectedStash = null;
        PendingDrop = null;
        IsStashFormVisible = false;
    }

    /// <summary>Reads <c>git stash list</c> off the UI thread and replaces the list, keeping the selection by index when possible.</summary>
    public async Task LoadAsync()
    {
        GitModule? module = _getModule();
        if (module is null)
        {
            Clear();
            return;
        }

        int version = ++_loadVersion;
        List<StashItemViewModel> stashes;
        try
        {
            stashes = await Task.Run(() => ReadStashes(module));
        }
        catch (Exception ex)
        {
            _reportError($"Could not list the stashes: {ex.Message}");
            return;
        }

        if (version != _loadVersion)
        {
            return;
        }

        int? selectedIndex = SelectedStash?.Index;
        Items.Clear();
        foreach (StashItemViewModel stash in stashes)
        {
            Items.Add(stash);
        }

        SelectedStash = selectedIndex is { } index ? Items.FirstOrDefault(s => s.Index == index) : null;
        if (PendingDrop is { } pending && Items.All(s => s.Index != pending.Index))
        {
            PendingDrop = null;
        }
    }

    private static List<StashItemViewModel> ReadStashes(GitModule module)
    {
        ExecutionResult result = module.GitExecutable.Execute("stash list --format=%gd%x1f%gs%x1f%cI", throwOnErrorExit: false);
        List<StashItemViewModel> stashes = [];
        if (!result.ExitedSuccessfully)
        {
            return stashes;
        }

        foreach (string line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (StashItemViewModel.TryParse(line.TrimEnd('\r')) is { } stash)
            {
                stashes.Add(stash);
            }
        }

        return stashes;
    }

    // ---- stash details (files + diff) ----

    partial void OnSelectedStashChanged(StashItemViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        _ = ShowFilesAsync(value);
    }

    partial void OnSelectedFileChanged(ChangedFileViewModel? value) => _ = ShowDiffAsync(SelectedStash, value);

    private async Task ShowFilesAsync(StashItemViewModel? stash)
    {
        if (_detailsCancellation is not null)
        {
            await _detailsCancellation.CancelAsync();
        }

        CancellationTokenSource cancellation = new();
        _detailsCancellation = cancellation;
        Files.Clear();
        DiffLines.Clear();
        SelectedFile = null;
        GitModule? module = _getModule();
        if (stash is null || module is null)
        {
            DiffMessage = "Select a stash to see its files.";
            return;
        }

        DiffMessage = "Loading…";
        try
        {
            List<ChangedFileViewModel> files = await Task.Run(() => module.GetStashDiffFiles(stash.Name).Select(item => new ChangedFileViewModel(item)).ToList());
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            foreach (ChangedFileViewModel file in files)
            {
                Files.Add(file);
            }

            DiffMessage = files.Count == 0 ? "This stash has no file changes." : "Select a file to see its diff.";
        }
        catch (Exception ex)
        {
            DiffMessage = $"Failed to load the stash: {ex.Message}";
        }
    }

    private async Task ShowDiffAsync(StashItemViewModel? stash, ChangedFileViewModel? file)
    {
        if (_detailsCancellation is not null)
        {
            await _detailsCancellation.CancelAsync();
        }

        CancellationTokenSource cancellation = new();
        _detailsCancellation = cancellation;
        DiffLines.Clear();
        GitModule? module = _getModule();
        if (stash is null || file is null || module is null)
        {
            return;
        }

        DiffMessage = "Loading diff…";
        try
        {
            IReadOnlyList<DiffLine> lines = await Task.Run(() => LoadDiff(module, stash, file.Name));
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            DiffLimits.Truncate(ref lines);
            DiffLines.ReplaceAll(lines);

            DiffMessage = lines.Count == 0 ? "No textual changes." : string.Empty;
        }
        catch (Exception ex)
        {
            DiffMessage = $"Failed to load the diff: {ex.Message}";
        }
    }

    private static IReadOnlyList<DiffLine> LoadDiff(GitModule module, StashItemViewModel stash, string fileName)
    {
        ExecutionResult diff = module.GitExecutable.Execute(
            new GitArgumentBuilder("diff") { $"{stash.Name}^".Quote(), stash.Name.Quote(), "--", fileName.Quote() },
            throwOnErrorExit: false);
        if (diff.ExitedSuccessfully && !string.IsNullOrWhiteSpace(diff.StandardOutput))
        {
            return UnifiedDiffParser.Parse(diff.StandardOutput);
        }

        // Untracked files stashed with --include-untracked live in the third parent.
        ExecutionResult show = module.GitExecutable.Execute(
            new GitArgumentBuilder("show") { $"{stash.Name}^3:{fileName}".Quote() },
            outputEncoding: Encoding.UTF8,
            throwOnErrorExit: false);
        return show.ExitedSuccessfully ? UnifiedDiffParser.FromNewFileContent(fileName, show.StandardOutput) : [];
    }

    // ---- commands ----

    [RelayCommand]
    private void RequestStash()
    {
        StashMessage = string.Empty;
        IncludeUntracked = false;
        IsStashFormVisible = true;
    }

    [RelayCommand]
    private void CancelStash() => IsStashFormVisible = false;

    [RelayCommand]
    public async Task<bool> ConfirmStashAsync()
    {
        string message = StashMessage.Replace('\r', ' ').Replace('\n', ' ').Trim();
        bool includeUntracked = IncludeUntracked;
        IsStashFormVisible = false;
        return await RunAsync(
            "Stashed changes",
            new GitArgumentBuilder("stash") { "push", { includeUntracked, "--include-untracked" }, { message.Length > 0, "-m " + message.Quote() } },
            checkNothingToSave: true);
    }

    [RelayCommand]
    public Task<bool> ApplyAsync(StashItemViewModel? stash)
        => (stash ?? SelectedStash) is { } target
            ? RunAsync($"Applied {target.Name}", new GitArgumentBuilder("stash") { "apply", target.Name.Quote() }, kept: target.Name)
            : Task.FromResult(false);

    [RelayCommand]
    public Task<bool> PopAsync(StashItemViewModel? stash)
        => (stash ?? SelectedStash) is { } target
            ? RunAsync($"Popped {target.Name}", new GitArgumentBuilder("stash") { "pop", target.Name.Quote() }, kept: target.Name)
            : Task.FromResult(false);

    [RelayCommand]
    private void RequestDrop(StashItemViewModel? stash) => PendingDrop = stash ?? SelectedStash;

    [RelayCommand]
    private void CancelDrop() => PendingDrop = null;

    [RelayCommand]
    private async Task ConfirmDropAsync()
    {
        if (PendingDrop is { } stash)
        {
            PendingDrop = null;
            _ = await DropAsync(stash, confirmed: true);
        }
    }

    /// <summary>
    ///  Drops a stash. It destroys data, so it only runs with <paramref name="confirmed"/>; otherwise it opens the
    ///  inline confirmation and returns <see langword="false"/> without touching the repository.
    /// </summary>
    public Task<bool> DropAsync(StashItemViewModel stash, bool confirmed = false)
    {
        if (!confirmed)
        {
            PendingDrop = stash;
            return Task.FromResult(false);
        }

        return RunAsync($"Dropped {stash.Name}", new GitArgumentBuilder("stash") { "drop", stash.Name.Quote() });
    }

    private async Task<bool> RunAsync(string successMessage, ArgumentString arguments, bool checkNothingToSave = false, string? kept = null)
    {
        GitModule? module = _getModule();
        if (module is null)
        {
            return false;
        }

        ExecutionResult result;
        try
        {
            result = await Task.Run(() => module.GitExecutable.Execute(arguments, throwOnErrorExit: false));
        }
        catch (Exception ex)
        {
            _reportError($"Git operation failed: {ex.Message}");
            return false;
        }

        string output = (result.StandardOutput + "\n" + result.StandardError).Trim();
        if (!result.ExitedSuccessfully)
        {
            string note = kept is not null ? $" ({kept} was kept; resolve or discard the conflicting changes first.)" : string.Empty;
            _reportError($"git {arguments.Arguments?.Split(' ')[0]} failed: {output}{note}");
            await RefreshAsync();
            return false;
        }

        if (checkNothingToSave && output.Contains("No local changes to save", StringComparison.OrdinalIgnoreCase))
        {
            _reportError("There are no local changes to stash.");
            return false;
        }

        // pop exits non-zero on conflicts, but be defensive: a "CONFLICT" report means the stash was kept.
        if (output.Contains("CONFLICT", StringComparison.Ordinal))
        {
            _reportError($"{successMessage} with conflicts: {output}");
        }
        else
        {
            _reportStatus(successMessage);
        }

        await RefreshAsync();
        return true;
    }

    private async Task RefreshAsync()
    {
        await _refresh();
        await LoadAsync();
    }
}
