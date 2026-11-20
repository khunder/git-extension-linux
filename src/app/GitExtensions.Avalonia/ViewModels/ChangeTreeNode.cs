using CommunityToolkit.Mvvm.ComponentModel;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>A folder or a changed file in the tree layout of the Commit window's file lists.</summary>
public sealed partial class ChangeTreeNode : ObservableObject
{
    private readonly ISet<string>? _collapsed;
    private readonly string _stateKey;
    private readonly string? _label;
    private readonly string? _icon;

    [ObservableProperty]
    private bool _isExpanded = true;

    private ChangeTreeNode(string name, string fullPath, ChangedFileViewModel? file, ISet<string>? collapsed, string stateKey, string? label = null, string? icon = null)
    {
        _label = label;
        _icon = icon;
        Name = name;
        FullPath = fullPath;
        File = file;
        _collapsed = collapsed;
        _stateKey = stateKey;
        _isExpanded = file is not null || collapsed is null || !collapsed.Contains(stateKey);
    }

    /// <summary>The folder name or the file name.</summary>
    public string Name { get; }

    /// <summary>The path relative to the working directory, with forward slashes.</summary>
    public string FullPath { get; }

    /// <summary>The changed file; <see langword="null"/> for folders.</summary>
    public ChangedFileViewModel? File { get; }

    public bool IsFolder => File is null;

    public bool IsFile => File is not null;

    public string Status => File?.Status ?? string.Empty;

    /// <summary>The icon of the change for files; folders use the folder icon.</summary>
    public string StatusIcon => File?.StatusIcon ?? _icon ?? "FolderClosed";

    public string Description => File?.Description ?? string.Empty;

    public bool IsConflicted => File?.IsConflicted ?? false;

    /// <summary>Folders show how many changed files they contain.</summary>
    public string Label => _label ?? (IsFolder ? $"{Name} ({Files().Count()})" : Name);

    /// <summary>The name without the count of files, as the Diff tab shows folders.</summary>
    public string PlainLabel => _label ?? Name;

    /// <summary>Gets a value indicating whether this is a group of the tree (the header, an extension) rather than a folder of the repository.</summary>
    public bool IsGroup => _label is not null || _icon is not null;

    public List<ChangeTreeNode> Children { get; } = [];

    /// <summary>The changed files of this node: the file itself, or every file below a folder.</summary>
    public IEnumerable<ChangedFileViewModel> Files()
    {
        if (File is not null)
        {
            yield return File;
            yield break;
        }

        foreach (ChangeTreeNode child in Children)
        {
            foreach (ChangedFileViewModel file in child.Files())
            {
                yield return file;
            }
        }
    }

    /// <summary>
    ///  Builds the folder hierarchy of <paramref name="files"/>: folders first, then files, both sorted by name.
    ///  <paramref name="collapsed"/> remembers the folders (keyed with <paramref name="side"/>) that the user collapsed, so a
    ///  refresh does not expand them again.
    /// </summary>
    public static List<ChangeTreeNode> Build(IEnumerable<ChangedFileViewModel> files, ISet<string>? collapsed = null, string side = "")
    {
        ChangeTreeNode root = new(string.Empty, string.Empty, null, null, string.Empty);
        Dictionary<string, ChangeTreeNode> folders = new(StringComparer.Ordinal);
        foreach (ChangedFileViewModel file in files)
        {
            ChangeTreeNode parent = root;
            string[] parts = file.Name.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string path = string.Empty;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                path = path.Length == 0 ? parts[i] : path + "/" + parts[i];
                if (!folders.TryGetValue(path, out ChangeTreeNode? folder))
                {
                    folder = new ChangeTreeNode(parts[i], path, null, collapsed, side + ":" + path);
                    folders[path] = folder;
                    parent.Children.Add(folder);
                }

                parent = folder;
            }

            parent.Children.Add(new ChangeTreeNode(parts.Length == 0 ? file.Name : parts[^1], file.Name, file, collapsed, side + ":" + file.Name));
        }

        Sort(root);
        Compact(root, collapsed, side);
        return root.Children;
    }

    /// <summary>
    ///  Like the original's "group by extension": one node per extension (".cs", ".axaml", ...; files without one last),
    ///  each holding the folder tree of its files.
    /// </summary>
    public static List<ChangeTreeNode> BuildByExtension(IEnumerable<ChangedFileViewModel> files, ISet<string>? collapsed = null, string side = "")
    {
        List<ChangeTreeNode> groups = [];
        foreach (IGrouping<string, ChangedFileViewModel> group in files
            .GroupBy(file => System.IO.Path.GetExtension(file.Name).ToLowerInvariant())
            .OrderBy(group => group.Key.Length == 0 ? 1 : 0)
            .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            string name = group.Key.Length == 0 ? "(no extension)" : group.Key;
            string key = side + ":ext:" + name;
            ChangeTreeNode node = new(name, string.Empty, null, collapsed, key, name, group.Key.Length == 0 ? "File" : "ext:" + group.Key);
            node.Children.AddRange(Build(group, collapsed, key));
            groups.Add(node);
        }

        return groups;
    }

    /// <summary>The top node of the original's diff list: "(35) Diff with A f698c68f: subject", holding the other nodes.</summary>
    public static ChangeTreeNode Header(string text, IEnumerable<ChangeTreeNode> children)
    {
        ChangeTreeNode header = new(text, string.Empty, null, null, ":header", text, "Diff");
        header.Children.AddRange(children);
        return header;
    }

    // Like the original, a folder whose only child is another folder is shown as one node ("src/app/Core" instead of three levels).
    private static void Compact(ChangeTreeNode node, ISet<string>? collapsed, string side)
    {
        for (int i = 0; i < node.Children.Count; i++)
        {
            ChangeTreeNode child = node.Children[i];
            while (child.IsFolder && !child.IsGroup && child.Children.Count == 1 && child.Children[0].IsFolder && !child.Children[0].IsGroup)
            {
                ChangeTreeNode only = child.Children[0];
                ChangeTreeNode merged = new(child.Name + "/" + only.Name, only.FullPath, null, collapsed, side + ":" + only.FullPath);
                merged.Children.AddRange(only.Children);
                child = merged;
            }

            node.Children[i] = child;
            Compact(child, collapsed, side);
        }
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (IsFolder && _collapsed is not null)
        {
            if (value)
            {
                _collapsed.Remove(_stateKey);
            }
            else
            {
                _collapsed.Add(_stateKey);
            }
        }
    }

    private static void Sort(ChangeTreeNode node)
    {
        node.Children.Sort((a, b) => a.IsFolder != b.IsFolder
            ? (a.IsFolder ? -1 : 1)
            : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (ChangeTreeNode child in node.Children)
        {
            Sort(child);
        }
    }
}
