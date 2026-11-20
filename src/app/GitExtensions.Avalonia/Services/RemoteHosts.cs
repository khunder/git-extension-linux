using System.Text.RegularExpressions;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  Tells which hosting service each remote of a repository lives on, from the remote URLs in the git config text,
///  so the remotes tree can show the GitHub or GitLab icon for those remotes (and the plain remote icon for the others).
/// </summary>
public static partial class RemoteHosts
{
    public const string GitHub = "github";
    public const string GitLab = "gitlab";

    [GeneratedRegex(@"^\[remote ""(?<name>[^""]+)""\]$", RegexOptions.CultureInvariant)]
    private static partial Regex RemoteSection();

    [GeneratedRegex(@"^(?:[a-z][a-z0-9+.-]*://)?(?:[^/@\s]+@)?(?<host>[^/:\s]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();

    /// <summary>The hosting service ("github", "gitlab") of a remote URL, or null for any other host.</summary>
    public static string? HostOf(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl) || HostPattern().Match(remoteUrl.Trim()) is not { Success: true } match)
        {
            return null;
        }

        string host = match.Groups["host"].Value.ToLowerInvariant();
        if (host == "github.com" || host.EndsWith(".github.com", StringComparison.Ordinal))
        {
            return GitHub;
        }

        return host.Contains("gitlab", StringComparison.Ordinal) ? GitLab : null;
    }

    /// <summary>The hosting service of every remote named in the text of a git config file; remotes on other hosts are left out.</summary>
    public static Dictionary<string, string> FromConfig(string configText)
    {
        Dictionary<string, string> hosts = new(StringComparer.Ordinal);
        string? current = null;
        foreach (string raw in configText.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('['))
            {
                current = RemoteSection().Match(line) is { Success: true } header ? header.Groups["name"].Value : null;
            }
            else if (current is not null && line.StartsWith("url", StringComparison.OrdinalIgnoreCase) && line.IndexOf('=') is var equals and > 0
                && line[..equals].Trim().Equals("url", StringComparison.OrdinalIgnoreCase)
                && HostOf(line[(equals + 1)..].Trim()) is { } host)
            {
                hosts[current] = host;
            }
        }

        return hosts;
    }

    /// <summary>Reads the config file inside <paramref name="gitDir"/>; an unreadable file means no known hosts.</summary>
    public static Dictionary<string, string> Read(string gitDir)
    {
        try
        {
            string config = Path.Combine(gitDir, "config");
            return File.Exists(config) ? FromConfig(File.ReadAllText(config)) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
