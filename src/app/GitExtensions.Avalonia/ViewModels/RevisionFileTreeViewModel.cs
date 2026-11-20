using CommunityToolkit.Mvvm.ComponentModel;
using GitCommands;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.Revision;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The "File tree" tab: the repository tree at a revision and the content of the selected file. Read only.
/// </summary>
public sealed partial class RevisionFileTreeViewModel : ObservableObject
{
    private const int MaxFilterMatches = 2000;

    private int _running;
    private IReadOnlyList<string> _paths = [];
    private TreeEntry? _root;
    private GitModule? _module;
    private string? _pendingRevision;
    private string? _openFilePath;
    private CancellationTokenSource? _treeCancellation;
    private CancellationTokenSource? _contentCancellation;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private FileTreeNode? _selectedNode;

    [ObservableProperty]
    private string _message = "Select a commit to browse its files.";

    [ObservableProperty]
    private string _contentMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Gets a value indicating whether a load (tree or file content) is still running.</summary>
    public bool IsBusy => _running > 0;

    public RangeObservableCollection<FileTreeNode> Roots { get; } = [];

    public RangeObservableCollection<ContentLine> Lines { get; } = [];

    /// <summary>The revision shown (or about to be shown, when the tab is not active yet).</summary>
    public string? Revision { get; private set; }

    /// <summary>Gets or sets whether the tab is the visible one; the tree of a pending revision is only listed once it is.</summary>
    public bool IsActive
    {
        get;
        set
        {
            field = value;
            if (value && _pendingRevision is not null && _module is not null)
            {
                _ = ShowRevisionAsync(_module, _pendingRevision);
            }
        }
    }

    /// <summary>Remembers the revision; the (potentially big) listing happens when the tab is active.</summary>
    public void SetRevision(GitModule module, string revision)
    {
        _module = module;
        _pendingRevision = revision;
        Revision = revision;
        if (IsActive)
        {
            _ = ShowRevisionAsync(module, revision);
        }
        else
        {
            ResetView();
        }
    }

    /// <summary>Selects (and opens) <paramref name="path"/> in the tree, now or once the tree is listed.</summary>
    public void Reveal(string path)
    {
        _openFilePath = path;
        if (_root is not null && _pendingRevision is null && !IsLoading)
        {
            ReopenFile();
        }
    }

    public void Clear()
    {
        _pendingRevision = null;
        _openFilePath = null;
        Revision = null;
        _module = null;
        CancelAll();
        ResetView();
        IsLoading = false;
    }

    /// <summary>Lists the tree of <paramref name="revision"/> now.</summary>
    public async Task ShowRevisionAsync(GitModule module, string revision)
    {
        CancelAll();
        CancellationTokenSource cancellation = new();
        _treeCancellation = cancellation;
        CancellationToken cancellationToken = cancellation.Token;

        _module = module;
        _pendingRevision = null;
        Revision = revision;
        ResetView();
        IsLoading = true;
        Message = "Loading…";

        _running++;
        try
        {
            (IReadOnlyList<string> paths, bool truncated, string? error) = await Task.Run(() => RevisionGit.ListTree(module, revision, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Building the tree is CPU work proportional to the number of files.
            TreeEntry root = await Task.Run(() => TreeEntry.Build(paths), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            _paths = paths;
            _root = root;
            Message = error ?? (truncated ? $"Showing the first {paths.Count:N0} files." : paths.Count == 0 ? "The tree is empty." : string.Empty);
            IsLoading = false;
            ApplyFilter();
            ReopenFile();
        }
        catch (OperationCanceledException)
        {
            // A newer selection replaced this one.
        }
        catch (Exception ex)
        {
            Message = $"Failed to list the files: {ex.Message}";
            IsLoading = false;
        }
        finally
        {
            _running--;
        }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnSelectedNodeChanged(FileTreeNode? value)
    {
        if (value is { IsFolder: false, IsPlaceholder: false })
        {
            _openFilePath = value.FullPath;
        }

        _ = ShowContentAsync(value);
    }

    /// <summary>After another commit was selected, opens the file that was open before so the two versions can be compared.</summary>
    private void ReopenFile()
    {
        if (_openFilePath is null)
        {
            return;
        }

        IReadOnlyList<FileTreeNode> level = Roots;
        FileTreeNode? found = null;
        foreach (string segment in _openFilePath.Split('/'))
        {
            found = level.FirstOrDefault(node => !node.IsPlaceholder && node.Name == segment);
            if (found is null)
            {
                break;
            }

            if (found.IsFolder)
            {
                found.IsExpanded = true;
                level = found.Children;
            }
        }

        if (found is { IsFolder: false })
        {
            SelectedNode = found;
        }
        else
        {
            ContentMessage = $"'{_openFilePath}' does not exist in this commit.";
        }
    }

    private void ApplyFilter()
    {
        if (_root is null)
        {
            return;
        }

        string filter = FilterText.Trim();
        if (filter.Length == 0)
        {
            Roots.ReplaceAll(FileTreeNode.CreateRoots(_root, eager: false));
            return;
        }

        List<string> matches = [.. _paths.Where(path => path.Contains(filter, StringComparison.OrdinalIgnoreCase)).Take(MaxFilterMatches + 1)];
        bool tooMany = matches.Count > MaxFilterMatches;
        if (tooMany)
        {
            matches.RemoveAt(matches.Count - 1);
        }

        Roots.ReplaceAll(FileTreeNode.CreateRoots(TreeEntry.Build(matches), eager: true));
        Message = matches.Count == 0 ? "No file matches the filter." : tooMany ? $"Showing the first {MaxFilterMatches:N0} matches." : string.Empty;
    }

    private async Task ShowContentAsync(FileTreeNode? node)
    {
        if (_contentCancellation is not null)
        {
            await _contentCancellation.CancelAsync();
        }

        CancellationTokenSource cancellation = new();
        _contentCancellation = cancellation;
        Lines.Clear();
        ContentMessage = string.Empty;
        if (node is not { IsFolder: false, IsPlaceholder: false } || _module is null || Revision is null)
        {
            return;
        }

        GitModule module = _module;
        string revision = Revision;
        ContentMessage = "Loading…";
        _running++;
        try
        {
            FileContentResult content = await Task.Run(() => RevisionGit.ReadFile(module, revision, node.FullPath, cancellation.Token), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            Lines.ReplaceAll(content.Lines.Select((text, index) => new ContentLine(index + 1, text)));
            ContentMessage = content.Message;
        }
        catch (OperationCanceledException)
        {
            // A newer file replaced this one.
        }
        catch (Exception ex)
        {
            ContentMessage = $"Failed to read the file: {ex.Message}";
        }
        finally
        {
            _running--;
        }
    }

    private void ResetView()
    {
        _root = null;
        _paths = [];
        SelectedNode = null;
        Roots.Clear();
        Lines.Clear();
        ContentMessage = string.Empty;
        Message = Revision is null ? "Select a commit to browse its files." : string.Empty;
    }

    private void CancelAll()
    {
        _treeCancellation?.Cancel();
        _contentCancellation?.Cancel();
    }
}
