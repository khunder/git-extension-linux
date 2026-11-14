using System.Globalization;
using System.Text.RegularExpressions;

namespace GitExtensions.Desktop.ViewModels;

public enum LostObjectType
{
    Commit,
    Blob,
    Tree,
    Tag,
    Other,
}

/// <summary>One line of <c>git fsck</c>: "dangling commit &lt;id&gt;", "unreachable blob &lt;id&gt;", "missing tree &lt;id&gt;"…</summary>
public sealed record LostObjectLine(string RawType, LostObjectType Type, string Hash);

public static partial class LostObjectParser
{
    [GeneratedRegex(@"^(?<raw>(dangling|missing|unreachable) (?<type>commit|blob|tree|tag)|warning in tree) (?<id>[0-9a-f]{40,64})")]
    private static partial Regex LineRegex { get; }

    public static IReadOnlyList<LostObjectLine> Parse(string fsckOutput)
    {
        List<LostObjectLine> lines = [];
        foreach (string line in fsckOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Match match = LineRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            LostObjectType type = match.Groups["type"].Value switch
            {
                "commit" => LostObjectType.Commit,
                "blob" => LostObjectType.Blob,
                "tree" => LostObjectType.Tree,
                "tag" => LostObjectType.Tag,
                _ => LostObjectType.Other,
            };
            lines.Add(new LostObjectLine(match.Groups["raw"].Value, type, match.Groups["id"].Value));
        }

        return lines;
    }

    /// <summary>Reads the output of <c>git show --no-patch --format=%H%x1f%aN%x1f%ct%x1f%P%x1f%s%x1e</c>.</summary>
    public static IReadOnlyDictionary<string, (string Author, DateTimeOffset Date, string Parent, string Subject)> ParseMetadata(string output)
    {
        Dictionary<string, (string, DateTimeOffset, string, string)> result = [];
        foreach (string record in output.Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = record.Trim('\n', '\r').Split('\u001f');
            if (fields.Length < 5 || !long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
            {
                continue;
            }

            string parent = fields[3].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            result[fields[0]] = (fields[1], DateTimeOffset.FromUnixTimeSeconds(seconds), parent, fields[4]);
        }

        return result;
    }
}
