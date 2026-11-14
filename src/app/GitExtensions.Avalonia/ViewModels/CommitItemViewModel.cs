using System.Globalization;
using Avalonia.Media;
using GitExtensions.Desktop.History;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitExtensions.Desktop.ViewModels;

public sealed class CommitItemViewModel
{
    public CommitItemViewModel(GitRevision revision, string refs, IReadOnlyList<RefBadge>? badges = null)
    {
        Revision = revision;
        ShortHash = revision.ObjectId.ToShortString();
        Subject = revision.Subject;
        Author = revision.Author ?? string.Empty;
        Date = revision.AuthorDate.ToString("g", CultureInfo.CurrentCulture);
        Refs = refs;
        RefBadges = badges ?? [];
        RelativeDate = RelativeDateFormatter.Format(revision.AuthorDate);
        DateToolTip = RelativeDateFormatter.Absolute(revision.AuthorDate);
        IsMerge = revision.ParentIds?.Count > 1;
        Initials = AvatarStyle.Initials(Author);
        AvatarColorIndex = AvatarStyle.ColorIndex(Author);
        IsCurrentHead = RefBadges.Any(badge => badge.IsCurrent);
    }

    public GitRevision Revision { get; }

    /// <summary>Graph cell data, set when the commit is added to the history.</summary>
    public GitExtensions.Desktop.History.CommitGraphRow? Graph { get; set; }

    /// <summary>The context menu operations (shared by all rows).</summary>
    public CommitActionsViewModel? Actions { get; set; }

    public ObjectId ObjectId => Revision.ObjectId;

    public string ShortHash { get; }

    public string Subject { get; }

    public string Author { get; }

    public string Date { get; }

    /// <summary>
    ///  Branches and tags pointing at this commit.
    /// </summary>
    public string Refs { get; }

    public bool HasRefs => Refs.Length > 0;

    /// <summary>The labels shown next to the subject (at most a few plus a "…" chip), see <see cref="RefBadgeBuilder"/>.</summary>
    public IReadOnlyList<RefBadge> RefBadges { get; }

    public bool HasRefBadges => RefBadges.Count > 0;

    /// <summary>"15 days ago"; the absolute date is <see cref="DateToolTip"/>.</summary>
    public string RelativeDate { get; }

    public string DateToolTip { get; }

    public bool IsMerge { get; }

    /// <summary>The commit the current branch points at.</summary>
    public bool IsCurrentHead { get; }

    /// <summary>True for commits whose author is the configured git user (<c>user.name</c>); shown bold.</summary>
    public bool IsMine { get; init; }

    /// <summary>Every other row gets a subtle background tint.</summary>
    public bool IsAlternate { get; init; }

    public string Initials { get; }

    public int AvatarColorIndex { get; }

    public IBrush AvatarBrush => Views.AvatarBrushes.For(AvatarColorIndex);

    /// <summary>The build server's verdict for the commit, if known; the grid's status column stays empty without one.</summary>
    public BuildStatus BuildStatus { get; init; }

    public bool HasBuildStatus => BuildStatus != BuildStatus.None;

    public bool IsBuildSuccess => BuildStatus == BuildStatus.Success;

    public bool IsBuildFailure => BuildStatus == BuildStatus.Failure;

    public bool IsBuildRunning => BuildStatus == BuildStatus.Running;

    /// <summary>What a screen reader announces for the row.</summary>
    public string AccessibleName => HasRefs ? $"{Subject}, by {Author}, {Date}, {ShortHash}, {Refs}" : $"{Subject}, by {Author}, {Date}, {ShortHash}";
}

public enum BuildStatus
{
    None,
    Success,
    Failure,
    Running,
}
