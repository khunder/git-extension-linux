namespace GitExtensions.Desktop.Revision;

/// <summary>A folder or file of the repository tree; built from the flat path list of <c>git ls-tree -r</c>.</summary>
public sealed class TreeEntry
{
    private readonly Dictionary<string, TreeEntry> _children = new(StringComparer.Ordinal);

    private TreeEntry(string name, string path, bool isFolder)
    {
        Name = name;
        Path = path;
        IsFolder = isFolder;
    }

    public string Name { get; }

    /// <summary>The slash separated path from the repository root.</summary>
    public string Path { get; }

    public bool IsFolder { get; }

    /// <summary>The children, folders first, each group sorted by name.</summary>
    public IEnumerable<TreeEntry> Children
        => _children.Values
            .OrderByDescending(child => child.IsFolder)
            .ThenBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(child => child.Name, StringComparer.Ordinal);

    public int ChildCount => _children.Count;

    public static TreeEntry Build(IEnumerable<string> paths)
    {
        TreeEntry root = new(string.Empty, string.Empty, isFolder: true);
        foreach (string path in paths)
        {
            string[] parts = path.Split('/');
            TreeEntry current = root;
            for (int i = 0; i < parts.Length; i++)
            {
                bool isFolder = i < parts.Length - 1;
                if (!current._children.TryGetValue(parts[i], out TreeEntry? child))
                {
                    child = new TreeEntry(parts[i], string.Join('/', parts.Take(i + 1)), isFolder);
                    current._children[parts[i]] = child;
                }

                current = child;
            }
        }

        return root;
    }
}
