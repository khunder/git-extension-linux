using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>A branch or tag as read from the repository.</summary>
public sealed record RefEntry(RefNodeKind Kind, string Name, ObjectId? ObjectId = null, UpstreamTrack? Track = null);

/// <summary>
///  Pure grouping/filter logic for the refs sidebar: no git, no UI thread.
///  <see cref="RefEntry.Name"/> is the name git understands: <c>feature/x</c>, <c>origin/main</c>, <c>v1.0</c>.
/// </summary>
public static class RefsTreeBuilder
{
    public static IReadOnlyList<RefNodeViewModel> Build(IEnumerable<RefEntry> entries, string? currentBranch, string? filter)
    {
        string text = filter?.Trim() ?? string.Empty;
        bool filtering = text.Length > 0;
        List<RefEntry> matching = entries
            .Where(entry => !filtering || entry.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            .ToList();

        List<RefNodeViewModel> roots = [];
        AddGroup(roots, "Branches", matching.Where(e => e.Kind == RefNodeKind.LocalBranch), currentBranch, filtering, splitRemote: false);
        AddGroup(roots, "Remotes", matching.Where(e => e.Kind == RefNodeKind.RemoteBranch), currentBranch, filtering, splitRemote: true);
        AddGroup(roots, "Tags", matching.Where(e => e.Kind == RefNodeKind.Tag), currentBranch, filtering, splitRemote: false);
        return roots;
    }

    private static void AddGroup(List<RefNodeViewModel> roots, string title, IEnumerable<RefEntry> entries, string? currentBranch, bool filtering, bool splitRemote)
    {
        List<RefEntry> list = entries.ToList();

        // While filtering, groups without hits disappear; otherwise empty groups stay as a hint.
        if (filtering && list.Count == 0)
        {
            return;
        }

        RefNodeViewModel group = new(title, RefNodeKind.Group);
        if (splitRemote)
        {
            foreach (IGrouping<string, RefEntry> remote in list
                .GroupBy(e => e.Name[..Math.Max(e.Name.IndexOf('/'), 0)])
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                RefNodeViewModel remoteNode = new(remote.Key, RefNodeKind.Folder) { IsRemoteRoot = true };
                AddTree(remoteNode, remote, currentBranch: null, prefixLength: remote.Key.Length + 1);
                group.Children.Add(remoteNode);
            }
        }
        else
        {
            AddTree(group, list, currentBranch, prefixLength: 0);
        }

        roots.Add(group);
    }

    private static void AddTree(RefNodeViewModel parent, IEnumerable<RefEntry> entries, string? currentBranch, int prefixLength)
    {
        Dictionary<string, RefNodeViewModel> folders = [];

        foreach (RefEntry entry in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            string[] segments = entry.Name[Math.Min(prefixLength, entry.Name.Length)..].Split('/');
            RefNodeViewModel container = parent;
            string path = string.Empty;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                path += segments[i] + "/";
                if (!folders.TryGetValue(path, out RefNodeViewModel? folder))
                {
                    folder = new RefNodeViewModel(segments[i], RefNodeKind.Folder);
                    folders[path] = folder;
                    container.Children.Add(folder);
                }

                container = folder;
            }

            bool isCurrent = entry.Kind == RefNodeKind.LocalBranch && entry.Name == currentBranch;
            container.Children.Add(new RefNodeViewModel(segments[^1], entry.Kind, entry.Name, entry.ObjectId, isCurrent) { Track = entry.Track });
        }

        SortFoldersFirst(parent);
    }

    private static void SortFoldersFirst(RefNodeViewModel node)
    {
        List<RefNodeViewModel> sorted = node.Children
            .OrderBy(child => child.IsLeaf ? 1 : 0)
            .ThenBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        node.Children.Clear();
        foreach (RefNodeViewModel child in sorted)
        {
            node.Children.Add(child);
            SortFoldersFirst(child);
        }
    }
}
