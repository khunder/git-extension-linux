using System.Text.RegularExpressions;

namespace GitExtensions.Desktop.Revision;

/// <summary>A link found in (or derived for) a commit message.</summary>
public sealed record MessageLink(string Text, string Url);

/// <summary>
///  Finds the "Related links" of a commit: issue references (<c>#123</c>) and web addresses in its message.
/// </summary>
public static partial class MessageLinker
{
    private const int MaxLinks = 20;

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    [GeneratedRegex(@"(?<![\w&/#])#(?<number>\d{1,9})\b")]
    private static partial Regex IssuePattern();

    /// <summary>
    ///  The links in <paramref name="message"/>, without duplicates, in order of appearance.
    ///  Issue references become links only when the repository's web address is known.
    /// </summary>
    /// <param name="repositoryWebUrl">E.g. <c>https://github.com/owner/repo</c> (see <see cref="GetRepositoryWebUrl"/>).</param>
    public static IReadOnlyList<MessageLink> Find(string? message, string? repositoryWebUrl = null)
    {
        List<(int Index, MessageLink Link)> found = [];
        if (!string.IsNullOrEmpty(message))
        {
            foreach (Match match in UrlPattern().Matches(message))
            {
                string url = TrimUrl(match.Value);
                if (url.Length > "https://".Length)
                {
                    found.Add((match.Index, new MessageLink(url, url)));
                }
            }

            if (!string.IsNullOrEmpty(repositoryWebUrl))
            {
                foreach (Match match in IssuePattern().Matches(message))
                {
                    // "#123" inside a web address is part of that address, not an issue reference.
                    if (!found.Any(entry => match.Index >= entry.Index && match.Index < entry.Index + entry.Link.Text.Length))
                    {
                        string number = match.Groups["number"].Value;
                        found.Add((match.Index, new MessageLink($"Issue {number}", $"{repositoryWebUrl.TrimEnd('/')}/issues/{number}")));
                    }
                }
            }
        }

        return [.. found.OrderBy(entry => entry.Index).Select(entry => entry.Link).DistinctBy(link => link.Url).Take(MaxLinks)];
    }

    /// <summary>
    ///  The browsable address of a remote (<c>git@github.com:o/r.git</c> or <c>https://host/o/r.git</c> to
    ///  <c>https://host/o/r</c>), or <see langword="null"/> when it is not a web-hosted remote.
    /// </summary>
    public static string? GetRepositoryWebUrl(string? remoteUrl)
    {
        string url = (remoteUrl ?? string.Empty).Trim();
        if (url.Length == 0)
        {
            return null;
        }

        string host;
        string path;
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" or "ssh" or "git")
        {
            host = uri.Host;
            path = uri.AbsolutePath;
        }
        else if (Regex.Match(url, @"^(?:[^@/\s]+@)?(?<host>[^:/\s]+):(?<path>[^/].*)$") is { Success: true } scp)
        {
            host = scp.Groups["host"].Value;
            path = scp.Groups["path"].Value;
        }
        else
        {
            return null;
        }

        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^4];
        }

        return host.Length == 0 || path.Split('/').Length < 2 ? null : $"https://{host}/{path}";
    }

    /// <summary>"View on GitHub" and the like, for the commit's page on the hosting service.</summary>
    public static MessageLink? GetCommitLink(string? repositoryWebUrl, string commitHash)
    {
        if (string.IsNullOrEmpty(repositoryWebUrl) || !Uri.TryCreate(repositoryWebUrl, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        string service = uri.Host.ToLowerInvariant() switch
        {
            "github.com" => "GitHub",
            "gitlab.com" => "GitLab",
            "bitbucket.org" => "Bitbucket",
            _ => uri.Host
        };
        return new MessageLink($"View on {service}", $"{repositoryWebUrl.TrimEnd('/')}/commit/{commitHash}");
    }

    private static string TrimUrl(string url)
    {
        url = url.TrimEnd('.', ',', ';', ':', '!', '?');

        // A closing bracket belongs to the address only when it has an opening partner inside it.
        while (url.Length > 0 && url[^1] is ')' or ']' or '}' && url.Count(c => c == url[^1]) > url.Count(c => c == Opening(url[^1])))
        {
            url = url[..^1].TrimEnd('.', ',', ';', ':', '!', '?');
        }

        return url;
    }

    private static char Opening(char closing) => closing switch { ')' => '(', ']' => '[', _ => '{' };
}
