using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitCommands.Git;
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
        Roots.Clear();
    }

    /// <summary>
    ///  Reads branches, remote branches and tags (off the UI thread) and rebuilds the tree.
    /// </summary>
    public async Task LoadAsync(GitModule module)
    {
        _module = module;
        (IReadOnlyList<RefEntry> entries, string current) = await Task.Run(() => (ReadEntries(module), module.GetSelectedBranch(emptyIfDetached: true)));
        if (!ReferenceEquals(_module, module))
        {
            return;
        }

        _entries = entries;
        _currentBranch = current;
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
    private async Task CheckoutAsync(RefNodeViewModel? node)
    {
        GitModule? module = _module;
        if (module is null || node is null || node.Kind is not (RefNodeKind.LocalBranch or RefNodeKind.Tag))
        {
            return;
        }

        _reportStatus($"Checking out {node.FullName}…");
        try
        {
            // A tag (or any non-branch name) makes git detach HEAD on its own.
            ExecutionResult result = await Task.Run(() => module.GitExecutable.Execute(
                Commands.CheckoutBranch(node.FullName, remote: false).Arguments,
                throwOnErrorExit: false));

            if (!result.ExitedSuccessfully)
            {
                string error = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
                _reportStatus($"Checkout of {node.FullName} failed: {error.Trim()}");
                return;
            }
        }
        catch (Exception ex)
        {
            _reportStatus($"Checkout of {node.FullName} failed: {ex.Message}");
            return;
        }

        await _refreshAll();
    }

    private void Rebuild()
    {
        Roots.Clear();
        foreach (RefNodeViewModel root in RefsTreeBuilder.Build(_entries, _currentBranch, Filter))
        {
            Roots.Add(root);
        }
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
