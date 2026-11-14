using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

public enum RefPromptKind
{
    None,
    RenameBranch,
    NewBranch,
    ForceDeleteBranch,
    SetUpstream,
    DeleteRemoteBranch,
    NewTag,
    DeleteTag,
    PushAllTags,
}

// Branch and tag management with inline prompts, plus upstream (ahead/behind) tracking (kept out of the main file).
public sealed partial class RefsTreeViewModel
{
    private string _promptTarget = string.Empty;

    [ObservableProperty]
    private RefPromptKind _promptKind;

    [ObservableProperty]
    private string _promptTitle = string.Empty;

    [ObservableProperty]
    private string _promptDetail = string.Empty;

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private string _startPoint = "HEAD";

    [ObservableProperty]
    private string _messageText = string.Empty;

    [ObservableProperty]
    private bool _checkoutAfterCreate = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationError))]
    private string? _validationError;

    [ObservableProperty]
    private string? _selectedUpstream;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrentTrack), nameof(CurrentTrackText), nameof(CurrentTrackToolTip))]
    private UpstreamTrack? _currentTrack;

    /// <summary>Shows git errors in the banner; defaults to the status bar.</summary>
    public Action<string>? ReportError { get; set; }

    /// <summary>The commit selected in the history list, used as default start point.</summary>
    public Func<ObjectId?>? SelectedCommitProvider { get; set; }

    /// <summary>
    ///  Runs a network git command (push, fetch) with output log and credential prompts; set by the main view-model.
    ///  Without it the command runs directly.
    /// </summary>
    public Func<string, IReadOnlyList<string>, Task>? RunRemote { get; set; }

    public IClipboardService? Clipboard { get; set; }

    public ObservableCollection<string> UpstreamChoices { get; } = [];

    public bool IsPromptVisible => PromptKind != RefPromptKind.None;

    public bool ShowName => PromptKind is RefPromptKind.RenameBranch or RefPromptKind.NewBranch or RefPromptKind.NewTag;

    public bool ShowStartPoint => PromptKind is RefPromptKind.NewBranch or RefPromptKind.NewTag;

    public bool ShowMessage => PromptKind == RefPromptKind.NewTag;

    public bool ShowCheckout => PromptKind == RefPromptKind.NewBranch;

    public bool ShowUpstreamPicker => PromptKind == RefPromptKind.SetUpstream;

    public bool HasDetail => PromptDetail.Length > 0;

    public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);

    public bool HasCurrentTrack => CurrentTrack is not null;

    public string CurrentTrackText => CurrentTrack?.Text ?? string.Empty;

    public string CurrentTrackToolTip => CurrentTrack?.ToolTip ?? string.Empty;

    public string ConfirmText => PromptKind switch
    {
        RefPromptKind.RenameBranch => "Rename",
        RefPromptKind.NewBranch => "Create branch",
        RefPromptKind.ForceDeleteBranch => "Force delete",
        RefPromptKind.SetUpstream => "Set upstream",
        RefPromptKind.DeleteRemoteBranch => "Delete on remote",
        RefPromptKind.NewTag => "Create tag",
        RefPromptKind.DeleteTag => "Delete tag",
        RefPromptKind.PushAllTags => "Push all tags",
        _ => "OK",
    };

    partial void OnPromptKindChanged(RefPromptKind value)
    {
        OnPropertyChanged(nameof(IsPromptVisible));
        OnPropertyChanged(nameof(ShowName));
        OnPropertyChanged(nameof(ShowStartPoint));
        OnPropertyChanged(nameof(ShowMessage));
        OnPropertyChanged(nameof(ShowCheckout));
        OnPropertyChanged(nameof(ShowUpstreamPicker));
        OnPropertyChanged(nameof(ConfirmText));
    }

    partial void OnPromptDetailChanged(string value) => OnPropertyChanged(nameof(HasDetail));

    // ---- parsing the refs with their upstream ----

    private static Dictionary<string, UpstreamTrack> ReadTracking(GitModule module)
    {
        try
        {
            ExecutionResult result = module.GitExecutable.Execute(
                $"for-each-ref --format=\"{UpstreamTrackParser.ForEachRefFormat}\" refs/heads",
                throwOnErrorExit: false);
            return result.ExitedSuccessfully ? UpstreamTrackParser.ParseAll(result.StandardOutput) : [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static IReadOnlyList<RefEntry> ApplyTracking(IReadOnlyList<RefEntry> entries, Dictionary<string, UpstreamTrack> tracks)
        => tracks.Count == 0
            ? entries
            : [.. entries.Select(e => e.Kind == RefNodeKind.LocalBranch && tracks.TryGetValue(e.Name, out UpstreamTrack? t) ? e with { Track = t } : e)];

    // ---- prompts ----

    private void OpenPrompt(RefPromptKind kind, string target, string title, string detail = "", string input = "")
    {
        _promptTarget = target;
        PromptTitle = title;
        PromptDetail = detail;
        InputText = input;
        MessageText = string.Empty;
        ValidationError = null;
        PromptKind = kind;
    }

    [RelayCommand]
    private void CancelPrompt()
    {
        PromptKind = RefPromptKind.None;
        PromptDetail = string.Empty;
        ValidationError = null;
        _promptTarget = string.Empty;
    }

    private string DefaultStartPoint() => SelectedCommitProvider?.Invoke() is ObjectId id ? id.ToString() : "HEAD";

    [RelayCommand]
    private void RequestRename(RefNodeViewModel? node)
    {
        if (node is { Kind: RefNodeKind.LocalBranch })
        {
            OpenPrompt(RefPromptKind.RenameBranch, node.FullName, $"Rename branch '{node.FullName}' to:", input: node.FullName);
        }
    }

    [RelayCommand]
    private void RequestNewBranch(RefNodeViewModel? node)
    {
        OpenPrompt(RefPromptKind.NewBranch, string.Empty, "New branch:");
        StartPoint = node is { IsLeaf: true } ? node.FullName : DefaultStartPoint();
        CheckoutAfterCreate = true;
    }

    [RelayCommand]
    private void RequestNewTag(RefNodeViewModel? node)
    {
        OpenPrompt(RefPromptKind.NewTag, string.Empty, "New tag (add a message to make it annotated):");
        StartPoint = node is { Kind: RefNodeKind.LocalBranch or RefNodeKind.RemoteBranch } ? node.FullName : DefaultStartPoint();
    }

    [RelayCommand]
    private void RequestSetUpstream(RefNodeViewModel? node)
    {
        if (node is not { Kind: RefNodeKind.LocalBranch })
        {
            return;
        }

        OpenPrompt(RefPromptKind.SetUpstream, node.FullName, $"Upstream of '{node.FullName}':");
        UpstreamChoices.Clear();
        foreach (RefEntry entry in _entries.Where(e => e.Kind == RefNodeKind.RemoteBranch).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            UpstreamChoices.Add(entry.Name);
        }

        SelectedUpstream = node.Track?.Upstream is { } current && UpstreamChoices.Contains(current) ? current
            : UpstreamChoices.FirstOrDefault(c => c.EndsWith("/" + node.FullName, StringComparison.Ordinal)) ?? UpstreamChoices.FirstOrDefault();
        if (UpstreamChoices.Count == 0)
        {
            ValidationError = "There are no remote branches to track. Fetch a remote first.";
        }
    }

    [RelayCommand]
    private void RequestDeleteRemoteBranch(RefNodeViewModel? node)
    {
        if (node is { Kind: RefNodeKind.RemoteBranch, RemoteName: { } remote })
        {
            string branch = node.FullName[(remote.Length + 1)..];
            OpenPrompt(
                RefPromptKind.DeleteRemoteBranch,
                node.FullName,
                $"Delete branch '{branch}' on remote '{remote}'?",
                $"This removes '{branch}' from the server. Everyone else tracking it loses it, and commits that exist only on this branch become unreachable there.");
        }
    }

    [RelayCommand]
    private void RequestDeleteTag(RefNodeViewModel? node)
    {
        if (node is { Kind: RefNodeKind.Tag })
        {
            OpenPrompt(
                RefPromptKind.DeleteTag,
                node.FullName,
                $"Delete tag '{node.FullName}'?",
                "The local tag is removed (an annotated tag also loses its message). A tag that was never pushed cannot be recovered; the tagged commit itself stays.");
        }
    }

    [RelayCommand]
    private void RequestPushAllTags()
    {
        int count = _entries.Count(e => e.Kind == RefNodeKind.Tag);
        OpenPrompt(
            RefPromptKind.PushAllTags,
            string.Empty,
            $"Push all {count} local tags to the remote?",
            "Every tag is published, including ones you created by accident. Published tags are hard to retract.");
    }

    [RelayCommand]
    public async Task ConfirmPromptAsync()
    {
        GitModule? module = _module;
        RefPromptKind kind = PromptKind;
        string target = _promptTarget;
        string name = InputText.Trim();
        if (module is null || kind == RefPromptKind.None)
        {
            return;
        }

        switch (kind)
        {
            case RefPromptKind.RenameBranch:
            case RefPromptKind.NewBranch:
            case RefPromptKind.NewTag:
            {
                string start = StartPoint.Trim();
                string message = MessageText.Trim();
                bool checkout = CheckoutAfterCreate;
                bool isTag = kind == RefPromptKind.NewTag;
                string? error = await Task.Run(() => ValidateNameAndStart(module, name, isTag, kind == RefPromptKind.RenameBranch ? null : start, kind == RefPromptKind.RenameBranch ? target : null));
                if (error is not null)
                {
                    ValidationError = error;
                    return;
                }

                CancelPrompt();
                if (kind == RefPromptKind.RenameBranch)
                {
                    await RunGitAsync($"Renamed branch {target} to {name}", "branch", ["-m", target, name]);
                }
                else if (kind == RefPromptKind.NewBranch)
                {
                    await RunGitAsync($"Created branch {name}", checkout ? "checkout" : "branch", checkout ? ["-b", name, start] : [name, start]);
                }
                else
                {
                    await CreateTagAsync(module, name, start, message);
                }

                return;
            }

            case RefPromptKind.ForceDeleteBranch:
                CancelPrompt();
                await RunGitAsync($"Deleted branch {target}", "branch", ["-D", target]);
                return;

            case RefPromptKind.SetUpstream:
                if (SelectedUpstream is not { Length: > 0 } upstream)
                {
                    ValidationError = "Choose a remote branch.";
                    return;
                }

                CancelPrompt();
                await RunGitAsync($"{target} now tracks {upstream}", "branch", [$"--set-upstream-to={upstream}", target]);
                return;

            case RefPromptKind.DeleteRemoteBranch:
            {
                CancelPrompt();
                string remote = target[..target.IndexOf('/')];
                await RunNetworkAsync($"Delete {target}", ["push", "--progress", remote, "--delete", target[(remote.Length + 1)..]], module);
                return;
            }

            case RefPromptKind.DeleteTag:
                CancelPrompt();
                await RunGitAsync($"Deleted tag {target}", "tag", ["-d", target]);
                return;

            case RefPromptKind.PushAllTags:
            {
                CancelPrompt();
                string? remote = await Task.Run(() => PickRemote(module));
                if (remote is null)
                {
                    Report("The repository has no remotes to push tags to.");
                    return;
                }

                await RunNetworkAsync("Push all tags", ["push", "--progress", remote, "--tags"], module);
                return;
            }
        }
    }

    private string? ValidateNameAndStart(GitModule module, string name, bool isTag, string? start, string? renaming)
    {
        string? error = RefNameValidator.Validate(module.GitExecutable, name, isTag);
        if (error is not null)
        {
            return error;
        }

        if (_entries.Any(e => e.Kind == (isTag ? RefNodeKind.Tag : RefNodeKind.LocalBranch) && e.Name == name))
        {
            return isTag ? $"A tag named '{name}' already exists." : $"A branch named '{name}' already exists.";
        }

        if (start is not null)
        {
            if (start.Length == 0 || start.Any(char.IsWhiteSpace) || start.StartsWith('-') || start.Contains('"'))
            {
                return "Enter a valid start point (a commit, branch or tag).";
            }

            if (!module.GitExecutable.Execute($"rev-parse --verify --quiet {start}^{{commit}}", throwOnErrorExit: false).ExitedSuccessfully)
            {
                return $"'{start}' is not a commit.";
            }
        }

        return null;
    }

    private async Task CreateTagAsync(GitModule module, string name, string start, string message)
    {
        if (message.Length == 0)
        {
            await RunGitAsync($"Created tag {name}", "tag", [name, start]);
            return;
        }

        // The message goes through a file: no quoting problems with quotes or line breaks.
        string file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, message + "\n");
            await RunGitAsync($"Created annotated tag {name}", "tag", ["-a", name, "-F", file, start]);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Best effort cleanup of a temp file.
            }
        }
    }

    // ---- direct commands ----

    [RelayCommand]
    private async Task DeleteBranchAsync(RefNodeViewModel? node)
    {
        GitModule? module = _module;
        if (module is null || node is not { Kind: RefNodeKind.LocalBranch })
        {
            return;
        }

        string branch = node.FullName;
        if (node.IsCurrent || branch == _currentBranch)
        {
            Report($"Cannot delete '{branch}': it is the current branch. Check out another branch first.");
            return;
        }

        (ExecutionResult result, int unique, string tip) = await Task.Run(() =>
        {
            ExecutionResult run = module.GitExecutable.Execute(Args("branch", "-d", branch), throwOnErrorExit: false);
            if (run.ExitedSuccessfully || !IsNotMerged(run))
            {
                return (run, 0, string.Empty);
            }

            ExecutionResult count = module.GitExecutable.Execute(Args("rev-list", "--count", branch, "--not", $"--exclude=refs/heads/{branch}", "--all"), throwOnErrorExit: false);
            ExecutionResult hash = module.GitExecutable.Execute(Args("rev-parse", "--short", branch), throwOnErrorExit: false);
            return (run, int.TryParse(count.StandardOutput.Trim(), out int n) ? n : 0, hash.StandardOutput.Trim());
        });

        if (result.ExitedSuccessfully)
        {
            _reportStatus($"Deleted branch {branch}");
            await _refreshAll();
        }
        else if (IsNotMerged(result))
        {
            string loss = unique > 0
                ? $"{unique} commit{(unique == 1 ? string.Empty : "s")} will be lost: only '{branch}' contains {(unique == 1 ? "it" : "them")} (tip {tip})."
                : $"'{branch}' is not merged into the current branch, but its commits are still reachable from other branches or remotes (tip {tip}), so nothing is lost.";
            OpenPrompt(RefPromptKind.ForceDeleteBranch, branch, $"Branch '{branch}' is not fully merged. Delete it anyway?", loss);
        }
        else
        {
            Report($"git branch -d failed: {ErrorOf(result)}");
        }
    }

    [RelayCommand]
    private Task UnsetUpstreamAsync(RefNodeViewModel? node)
        => node is { Kind: RefNodeKind.LocalBranch, HasUpstream: true }
            ? RunGitAsync($"Removed the upstream of {node.FullName}", "branch", ["--unset-upstream", node.FullName])
            : Task.CompletedTask;

    [RelayCommand]
    private async Task PushBranchAsync(RefNodeViewModel? node)
    {
        GitModule? module = _module;
        if (module is null || node is not { Kind: RefNodeKind.LocalBranch })
        {
            return;
        }

        string branch = node.FullName;
        if (node.Track is { RemoteName.Length: > 0 } track)
        {
            string remoteBranch = track.RemoteRef.Length > 0 ? track.RemoteRef : branch;
            await RunNetworkAsync($"Push {branch}", ["push", "--progress", track.RemoteName, $"refs/heads/{branch}:refs/heads/{remoteBranch}"], module);
            return;
        }

        string? remote = await Task.Run(() => PickRemote(module));
        if (remote is null)
        {
            Report("The repository has no remotes to push to.");
            return;
        }

        await RunNetworkAsync($"Push {branch}", ["push", "--progress", "--set-upstream", remote, branch], module);
    }

    [RelayCommand]
    private async Task CheckoutRemoteAsTrackingAsync(RefNodeViewModel? node)
    {
        if (node is not { Kind: RefNodeKind.RemoteBranch, RemoteName: { } remote })
        {
            return;
        }

        string local = node.FullName[(remote.Length + 1)..];
        if (_entries.Any(e => e.Kind == RefNodeKind.LocalBranch && e.Name == local))
        {
            Report($"A local branch named '{local}' already exists. Check it out, or rename it first.");
            return;
        }

        await RunGitAsync($"Checked out {node.FullName} as local branch {local}", "checkout", ["-b", local, "--track", node.FullName]);
    }

    [RelayCommand]
    private async Task FetchRemoteAsync(RefNodeViewModel? node)
    {
        if (_module is { } module && node?.RemoteName is { } remote)
        {
            await RunNetworkAsync($"Fetch {remote}", ["fetch", "--progress", remote], module);
        }
    }

    [RelayCommand]
    private async Task PushTagAsync(RefNodeViewModel? node)
    {
        GitModule? module = _module;
        if (module is null || node is not { Kind: RefNodeKind.Tag })
        {
            return;
        }

        string? remote = await Task.Run(() => PickRemote(module));
        if (remote is null)
        {
            Report("The repository has no remotes to push to.");
            return;
        }

        await RunNetworkAsync($"Push tag {node.FullName}", ["push", "--progress", remote, $"refs/tags/{node.FullName}"], module);
    }

    [RelayCommand]
    private Task CopyNameAsync(RefNodeViewModel? node)
        => node is { IsLeaf: true } && Clipboard is not null ? Clipboard.SetTextAsync(node.FullName) : Task.CompletedTask;

    // ---- plumbing ----

    private static string? PickRemote(GitModule module)
    {
        IReadOnlyList<string> names = module.GetRemoteNames();
        return names.Contains("origin") ? "origin" : names.FirstOrDefault();
    }

    private static bool IsNotMerged(ExecutionResult result)
        => ErrorOf(result).Contains("not fully merged", StringComparison.OrdinalIgnoreCase)
            || ErrorOf(result).Contains("branch -D", StringComparison.Ordinal);

    private static string ErrorOf(ExecutionResult result)
        => (string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError).Trim();

    private static ArgumentString Args(string command, params string[] arguments)
        => $"{command} {string.Join(' ', arguments.Select(a => a.Length > 0 && a.IndexOfAny([' ', '\t']) < 0 ? a : $"\"{a}\""))}";

    private void Report(string message) => (ReportError ?? _reportStatus)(message);

    /// <summary>Runs a local git command off the UI thread; reports failures, reloads everything on success.</summary>
    private async Task<bool> RunGitAsync(string successMessage, string command, string[] arguments)
    {
        GitModule? module = _module;
        if (module is null)
        {
            return false;
        }

        try
        {
            ExecutionResult result = await Task.Run(() => module.GitExecutable.Execute(Args(command, arguments), throwOnErrorExit: false));
            if (!result.ExitedSuccessfully)
            {
                Report($"git {command} failed: {ErrorOf(result)}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Report($"git {command} failed: {ex.Message}");
            return false;
        }

        _reportStatus(successMessage);
        await _refreshAll();
        return true;
    }

    private async Task RunNetworkAsync(string title, string[] arguments, GitModule module)
    {
        if (RunRemote is not null)
        {
            await RunRemote(title, arguments);
            return;
        }

        await RunGitAsync($"{title} completed", arguments[0], arguments[1..]);
    }
}
