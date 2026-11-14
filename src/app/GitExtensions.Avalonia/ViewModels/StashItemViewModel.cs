using System.Globalization;
using System.Text.RegularExpressions;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  One entry of <c>git stash list</c>.
/// </summary>
public sealed partial class StashItemViewModel
{
    [GeneratedRegex(@"^(?:WIP on|On) (?<branch>[^:]*): (?<message>.*)$", RegexOptions.ExplicitCapture | RegexOptions.Singleline)]
    private static partial Regex SubjectRegex { get; }

    public StashItemViewModel(int index, string subject, DateTimeOffset? date)
    {
        Index = index;
        Name = $"stash@{{{index}}}";
        Match match = SubjectRegex.Match(subject);
        Branch = match.Success ? match.Groups["branch"].Value : string.Empty;
        Message = match.Success ? match.Groups["message"].Value : subject;
        DateText = date?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    public int Index { get; }

    /// <summary><c>stash@{n}</c>.</summary>
    public string Name { get; }

    public string Message { get; }

    public string Branch { get; }

    public string DateText { get; }

    public string IndexText => $"@{{{Index}}}";

    /// <summary>Parses one line of <c>stash list --format=%gd%x1f%gs%x1f%cI</c>.</summary>
    public static StashItemViewModel? TryParse(string line)
    {
        string[] parts = line.Split('\u001f');
        if (parts.Length < 2 || !parts[0].StartsWith("stash@{", StringComparison.Ordinal))
        {
            return null;
        }

        string number = parts[0]["stash@{".Length..].TrimEnd('}');
        if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
        {
            return null;
        }

        DateTimeOffset? date = parts.Length > 2 && DateTimeOffset.TryParse(parts[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed) ? parsed : null;
        return new StashItemViewModel(index, parts[1], date);
    }
}
