using CommunityToolkit.Mvvm.ComponentModel;
using GitExtensions.Desktop.Revision;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>A folder or file in the file tree tab. Folders create their children when first expanded.</summary>
public sealed partial class FileTreeNode : ObservableObject
{
    private readonly TreeEntry? _entry;
    private bool _populated;

    [ObservableProperty]
    private bool _isExpanded;

    private FileTreeNode(TreeEntry? entry, bool eager)
    {
        _entry = entry;
        if (entry is null)
        {
            // The placeholder child that makes a folder expandable before its content exists.
            Name = string.Empty;
            FullPath = string.Empty;
            return;
        }

        // Like the original, a folder whose only child is a folder is shown as one node (".github/workflows").
        string name = entry.Name;
        TreeEntry shown = entry;
        while (shown.IsFolder && shown.ChildCount == 1 && shown.Children.First() is { IsFolder: true } only)
        {
            name += "/" + only.Name;
            shown = only;
        }

        _entry = shown;
        Name = name;
        FullPath = shown.Path;
        IsFolder = shown.IsFolder;
        if (IsFolder)
        {
            if (eager)
            {
                Populate(eager: true);
                _isExpanded = true;
            }
            else if (shown.ChildCount > 0)
            {
                Children.Add(new FileTreeNode(null, eager: false));
            }
        }
    }

    public string Name { get; }

    public string FullPath { get; }

    public bool IsFolder { get; }

    public bool IsPlaceholder => _entry is null;

    /// <summary>The icon of the entry: an open or closed folder, or a file.</summary>
    public string Icon => IsFolder ? (IsExpanded ? "FolderOpen" : "FolderClosed") : "ext:" + System.IO.Path.GetExtension(Name);

    public RangeObservableCollection<FileTreeNode> Children { get; } = [];

    /// <summary>The top level nodes of <paramref name="root"/>; with <paramref name="eager"/> everything is created and expanded (for filtered trees).</summary>
    public static IReadOnlyList<FileTreeNode> CreateRoots(TreeEntry root, bool eager)
        => [.. root.Children.Select(child => new FileTreeNode(child, eager))];

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(Icon));
        if (value)
        {
            Populate(eager: false);
        }
    }

    private void Populate(bool eager)
    {
        if (_populated || _entry is null)
        {
            return;
        }

        _populated = true;
        Children.ReplaceAll(_entry.Children.Select(child => new FileTreeNode(child, eager)));
    }
}
