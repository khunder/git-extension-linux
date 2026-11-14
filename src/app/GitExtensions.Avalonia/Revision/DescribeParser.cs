using System.Globalization;
using System.Text.RegularExpressions;

namespace GitExtensions.Desktop.Revision;

/// <summary>
///  Parses the output of <c>git describe --tags --long</c> (<c>v3.00.00-rc2-186-gabcdef</c>).
/// </summary>
public static partial class DescribeParser
{
    [GeneratedRegex(@"^(?<tag>.+)-(?<count>\d+)-g(?<hash>[0-9a-fA-F]+)$")]
    private static partial Regex LongFormat();

    /// <summary>The nearest tag and the number of commits since it.</summary>
    public readonly record struct Description(string Tag, int Commits)
    {
        public override string ToString() => Commits == 1 ? $"{Tag} + 1 commit" : $"{Tag} + {Commits} commits";
    }

    public static Description? Parse(string? output)
    {
        string text = (output ?? string.Empty).Trim();
        if (LongFormat().Match(text) is not { Success: true } match
            || !int.TryParse(match.Groups["count"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int count))
        {
            return null;
        }

        return new Description(match.Groups["tag"].Value, count);
    }
}
