using System.Globalization;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  How a local branch relates to its upstream. <see cref="Upstream"/> is e.g. <c>origin/main</c>.
/// </summary>
public sealed record UpstreamTrack(string Upstream, int Ahead, int Behind, bool Gone, string RemoteName = "", string RemoteRef = "")
{
    public bool InSync => !Gone && Ahead == 0 && Behind == 0;

    /// <summary>
    ///  The counts as the original shows them after a branch name: <c>(0↑↓)</c> in sync, otherwise <c>(2↑ 1↓)</c> with only
    ///  the counts that are not zero. A remote branch shows the mirrored view (<c>(0↓↑)</c> in sync).
    /// </summary>
    public string CountsText(bool remoteSide = false) => Gone ? "(gone)"
        : InSync ? (remoteSide ? "(0↓↑)" : "(0↑↓)")
        : "(" + string.Join(' ', new[] { Ahead > 0 ? $"{Ahead}↑" : null, Behind > 0 ? $"{Behind}↓" : null }.OfType<string>()) + ")";

    /// <summary>Compact marker such as <c>↑2 ↓1</c>, <c>✓</c> (in sync) or <c>gone</c>.</summary>
    public string Text => Gone ? "gone"
        : InSync ? "✓"
        : string.Join(' ', new[] { Ahead > 0 ? $"↑{Ahead}" : null, Behind > 0 ? $"↓{Behind}" : null }.OfType<string>());

    public string ToolTip => Gone
        ? $"The upstream branch {Upstream} no longer exists on the remote. Set a new upstream or delete this branch."
        : InSync
            ? $"Up to date with {Upstream}."
            : $"Compared with {Upstream}: "
                + string.Join(", ", new[]
                {
                    Ahead > 0 ? $"↑{Ahead} = {Plural(Ahead)} not pushed yet (ahead)" : null,
                    Behind > 0 ? $"↓{Behind} = {Plural(Behind)} not pulled yet (behind)" : null,
                }.OfType<string>());

    private static string Plural(int count) => count == 1 ? "1 commit" : $"{count} commits";
}

/// <summary>
///  Parses the output of <c>git for-each-ref</c> with <c>%(upstream:track)</c>.
/// </summary>
public static class UpstreamTrackParser
{
    /// <summary>The format string for <c>for-each-ref refs/heads</c>; fields are tab separated.</summary>
    public const string ForEachRefFormat = "%(refname)%09%(upstream:short)%09%(upstream:track)%09%(upstream:remotename)%09%(upstream:remoteref)";

    /// <summary>Parses <c>[ahead 2, behind 1]</c>, <c>[gone]</c>, <c>[ahead 3]</c>, <c>[behind 1]</c> or an empty string.</summary>
    public static (int Ahead, int Behind, bool Gone) Parse(string? track)
    {
        string text = (track ?? string.Empty).Trim().Trim('[', ']');
        if (text.Length == 0)
        {
            return (0, 0, false);
        }

        int ahead = 0;
        int behind = 0;
        bool gone = false;
        foreach (string part in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals("gone", StringComparison.OrdinalIgnoreCase))
            {
                gone = true;
            }
            else if (part.StartsWith("ahead ", StringComparison.OrdinalIgnoreCase) && int.TryParse(part[6..], NumberStyles.None, CultureInfo.InvariantCulture, out int a))
            {
                ahead = a;
            }
            else if (part.StartsWith("behind ", StringComparison.OrdinalIgnoreCase) && int.TryParse(part[7..], NumberStyles.None, CultureInfo.InvariantCulture, out int b))
            {
                behind = b;
            }
        }

        return (ahead, behind, gone);
    }

    /// <summary>
    ///  Parses all lines of the output into branch name -> track. Branches without an upstream are left out.
    /// </summary>
    public static Dictionary<string, UpstreamTrack> ParseAll(string output)
    {
        Dictionary<string, UpstreamTrack> result = [];
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.TrimEnd('\r').Split('\t');
            if (fields.Length < 2 || fields[1].Length == 0)
            {
                continue;
            }

            string name = fields[0].StartsWith("refs/heads/", StringComparison.Ordinal) ? fields[0]["refs/heads/".Length..] : fields[0];
            (int ahead, int behind, bool gone) = Parse(fields.Length > 2 ? fields[2] : string.Empty);
            string remoteRef = fields.Length > 4 ? fields[4] : string.Empty;
            result[name] = new UpstreamTrack(
                fields[1],
                ahead,
                behind,
                gone,
                fields.Length > 3 ? fields[3] : string.Empty,
                remoteRef.StartsWith("refs/heads/", StringComparison.Ordinal) ? remoteRef["refs/heads/".Length..] : remoteRef);
        }

        return result;
    }
}
