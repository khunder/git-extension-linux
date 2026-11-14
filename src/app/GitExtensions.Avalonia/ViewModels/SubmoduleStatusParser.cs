using System.Text.RegularExpressions;

namespace GitExtensions.Desktop.ViewModels;

public enum SubmoduleState
{
    Uninitialized,
    UpToDate,
    Modified,
    Dirty,
    Conflict,
    Missing,
}

/// <summary>One line of <c>git submodule status</c>: the flag, the recorded commit, the path and git's description.</summary>
public sealed record SubmoduleStatusLine(SubmoduleState State, string Commit, string Path, string? Describe);

/// <summary>An entry of <c>.gitmodules</c>.</summary>
public sealed record GitModulesEntry(string Name, string Path, string? Url);

public static class SubmoduleStatusParser
{
    private static readonly Regex DescribeSuffix = new(@"^(?<path>.*) \((?<describe>[^()]*)\)$", RegexOptions.Compiled);

    /// <summary>
    ///  Parses <c>git submodule status [--recursive]</c>. The flag column is ' ' (up to date), '-' (not initialized),
    ///  '+' (checked out commit differs from the recorded one) or 'U' (merge conflict).
    /// </summary>
    public static IReadOnlyList<SubmoduleStatusLine> ParseStatus(string output)
    {
        List<SubmoduleStatusLine> lines = [];
        foreach (string raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length < 10)
            {
                continue;
            }

            SubmoduleState? state = line[0] switch
            {
                '-' => SubmoduleState.Uninitialized,
                '+' => SubmoduleState.Modified,
                'U' => SubmoduleState.Conflict,
                ' ' => SubmoduleState.UpToDate,
                _ => null,
            };
            int space = line.IndexOf(' ', 1);
            if (state is null || space < 0)
            {
                continue;
            }

            string commit = line[1..space];
            string rest = line[(space + 1)..];
            string? describe = null;
            if (DescribeSuffix.Match(rest) is { Success: true } match)
            {
                rest = match.Groups["path"].Value;
                describe = match.Groups["describe"].Value;
            }

            lines.Add(new SubmoduleStatusLine(state.Value, commit, rest, describe));
        }

        return lines;
    }

    /// <summary>Parses <c>git config -f .gitmodules --get-regexp "^submodule\..*\.(path|url)$"</c> (lines of "key value").</summary>
    public static IReadOnlyList<GitModulesEntry> ParseGitModules(string output)
    {
        Dictionary<string, (string? Path, string? Url)> byName = [];
        List<string> order = [];
        foreach (string raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string line = raw.TrimEnd('\r');
            int space = line.IndexOf(' ');
            if (space < 0 || !line.StartsWith("submodule.", StringComparison.Ordinal))
            {
                continue;
            }

            string key = line["submodule.".Length..space];
            string value = line[(space + 1)..];
            int dot = key.LastIndexOf('.');
            if (dot < 0)
            {
                continue;
            }

            string name = key[..dot];
            string property = key[(dot + 1)..];
            if (!byName.TryGetValue(name, out (string? Path, string? Url) current))
            {
                order.Add(name);
            }

            byName[name] = property switch
            {
                "path" => (value, current.Url),
                "url" => (current.Path, value),
                _ => current,
            };
        }

        return order.Where(name => byName[name].Path is not null).Select(name => new GitModulesEntry(name, byName[name].Path!, byName[name].Url)).ToList();
    }
}
