using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GitExtensions.Desktop.Revision;

/// <summary>A local, network-free avatar: the author's initials on a colour derived from the author.</summary>
public static class CommitAvatar
{
    public static string GetInitials(string? name, string? email)
    {
        string source = string.IsNullOrWhiteSpace(name) ? (email ?? string.Empty).Split('@')[0] : name;
        string[] words = source.Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        string initials = words.Length switch
        {
            0 => "?",
            1 => words[0][..1],
            _ => string.Concat(words[0][..1], words[^1][..1])
        };
        return initials.ToUpperInvariant();
    }

    public static IBrush GetBrush(string key)
    {
        // A stable hash (string.GetHashCode is randomised per process).
        uint hash = 2166136261;
        foreach (char c in key.ToLowerInvariant())
        {
            hash = (hash ^ c) * 16777619;
        }

        // Immutable: usable from any thread (a SolidColorBrush may only be created on the UI thread).
        return new ImmutableSolidColorBrush(new HsvColor(1, hash % 360, 0.55, 0.55).ToRgb());
    }
}
