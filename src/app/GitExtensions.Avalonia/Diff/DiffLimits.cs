using System.Text;

namespace GitExtensions.Desktop.Diff;

/// <summary>
///  Guards that keep the viewers responsive for pathological inputs: giant diffs, giant lines, control characters.
/// </summary>
public static class DiffLimits
{
    /// <summary>The most lines a diff viewer shows; the rest is replaced by a notice.</summary>
    public const int MaxDisplayedLines = 50_000;

    /// <summary>The most characters of one line that are laid out and drawn.</summary>
    public const int MaxDisplayedLineLength = 2_000;

    /// <summary>
    ///  Text safe to hand to a text control: truncated to <see cref="MaxDisplayedLineLength"/> and with control
    ///  characters (NUL, ESC, ...) other than tab replaced by a visible placeholder.
    /// </summary>
    public static string ToDisplayText(string text)
    {
        bool truncate = text.Length > MaxDisplayedLineLength;
        ReadOnlySpan<char> span = truncate ? text.AsSpan(0, MaxDisplayedLineLength) : text.AsSpan();
        bool hasControl = false;
        foreach (char c in span)
        {
            if (char.IsControl(c) && c != '\t')
            {
                hasControl = true;
                break;
            }
        }

        if (!truncate && !hasControl)
        {
            return text;
        }

        StringBuilder builder = new(span.Length + 1);
        foreach (char c in span)
        {
            builder.Append(char.IsControl(c) && c != '\t' ? '�' : c);
        }

        if (truncate)
        {
            builder.Append('…');
        }

        return builder.ToString();
    }

    /// <summary>
    ///  Limits <paramref name="lines"/> to <see cref="MaxDisplayedLines"/>, adding an information line when it cut something off.
    /// </summary>
    /// <returns><see langword="true"/> if the list was truncated.</returns>
    public static bool Truncate(ref IReadOnlyList<DiffLine> lines)
    {
        if (lines.Count <= MaxDisplayedLines)
        {
            return false;
        }

        List<DiffLine> limited = new(MaxDisplayedLines + 1);
        for (int i = 0; i < MaxDisplayedLines; i++)
        {
            limited.Add(lines[i]);
        }

        limited.Add(new DiffLine(DiffLineKind.Info, $"Diff too large: showing the first {MaxDisplayedLines:N0} of {lines.Count:N0} lines.", null, null));
        lines = limited;
        return true;
    }
}
