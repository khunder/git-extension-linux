using System.Globalization;
using System.Text.RegularExpressions;

namespace GitExtensions.Desktop.FileHistory;

/// <summary>
///  One line of a file with the commit that last changed it.
/// </summary>
public sealed record BlameEntry(int LineNumber, string CommitHash, string Author, DateTimeOffset Date, string Summary, string Text);

/// <summary>
///  Parses <c>git blame --porcelain</c> output.
/// </summary>
public static partial class BlameParser
{
    [GeneratedRegex(@"^(?<sha>[0-9a-f]{40,64}) (?<orig>\d+) (?<final>\d+)( \d+)?$")]
    private static partial Regex HeaderLine();

    public static IReadOnlyList<BlameEntry> Parse(string? porcelain)
    {
        List<BlameEntry> result = [];
        if (string.IsNullOrEmpty(porcelain))
        {
            return result;
        }

        // Details (author, time, summary) are given once per commit, on its first line in the output.
        Dictionary<string, CommitInfo> commits = [];
        CommitInfo? current = null;
        int finalLine = 0;

        foreach (string raw in porcelain.Split('\n'))
        {
            if (raw.Length > 0 && raw[0] == '\t')
            {
                if (current is not null)
                {
                    result.Add(new BlameEntry(finalLine, current.Hash, current.Author, current.Date, current.Summary, raw[1..].TrimEnd('\r')));
                }

                continue;
            }

            string line = raw.TrimEnd('\r');
            if (HeaderLine().Match(line) is { Success: true } header)
            {
                string sha = header.Groups["sha"].Value;
                if (!commits.TryGetValue(sha, out current))
                {
                    current = new CommitInfo(sha);
                    commits[sha] = current;
                }

                finalLine = int.Parse(header.Groups["final"].Value, CultureInfo.InvariantCulture);
                continue;
            }

            if (current is null)
            {
                continue;
            }

            int space = line.IndexOf(' ');
            string key = space < 0 ? line : line[..space];
            string value = space < 0 ? string.Empty : line[(space + 1)..];
            switch (key)
            {
                case "author":
                    current.Author = value;
                    break;
                case "author-time" when long.TryParse(value, CultureInfo.InvariantCulture, out long seconds):
                    current.Seconds = seconds;
                    break;
                case "author-tz":
                    current.Timezone = value;
                    break;
                case "summary":
                    current.Summary = value;
                    break;
            }
        }

        return result;
    }

    private sealed class CommitInfo(string hash)
    {
        public string Hash { get; } = hash;

        public string Author { get; set; } = string.Empty;

        public string Summary { get; set; } = string.Empty;

        public long Seconds { get; set; }

        public string Timezone { get; set; } = "+0000";

        public DateTimeOffset Date
        {
            get
            {
                DateTimeOffset utc = DateTimeOffset.FromUnixTimeSeconds(Seconds);
                if (Timezone.Length == 5 && int.TryParse(Timezone.AsSpan(1, 2), out int hours) && int.TryParse(Timezone.AsSpan(3, 2), out int minutes))
                {
                    TimeSpan offset = new(hours, minutes, 0);
                    return utc.ToOffset(Timezone[0] == '-' ? -offset : offset);
                }

                return utc;
            }
        }
    }
}
