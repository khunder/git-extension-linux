using System.Globalization;

namespace GitExtensions.Desktop.FileHistory;

/// <summary>
///  One commit of a file's history. <see cref="Path"/> is the name the file had in that commit (renames are followed).
/// </summary>
public sealed record FileHistoryEntry(string Hash, string Author, DateTimeOffset Date, string Subject, string Path)
{
    public string ShortHash => Hash.Length > 8 ? Hash[..8] : Hash;

    public string DateText => Date.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}

/// <summary>
///  Parses the output of <see cref="FileHistoryGit.LogFormat"/> (<c>git log --follow --name-only</c>).
/// </summary>
public static class FileLogParser
{
    public const char RecordMarker = '\u0001';
    public const char FieldSeparator = '\u001f';

    public static IReadOnlyList<FileHistoryEntry> Parse(string? output)
    {
        List<FileHistoryEntry> entries = [];
        if (string.IsNullOrEmpty(output))
        {
            return entries;
        }

        foreach (string record in output.Split(RecordMarker, StringSplitOptions.RemoveEmptyEntries))
        {
            string[] lines = record.Split('\n');
            string[] fields = lines[0].TrimEnd('\r').Split(FieldSeparator);
            if (fields.Length < 4 || !DateTimeOffset.TryParse(fields[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset date))
            {
                continue;
            }

            string path = lines.Skip(1).Select(line => line.TrimEnd('\r')).LastOrDefault(line => line.Length > 0) ?? string.Empty;
            entries.Add(new FileHistoryEntry(fields[0], fields[1], date, fields[3], path));
        }

        return entries;
    }
}
