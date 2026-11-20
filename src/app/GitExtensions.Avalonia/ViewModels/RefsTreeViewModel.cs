using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitCommands.Git;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The branches/remotes/tags sidebar.
/// </summary>
public sealed partial class RefsTreeViewModel : ObservableObject
{
    private readonly Action<string> _reportStatus;
    private readonly Action<ObjectId> _selectCommit;
    private readonly Func<Task> _refreshAll;
    private GitModule? _module;
    private IReadOnlyDictionary<string, string> _remoteHosts = new Dictionary<string, string>();
    private IReadOnlyList<RefEntry> _entries = [];
    private string? _currentBranch;
    private CancellationTokenSource? _filterDebounce;

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private RefNodeViewModel? _selectedNode;

    /// <param name="reportStatus">Shows a message in the status bar.</param>
    /// <param name="selectCommit">Selects a commit in the history list, if it is loaded.</param>
    /// <param name="refreshAll">Reloads branches, history and changes after a checkout.</param>
    public RefsTreeViewModel(Action<string>? reportStatus = null, Action<ObjectId>? selectCommit = null, Func<Task>? refreshAll = null)
    {
        _reportStatus = reportStatus ?? (static _ => { });
        _selectCommit = selectCommit ?? (static _ => { });
        _refreshAll = refreshAll ?? (static () => Task.CompletedTask);
    }

    public ObservableCollection<RefNodeViewModel> Roots { get; } = [];

    public void Clear()
    {
        _filterDebounce?.Cancel();
        _module = null;
        _entries = [];
        _currentBranch = null;
        CurrentTrack = null;
        LocalBranchNames.Clear();
        CancelPrompt();
        Roots.Clear();
    }

    /// <summary>
    ///  Reads branches, remote branches and tags (off the UI thread) and rebuilds the tree.
    /// </summary>
    public async Task LoadAsync(GitModule module)
    {
        _module = module;
        _remoteHosts = RemoteHosts.Read(module.WorkingDirGitDir);
        Task<Dictionary<string, UpstreamTrack>> tracking = Task.Run(() => ReadTracking(module));
        (IReadOnlyList<RefEntry> entries, string current) = await Task.Run(() =>
        {
            List<RefEntry> read = ReadEntries(module);
            read.AddRange(ReadSubmodulesAndStashes(module));
            return ((IReadOnlyList<RefEntry>)read, module.GetSelectedBranch(emptyIfDetached: true));
        });
        Dictionary<string, UpstreamTrack> tracks = await tracking;
        if (!ReferenceEquals(_module, module))
        {
            return;
        }

        _entries = ApplyTracking(entries, tracks);
        _currentBranch = current;
        CurrentTrack = tracks.GetValueOrDefault(current);
        UpdateLocalBranchNames();
        Rebuild();
    }

    /// <summary>Trees with more refs than this are rebuilt after a short pause in typing instead of on every keystroke.</summary>
    public const int DebounceThreshold = 500;

    public static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(150);

    partial void OnFilterChanged(string value)
    {
        _filterDebounce?.Cancel();
        if (_entries.Count <= DebounceThreshold)
        {
            Rebuild();
            return;
        }

        // Rebuilding thousands of tree nodes per keystroke makes typing lag; wait until typing pauses.
        CancellationTokenSource debounce = new();
        _filterDebounce = debounce;
        _ = RebuildAfterPauseAsync(debounce.Token);
    }

    private async Task RebuildAfterPauseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DebounceDelay, cancellationToken);
            Rebuild();
        }
        catch (OperationCanceledException)
        {
            // Another keystroke arrived.
        }
    }

    partial void OnSelectedNodeChanged(RefNodeViewModel? value)
    {
        if (value?.IsLeaf == true && value.ObjectId is ObjectId id)
        {
            _selectCommit(id);
        }
    }

    [RelayCommand]
    private Task CheckoutAsync(RefNodeViewModel? node)
        => node is { Kind: RefNodeKind.LocalBranch or RefNodeKind.Tag } ? CheckoutRefAsync(node.FullName) : Task.CompletedTask;

    /// <summary>
    ///  Checks out a local branch or tag by name (the toolbar's branch list uses this) and reloads everything.
    /// </summary>
    public async Task CheckoutRefAsync(string name)
    {
        GitModule? module = _module;
        if (module is null)
        {
            return;
        }

        _reportStatus($"Checking out {name}…");
        try
        {
            // A tag (or any non-branch name) makes git detach HEAD on its own.
            ExecutionResult result = await Task.Run(() => module.GitExecutable.Execute(
                Commands.CheckoutBranch(name, remote: false).Arguments,
                throwOnErrorExit: false));

            if (!result.ExitedSuccessfully)
            {
                string error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
                _reportStatus($"Checkout of {name} failed: {error.Trim()}");
                return;
            }
        }
        catch (Exception ex)
        {
            _reportStatus($"Checkout of {name} failed: {ex.Message}");
            return;
        }

        await _refreshAll();
    }

    private void Rebuild()
    {
        Roots.Clear();
        foreach (RefNodeViewModel root in RefsTreeBuilder.Build(_entries, _currentBranch, Filter, _remoteHosts))
        {
            Roots.Add(root);
        }
    }

    // Cheap lookups for the "Submodules" and "Stashes" groups: .gitmodules (top level only) and the stash list.
    // "git-extensions-linux (main)" for every working tree, like the original; the one this window shows is the current one (bold).
    private static List<RefEntry> ReadWorktrees(GitModule module)
    {
        List<RefEntry> worktrees = [];
        ExecutionResult result = module.GitExecutable.Execute("worktree list --porcelain", throwOnErrorExit: false);
        if (!result.ExitedSuccessfully)
        {
            return worktrees;
        }

        static string Normalize(string path) => Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar)).TrimEnd(Path.DirectorySeparatorChar);
        string current = Normalize(module.WorkingDir);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string? path = null;
        string? head = null;

        void Flush()
        {
            if (path is null)
            {
                return;
            }

            string name = Path.GetFileName(Normalize(path));
            worktrees.Add(new RefEntry(RefNodeKind.Worktree, head is null ? name : $"{name} ({head})", IsCurrent: string.Equals(Normalize(path), current, comparison)));
            path = null;
            head = null;
        }

        foreach (string raw in result.StandardOutput.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                Flush();
                path = line["worktree ".Length..];
            }
            else if (line.StartsWith("branch refs/heads/", StringComparison.Ordinal))
            {
                head = line["branch refs/heads/".Length..];
            }
            else if (line == "detached")
            {
                head = "detached HEAD";
            }
            else if (line == "bare")
            {
                head = "bare";
            }
        }

        Flush();
        return worktrees;
    }

    private static List<RefEntry> ReadSubmodulesAndStashes(GitModule module)
    {
        List<RefEntry> entries = [];
        try
        {
            ExecutionResult modules = module.GitExecutable.Execute("config -f .gitmodules --get-regexp \"^submodule\\..*\\.path$\"", throwOnErrorExit: false);
            if (modules.ExitedSuccessfully)
            {
                foreach (string line in modules.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    int space = line.IndexOf(' ');
                    if (space > 0)
                    {
                        entries.Add(new RefEntry(RefNodeKind.Submodule, line[(space + 1)..].Trim()));
                    }
                }
            }

            entries.AddRange(ReadWorktrees(module));

            ExecutionResult stashes = module.GitExecutable.Execute("stash list", throwOnErrorExit: false);
            if (stashes.ExitedSuccessfully)
            {
                foreach (string line in stashes.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    entries.Add(new RefEntry(RefNodeKind.Stash, line));
                }
            }
        }
        catch (Exception)
        {
            // The groups are a convenience: never fail the sidebar for them.
        }

        return entries;
    }

    private static List<RefEntry> ReadEntries(GitModule module)
    {
        List<RefEntry> entries = [];
        foreach (IGitRef gitRef in module.GetRefs(RefsFilter.Heads | RefsFilter.Tags | RefsFilter.Remotes))
        {
            if (gitRef.IsTag)
            {
                entries.Add(new RefEntry(RefNodeKind.Tag, gitRef.LocalName, gitRef.ObjectId));
            }
            else if (gitRef.IsRemote)
            {
                // "refs/remotes/origin/main" -> "origin/main"
                string name = gitRef.CompleteName["refs/remotes/".Length..];
                if (!name.EndsWith("/HEAD", StringComparison.Ordinal))
                {
                    entries.Add(new RefEntry(RefNodeKind.RemoteBranch, name, gitRef.ObjectId));
                }
            }
            else if (gitRef.IsHead)
            {
                entries.Add(new RefEntry(RefNodeKind.LocalBranch, gitRef.LocalName, gitRef.ObjectId));
            }
        }

        return entries;
    }
}
