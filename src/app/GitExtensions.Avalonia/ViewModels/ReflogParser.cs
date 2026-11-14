using System.Globalization;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>One line of <c>git reflog</c>.</summary>
public sealed record ReflogEntry(string Hash, string Selector, string Message, DateTimeOffset? Date)
{
    public string ShortHash => Hash.Length > 8 ? Hash[..8] : Hash;

    public string DateText => Date is { } date ? date.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture) : string.Empty;
}

/// <summary>Parses the output of <c>git reflog show --format=%H%x1f%gd%x1f%gs%x1f%cI</c>.</summary>
public static class ReflogParser
{
    public const string Format = "%H%x1f%gd%x1f%gs%x1f%cI";

    public static IReadOnlyList<ReflogEntry> Parse(string output)
    {
        List<ReflogEntry> entries = [];
        foreach (string raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = raw.TrimEnd('\r').Split('\x1f');
            if (fields.Length < 3 || fields[0].Length < 7)
            {
                continue;
            }

            DateTimeOffset? date = fields.Length > 3 && DateTimeOffset.TryParse(fields[3], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed) ? parsed : null;
            entries.Add(new ReflogEntry(fields[0], fields[1], fields[2], date));
        }

        return entries;
    }
}
