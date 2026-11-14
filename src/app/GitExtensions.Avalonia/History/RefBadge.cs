namespace GitExtensions.Desktop.History;

public enum RefBadgeKind
{
    LocalBranch,
    RemoteBranch,
    Tag,

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

    /// <summary>For the overflow chip: the full list of refs.</summary>
    public string? ToolTip { get; init; }
}

public static class RefBadgeBuilder
{
    public const int DefaultMaxVisible = 3;

    /// <summary>
    ///  Orders the badges (current branch first, then local branches, remote branches, tags; names alphabetical) and,
    ///  when there are more than <paramref name="maxVisible"/>, replaces the rest by one "…" chip with a tooltip listing every ref.
    /// </summary>
    public static IReadOnlyList<RefBadge> Arrange(IEnumerable<RefBadge> badges, int maxVisible = DefaultMaxVisible)
    {
        List<RefBadge> ordered = badges
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
