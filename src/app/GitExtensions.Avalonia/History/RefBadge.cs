namespace GitExtensions.Desktop.History;

public enum RefBadgeKind
{
    LocalBranch,
    RemoteBranch,
    Tag,

    /// <summary>A stash entry ("stash@{0}"), shown on its commit when the history includes stashes.</summary>
    Stash,

    /// <summary>The "…" chip that stands for the badges that did not fit.</summary>
    Overflow,
}

/// <summary>One label next to a commit subject: a branch or a tag.</summary>
public sealed record RefBadge(RefBadgeKind Kind, string Name, bool IsCurrent)
{
    /// <summary>The text of the label ("origin/main" for remote branches).</summary>
    public string Text => Name;

    public bool IsOverflow => Kind == RefBadgeKind.Overflow;

    public bool IsLocal => Kind == RefBadgeKind.LocalBranch;

    public bool IsRemote => Kind == RefBadgeKind.RemoteBranch;

    public bool IsTag => Kind == RefBadgeKind.Tag;

    public bool IsStash => Kind == RefBadgeKind.Stash;

    /// <summary>For a local branch whose remote-tracking branch is on the same commit: the remote name(s), e.g. "origin", shown joined to the label.</summary>
    public string? RemoteSuffix { get; init; }

    public bool HasRemoteSuffix => !string.IsNullOrEmpty(RemoteSuffix);

    /// <summary>A remote-tracking branch of the checked out branch (same name on the remote): it gets the small arrow like the current branch.</summary>
    public bool IsCurrentUpstream { get; init; }

    /// <summary>For the overflow chip: the full list of refs.</summary>
    public string? ToolTip { get; init; }
}

public static class RefBadgeBuilder
{
    public const int DefaultMaxVisible = 3;

    /// <summary>
    ///  Like the original: "develop" and "origin/develop" on the same commit become one label, "develop" with "origin" joined to it.
    ///  Remote branches without a local branch of the same name stay separate.
    /// </summary>
    public static IReadOnlyList<RefBadge> JoinRemotes(IEnumerable<RefBadge> badges)
    {
        List<RefBadge> all = [.. badges];
        List<RefBadge> result = [];
        HashSet<RefBadge> joined = [];
        foreach (RefBadge local in all.Where(b => b.IsLocal))
        {
            List<RefBadge> remotes = [.. all.Where(r => r.IsRemote && r.Name.IndexOf('/') is var slash and > 0 && string.Equals(r.Name[(slash + 1)..], local.Name, StringComparison.Ordinal))];
            foreach (RefBadge remote in remotes)
            {
                _ = joined.Add(remote);
            }

            result.Add(remotes.Count == 0 ? local : local with { RemoteSuffix = string.Join(", ", remotes.Select(r => r.Name[..r.Name.IndexOf('/')])) });
        }

        result.AddRange(all.Where(b => !b.IsLocal && !joined.Contains(b)));
        return result;
    }

    /// <summary>
    ///  Orders the badges (current branch first, then local branches, remote branches, tags; names alphabetical) and,
    ///  when there are more than <paramref name="maxVisible"/>, replaces the rest by one "…" chip with a tooltip listing every ref.
    /// </summary>
    public static IReadOnlyList<RefBadge> Arrange(IEnumerable<RefBadge> badges, int maxVisible = DefaultMaxVisible)
    {
        List<RefBadge> ordered = JoinRemotes(badges)
            .OrderByDescending(b => b.IsCurrent)
            .ThenBy(b => b.Kind)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ordered.Count <= maxVisible)
        {
            return ordered;
        }

        List<RefBadge> shown = ordered.Take(maxVisible).ToList();
        shown.Add(new RefBadge(RefBadgeKind.Overflow, "…", false) { ToolTip = string.Join(Environment.NewLine, ordered.Select(b => b.Name)) });
        return shown;
    }
}
