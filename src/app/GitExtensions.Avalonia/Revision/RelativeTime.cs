using System.Globalization;

namespace GitExtensions.Desktop.Revision;

/// <summary>"1 day ago (17/02/2019 00:24:07)" like the commit info of the original Git Extensions.</summary>
public static class RelativeTime
{
    public static string Format(DateTime now, DateTime then)
    {
        TimeSpan span = now - then;
        if (span < TimeSpan.Zero)
        {
            return "in the future";
        }

        string text = span.TotalSeconds < 90 ? Unit((int)span.TotalSeconds, "second")
            : span.TotalMinutes < 90 ? Unit((int)Math.Round(span.TotalMinutes), "minute")
            : span.TotalHours < 24 ? Unit((int)Math.Round(span.TotalHours), "hour")
            : span.TotalDays < 14 ? Unit((int)Math.Round(span.TotalDays), "day")
            : span.TotalDays < 60 ? Unit((int)Math.Round(span.TotalDays / 7), "week")
            : span.TotalDays < 730 ? Unit((int)Math.Round(span.TotalDays / 30.4), "month")
            : Unit((int)Math.Round(span.TotalDays / 365.25), "year");
        return $"{text} ago";
    }

    /// <summary>Relative text followed by the absolute local date and time.</summary>
    public static string FormatWithDate(DateTime now, DateTime then)
        => $"{Format(now, then)} ({then.ToString("G", CultureInfo.CurrentCulture)})";

    private static string Unit(int count, string unit) => count == 1 ? $"1 {unit}" : $"{count} {unit}s";
}
