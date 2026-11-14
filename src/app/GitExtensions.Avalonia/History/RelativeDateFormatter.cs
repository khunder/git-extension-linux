using System.Globalization;

namespace GitExtensions.Desktop.History;

/// <summary>
///  Formats a date the way the revision grid shows it: "14 hours ago", "15 days ago".
/// </summary>
public static class RelativeDateFormatter
{
    public static string Format(DateTimeOffset date, DateTimeOffset now)
    {
        TimeSpan age = now - date;
        if (age < TimeSpan.FromMinutes(1))
        {
            // Includes dates slightly in the future (clock skew between machines).
            return "just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return Unit((int)age.TotalMinutes, "minute");
        }

        if (age < TimeSpan.FromDays(1))
        {
            return Unit((int)age.TotalHours, "hour");
        }

        if (age < TimeSpan.FromDays(30))
        {
            return Unit((int)age.TotalDays, "day");
        }

        if (age < TimeSpan.FromDays(365))
        {
            return Unit((int)(age.TotalDays / 30), "month");
        }

        return Unit((int)(age.TotalDays / 365), "year");
    }

    public static string Format(DateTimeOffset date) => Format(date, DateTimeOffset.Now);

    /// <summary>The absolute date shown in the tooltip.</summary>
    public static string Absolute(DateTimeOffset date) => date.ToString("f", CultureInfo.CurrentCulture);

    private static string Unit(int count, string unit)
        => count == 1 ? $"1 {unit} ago" : $"{count} {unit}s ago";
}
