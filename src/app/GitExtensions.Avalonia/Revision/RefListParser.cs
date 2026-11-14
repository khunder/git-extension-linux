namespace GitExtensions.Desktop.Revision;

/// <summary>
///  Parses <c>git branch -a --contains --format=%(refname)</c> and <c>git tag --contains</c> output.
/// </summary>
public static class RefListParser
{
    /// <summary>The most names a "Contained in" line lists; the rest is summarised.</summary>
    public const int MaxNames = 50;

    public static IReadOnlyList<string> ParseBranches(string? output)
    {
        List<string> names = [];
        foreach (string raw in SplitLines(output))
        {
            string line = raw.Trim();
            if (line.StartsWith("refs/heads/", StringComparison.Ordinal))
            {
                names.Add(line["refs/heads/".Length..]);
            }
            else if (line.StartsWith("refs/remotes/", StringComparison.Ordinal))
            {
                string name = line["refs/remotes/".Length..];

                // "origin/HEAD" is a pointer to another remote branch, not a branch of its own.
                if (!name.EndsWith("/HEAD", StringComparison.Ordinal) && name.Contains('/'))
                {
                    names.Add(name);
                }
            }

            // Anything else is "(HEAD detached at ...)" and the like.
        }

        return names;
    }

    public static IReadOnlyList<string> ParseTags(string? output)
        => [.. SplitLines(output).Select(line => line.Trim()).Where(line => line.Length > 0)];

    /// <summary>
    ///  "a, b, c" for at most <see cref="MaxNames"/> names, then "… and N more".
    /// </summary>
    public static string Join(IReadOnlyList<string> names)
        => names.Count <= MaxNames
            ? string.Join(", ", names)
            : $"{string.Join(", ", names.Take(MaxNames))} … and {names.Count - MaxNames} more";

    private static IEnumerable<string> SplitLines(string? output)
        => (output ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
