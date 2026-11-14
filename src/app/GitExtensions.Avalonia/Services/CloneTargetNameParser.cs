namespace GitExtensions.Desktop.Services;

/// <summary>
///  Derives the default folder name for a clone from its URL, the way <c>git clone</c> does.
/// </summary>
public static class CloneTargetNameParser
{
    /// <summary>
    ///  Gets the folder name for <paramref name="url"/> (https, ssh, scp-like <c>git@host:owner/repo.git</c>, file URLs and paths,
    ///  including Windows-style ones), or an empty string if none can be derived.
    /// </summary>
    public static string Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        string text = url.Trim();

        // Query strings and fragments are not part of the repository name.
        int cut = text.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            text = text[..cut];
        }

        text = text.TrimEnd('/', '\\');

        // A bare "C:" or "scheme:" has no name.
        int lastSeparator = text.LastIndexOfAny(['/', '\\', ':']);
        string name = lastSeparator >= 0 ? text[(lastSeparator + 1)..] : text;

        // "host:repo.git" (no slash) is handled by the colon above; "https://host" yields the host like git does.
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) && name.Length > 4)
        {
            name = name[..^4];
        }

        name = name.Trim();
        return name is "." or ".." ? string.Empty : name;
    }
}
