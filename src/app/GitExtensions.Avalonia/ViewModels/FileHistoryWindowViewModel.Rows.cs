using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.FileHistory;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>The kind of a ref badge shown beside a commit message.</summary>
public enum FileHistoryRefKind
{
    Branch,
    Remote,
    Tag
}

/// <summary>A branch or tag name decorating a commit.</summary>
public sealed record FileHistoryRef(string Name, FileHistoryRefKind Kind, bool IsCurrent)
{
    public bool IsBranch => Kind == FileHistoryRefKind.Branch;

    public bool IsRemote => Kind == FileHistoryRefKind.Remote;

    public bool IsTag => Kind == FileHistoryRefKind.Tag;

    /// <summary>
    ///  Parses <c>git log --decorate=full %D</c> output (<c>HEAD -> refs/heads/main, tag: refs/tags/v1, refs/remotes/origin/main</c>).
    /// </summary>
    public static IReadOnlyList<FileHistoryRef> Parse(string? decorations)
    {
        List<FileHistoryRef> refs = [];
        if (string.IsNullOrWhiteSpace(decorations))
        {
            return refs;
        }

        foreach (string raw in decorations.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string name = raw;
            bool current = false;
            if (name.StartsWith("HEAD -> ", StringComparison.Ordinal))
            {
                current = true;
                name = name["HEAD -> ".Length..];
            }

            if (name == "HEAD")
            {
                continue;
            }

            if (name.StartsWith("tag: refs/tags/", StringComparison.Ordinal))
            {
                refs.Add(new FileHistoryRef(name["tag: refs/tags/".Length..], FileHistoryRefKind.Tag, false));
            }
            else if (name.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                refs.Add(new FileHistoryRef(name["refs/heads/".Length..], FileHistoryRefKind.Branch, current));
            }
            else if (name.StartsWith("refs/remotes/", StringComparison.Ordinal))
            {
                if (!name.EndsWith("/HEAD", StringComparison.Ordinal))
                {
                    refs.Add(new FileHistoryRef(name["refs/remotes/".Length..], FileHistoryRefKind.Remote, false));
                }
            }
        }

        return refs;
    }
}

/// <summary>The column widths of the revision grid, shared by its header and rows so resizing a header resizes every row.</summary>
public sealed partial class FileHistoryColumns : ObservableObject
{
    public const double MinWidth = 50;

    [ObservableProperty]
    private double _authorWidth = 190;

    [ObservableProperty]
    private double _dateWidth = 140;

    [ObservableProperty]
    private double _hashWidth = 110;
}

/// <summary>One row of the File History grid.</summary>
public sealed class FileHistoryRowViewModel
{
    private static readonly Color[] AvatarColors =
    [
        Color.FromRgb(0x3F, 0x7F, 0xBF), Color.FromRgb(0x8A, 0x5B, 0xB8), Color.FromRgb(0x3A, 0x9A, 0x7A), Color.FromRgb(0xC0, 0x6A, 0x2B),
        Color.FromRgb(0xB0, 0x4A, 0x6A), Color.FromRgb(0x5A, 0x8A, 0x30), Color.FromRgb(0x4A, 0x6A, 0xB0), Color.FromRgb(0x9A, 0x7A, 0x2A)
    ];

    public FileHistoryRowViewModel(FileHistoryEntry entry, FileHistoryColumns columns, string currentPath, DateTimeOffset now)
    {
        Entry = entry;
        Columns = columns;
        Refs = FileHistoryRef.Parse(entry.Refs);
        RenamedFrom = string.Equals(entry.Path, currentPath, StringComparison.Ordinal) ? string.Empty : entry.Path;
        RelativeDate = FormatRelative(entry.Date, now);
        Initials = GetInitials(entry.Author);
        AvatarBrush = new ImmutableSolidColorBrush(AvatarColors[(uint)StableHash(entry.Author) % (uint)AvatarColors.Length]);
    }

    public FileHistoryEntry Entry { get; }

    public FileHistoryColumns Columns { get; }

    public string Hash => Entry.Hash;

    public string ShortHash => Entry.Hash.Length > 10 ? Entry.Hash[..10] : Entry.Hash;

    public string Subject => Entry.Subject;

    public string Author => Entry.Author;

    public string Initials { get; }

    public IBrush AvatarBrush { get; }

    public string RelativeDate { get; }

    public IReadOnlyList<FileHistoryRef> Refs { get; }

    /// <summary>Gets the name the file had in this commit when it differs from its current name; blank otherwise.</summary>
    public string RenamedFrom { get; }

    public bool HasRenamedFrom => RenamedFrom.Length > 0;

    public string RenamedText => HasRenamedFrom ? "(" + RenamedFrom + ")" : string.Empty;

    public string ToolTip => $"{Entry.Hash}\n{Entry.Author}  {Entry.Date.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}\n{Entry.Path}";

    public static string GetInitials(string name)
    {
        string[] words = name.Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => "?",
            1 => char.ToUpperInvariant(words[0][0]).ToString(),
            _ => string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[^1][0]))
        };
    }

    /// <summary>
    ///  "3 days ago", "1 month ago": the form the original revision grid shows.
    /// </summary>
    public static string FormatRelative(DateTimeOffset date, DateTimeOffset now)
    {
        TimeSpan span = now - date;
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        static string Unit(long count, string unit) => count == 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";

        return span.TotalSeconds < 60 ? Unit((long)span.TotalSeconds, "second")
            : span.TotalMinutes < 60 ? Unit((long)span.TotalMinutes, "minute")
            : span.TotalHours < 24 ? Unit((long)span.TotalHours, "hour")
            : span.TotalDays < 7 ? Unit((long)span.TotalDays, "day")
            : span.TotalDays < 30 ? Unit((long)(span.TotalDays / 7), "week")
            : span.TotalDays < 365 ? Unit((long)(span.TotalDays / 30), "month")
            : Unit((long)(span.TotalDays / 365), "year");
    }

    private static int StableHash(string text)
    {
        int hash = 17;
        foreach (char c in text)
        {
            hash = unchecked((hash * 31) + c);
        }

        return hash & int.MaxValue;
    }
}

/// <summary>One line of the file in the View tab.</summary>
public sealed record FileViewLine(int Number, string Text)
{
    public string DisplayText => DiffLimits.ToDisplayText(Text);
}

/// <summary>A choice in the Branches combo box.</summary>
public sealed record FileHistoryBranchOption(string Display, string? Value)
{
    public override string ToString() => Display;
}
