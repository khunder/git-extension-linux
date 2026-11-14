namespace GitExtensions.Desktop.Services;

/// <summary>
///  Fuzzy-ish matching for the command palette: every query character must appear in the text, in order
///  (case-insensitive). Better matches (prefix, word starts, contiguous runs) rank first.
/// </summary>
public static class CommandMatcher
{
    /// <summary>
    ///  Returns a score (higher is better) or null when <paramref name="text"/> does not match <paramref name="query"/>.
    ///  An empty query matches everything with score 0.
    /// </summary>
    public static int? Score(string? query, string? text)
    {
        query = query?.Trim() ?? string.Empty;
        text ??= string.Empty;
        if (query.Length == 0)
        {
            return 0;
        }

        int score = 0;
        int searchFrom = 0;
        int previousMatch = -2;
        foreach (char ch in query)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            int index = text.IndexOf(ch.ToString(), searchFrom, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            if (IsWordStart(text, index))
            {
                score += 10;
            }

            if (index == previousMatch + 1)
            {
                score += 5;
            }
            else if (previousMatch >= 0)
            {
                score -= Math.Min(index - previousMatch - 1, 5);
            }

            previousMatch = index;
            searchFrom = index + 1;
        }

        string compact = string.Concat(query.Where(c => !char.IsWhiteSpace(c)));
        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }
        else if (HasWordStartingWith(text, query))
        {
            score += 60;
        }
        else if (text.Contains(compact, StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        return score;
    }

    /// <summary>
    ///  Filters and ranks <paramref name="items"/>; equal scores keep their original order.
    /// </summary>
    public static IReadOnlyList<T> Rank<T>(IEnumerable<T> items, string? query, Func<T, string> text)
        => items
            .Select((item, order) => (Item: item, Order: order, Score: Score(query, text(item))))
            .Where(entry => entry.Score is not null)
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.Order)
            .Select(entry => entry.Item)
            .ToList();

    private static bool IsWordStart(string text, int index)
        => index == 0 || !char.IsLetterOrDigit(text[index - 1]) || (char.IsUpper(text[index]) && char.IsLower(text[index - 1]));

    private static bool HasWordStartingWith(string text, string query)
    {
        for (int i = 1; i < text.Length; i++)
        {
            if (IsWordStart(text, i) && string.Compare(text, i, query, 0, query.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }
        }

        return false;
    }
}
