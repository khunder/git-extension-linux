using System.Text.RegularExpressions;

namespace GitExtensions.Desktop.Services;

/// <summary>Recognises GitHub remotes and builds the web addresses of their commits.</summary>
public static partial class GitHubRemote
{
    [GeneratedRegex(@"^(?:https?://(?:[^/@]+@)?|ssh://(?:[^/@]+@)?|git://|git@)github\.com[/:](?<owner>[^/\s]+)/(?<repo>[^/\s]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>The web address of the repository (<c>https://github.com/owner/repo</c>) for a GitHub remote URL, otherwise null.</summary>
    public static string? WebUrl(string? remoteUrl)
        => remoteUrl is not null && Pattern().Match(remoteUrl.Trim()) is { Success: true } match
            ? $"https://github.com/{match.Groups["owner"].Value}/{match.Groups["repo"].Value}"
            : null;

    /// <summary>
    ///  The GitHub web address for the remotes in the text of a git config file (origin first, then the others), or null
    ///  when no remote lives on GitHub.
    /// </summary>
    public static string? WebUrlFromConfig(string configText)
    {
        List<(string Remote, string Url)> remotes = [];
        string? current = null;
        foreach (string raw in configText.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('['))
            {
                Match header = RemoteSection().Match(line);
                current = header.Success ? header.Groups["name"].Value : null;
            }
            else if (current is not null && line.StartsWith("url", StringComparison.OrdinalIgnoreCase) && line.IndexOf('=') is > 0 and int equals)
            {
                remotes.Add((current, line[(equals + 1)..].Trim()));
            }
        }

        return remotes.OrderBy(r => r.Remote == "origin" ? 0 : 1).Select(r => WebUrl(r.Url)).FirstOrDefault(u => u is not null);
    }

    [GeneratedRegex("""^\[remote\s+"(?<name>[^"]+)"\]""", RegexOptions.CultureInvariant)]
    private static partial Regex RemoteSection();

    public static string CommitUrl(string webUrl, string commitId) => $"{webUrl}/commit/{commitId}";
}
