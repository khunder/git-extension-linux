using System.Text;

namespace GitExtensions.Desktop.History;

public enum BranchFilterMode
{
    AllBranches,
    CurrentBranch,
    SpecificBranch,
}

public enum TextFilterMode
{
    CommitMessage,
    Author,
    Committer,
    HashOrRef,
    DiffContent,
}

/// <summary>What the revision grid shows: which refs are walked and which commits pass the text filter.</summary>
public sealed record RevisionFilter
{
    public BranchFilterMode Branches { get; init; } = BranchFilterMode.AllBranches;

    /// <summary>The local branch when <see cref="Branches"/> is <see cref="BranchFilterMode.SpecificBranch"/>.</summary>
    public string? Branch { get; init; }

    public bool ShowRemoteBranches { get; init; } = true;

    public bool ShowTags { get; init; } = true;

    public bool ShowMergeCommits { get; init; } = true;

    public string Text { get; init; } = string.Empty;

    public TextFilterMode TextMode { get; init; } = TextFilterMode.CommitMessage;

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    /// <summary>Only the commits that changed this repository-relative path ("Filter file in grid"), or null.</summary>
    public string? Path { get; init; }

    public bool HasPath => !string.IsNullOrWhiteSpace(Path);

    /// <summary>True when something narrows the list (the "Uncommitted changes" row is hidden then).</summary>
    public bool IsFiltering => HasText || HasPath || !ShowMergeCommits || Branches == BranchFilterMode.SpecificBranch;
}

/// <summary>Maps a <see cref="RevisionFilter"/> to the revision part of the <c>git log</c> command line.</summary>
public static class RevisionFilterArgs
{
    public static string Build(RevisionFilter filter, int maxCount, IEnumerable<string>? extraRevisions = null)
    {
        StringBuilder args = new();
        args.Append("--topo-order --max-count=").Append(maxCount);
        if (!filter.ShowMergeCommits)
        {
            args.Append(" --no-merges");
        }

        string text = filter.Text.Trim();
        if (text.Length > 0)
        {
            switch (filter.TextMode)
            {
                case TextFilterMode.CommitMessage:
                    args.Append(" -i --fixed-strings --all-match --grep=").Append(Quote(text));
                    break;
                case TextFilterMode.Author:
                    args.Append(" -i --fixed-strings --author=").Append(Quote(text));
                    break;
                case TextFilterMode.Committer:
                    args.Append(" -i --fixed-strings --committer=").Append(Quote(text));
                    break;
                case TextFilterMode.DiffContent:
                    args.Append(" -i -G").Append(Quote(text));
                    break;
                case TextFilterMode.HashOrRef:
                    // Shows the one commit the text names. Anything that cannot be a revision matches nothing.
                    return IsSafeRevision(text)
                        ? $"{args} --no-walk=unsorted {text}"
                        : $"{args} --max-count=0 HEAD";
            }
        }

        args.Append(' ').Append(Revisions(filter));
        if (extraRevisions is not null && filter.Branches == BranchFilterMode.AllBranches)
        {
            foreach (string revision in extraRevisions.Where(IsSafeRevision))
            {
                args.Append(' ').Append(revision);
            }
        }

        return args.ToString();
    }

    /// <summary>The path part of the command line (after git's <c>--</c>, which the reader adds), empty for no path filter.</summary>
    public static string PathSpec(RevisionFilter filter) => filter.HasPath ? Quote(filter.Path!.Trim()) : string.Empty;

    private static string Revisions(RevisionFilter filter)
    {
        switch (filter.Branches)
        {
            case BranchFilterMode.CurrentBranch:
                return "HEAD";
            case BranchFilterMode.SpecificBranch when !string.IsNullOrWhiteSpace(filter.Branch):
                return Quote("refs/heads/" + filter.Branch);
            default:
                StringBuilder revisions = new("--branches");
                if (filter.ShowRemoteBranches)
                {
                    revisions.Append(" --remotes");
                }

                if (filter.ShowTags)
                {
                    revisions.Append(" --tags");
                }

                return revisions.Append(" HEAD").ToString();
        }
    }

    private static bool IsSafeRevision(string text)
        => !text.StartsWith('-') && text.All(c => char.IsLetterOrDigit(c) || c is '/' or '_' or '.' or '-' or '^' or '~' or '@');

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
