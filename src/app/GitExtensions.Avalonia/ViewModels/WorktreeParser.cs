namespace GitExtensions.Desktop.ViewModels;

/// <summary>One record of <c>git worktree list --porcelain</c>.</summary>
public sealed record WorktreeInfo(string Path, string? Head, string? Branch, bool IsDetached, bool IsBare, bool IsMain, bool IsLocked, string? LockReason, bool IsPrunable, string? PruneReason)
{
    public string ShortHead => Head is { Length: > 8 } ? Head[..8] : Head ?? string.Empty;

    public string BranchText => IsBare ? "(bare)" : IsDetached ? "(detached)" : Branch ?? "(none)";

    public string Flags => string.Join(", ", new[] { IsMain ? "main" : null, IsLocked ? "locked" : null, IsPrunable ? "prunable" : null }.Where(f => f is not null));
}

public static class WorktreeParser
{
    public static IReadOnlyList<WorktreeInfo> Parse(string output)
    {
        List<WorktreeInfo> result = [];
        string? path = null, head = null, branch = null, lockReason = null, pruneReason = null;
        bool detached = false, bare = false, locked = false, prunable = false;

        void Flush()
        {
            if (path is not null)
            {
                result.Add(new WorktreeInfo(path, head, branch, detached, bare, result.Count == 0, locked, lockReason, prunable, pruneReason));
            }

            path = head = branch = lockReason = pruneReason = null;
            detached = bare = locked = prunable = false;
        }

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            int space = line.IndexOf(' ');
            string key = space < 0 ? line : line[..space];
            string value = space < 0 ? string.Empty : line[(space + 1)..];
            switch (key)
            {
                case "worktree":
                    Flush();
                    path = value;
                    break;
                case "HEAD":
                    head = value;
                    break;
                case "branch":
                    branch = value.StartsWith("refs/heads/", StringComparison.Ordinal) ? value["refs/heads/".Length..] : value;
                    break;
                case "detached":
                    detached = true;
                    break;
                case "bare":
                    bare = true;
                    break;
                case "locked":
                    locked = true;
                    lockReason = value.Length > 0 ? value : null;
                    break;
                case "prunable":
                    prunable = true;
                    pruneReason = value.Length > 0 ? value : null;
                    break;
            }
        }

        Flush();
        return result;
    }
}
