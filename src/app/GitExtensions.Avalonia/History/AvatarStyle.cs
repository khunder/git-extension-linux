namespace GitExtensions.Desktop.History;

/// <summary>Derives the initials and the colour of the avatar chip from an author name (no network involved).</summary>
public static class AvatarStyle
{
    public const int ColorCount = 8;

    public static string Initials(string? name)
    {
        string[] words = (name ?? string.Empty).Split([' ', '.', '-', '_', '@'], StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => "?",
            1 => words[0].Length == 1 ? char.ToUpperInvariant(words[0][0]).ToString() : string.Concat(char.ToUpperInvariant(words[0][0]), char.ToLowerInvariant(words[0][1])),
            _ => string.Concat(char.ToUpperInvariant(words[0][0]), char.ToUpperInvariant(words[^1][0])),
        };
    }

    /// <summary>A stable colour index for the name (not <see cref="string.GetHashCode()"/>, which varies per process).</summary>
    public static int ColorIndex(string? name)
    {
        uint hash = 2166136261;
        foreach (char c in (name ?? string.Empty).ToLowerInvariant())
        {
            hash = (hash ^ c) * 16777619;
        }

        return (int)(hash % ColorCount);
    }
}
