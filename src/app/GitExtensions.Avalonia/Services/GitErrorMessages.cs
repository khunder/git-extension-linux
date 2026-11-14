using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  Turns raw git failures into messages that say what is wrong and what to do.
/// </summary>
public static class GitErrorMessages
{
    /// <summary>
    ///  Splits the status of a repository into the real changes and the error git reported, if any.
    ///  The core reports a failed <c>git status</c> as a fake changed file named "Git Error".
    /// </summary>
    public static (List<GitItemStatus> Changes, string? Error) SplitStatusError(IEnumerable<GitItemStatus> items, string repositoryPath)
    {
        List<GitItemStatus> changes = [];
        string? error = null;
        foreach (GitItemStatus item in items)
        {
            if (item.IsStatusOnly && !string.IsNullOrWhiteSpace(item.ErrorMessage))
            {
                error ??= Describe(item.ErrorMessage, repositoryPath);
            }
            else if (!item.IsStatusOnly)
            {
                changes.Add(item);
            }
        }

        return (changes, error);
    }

    /// <summary>
    ///  Describes a git error message for <paramref name="repositoryPath"/>.
    /// </summary>
    public static string Describe(string gitMessage, string repositoryPath)
    {
        string message = gitMessage.Trim();
        if (message.Contains("dubious ownership", StringComparison.OrdinalIgnoreCase))
        {
            return $"Git refuses to use {repositoryPath} because it is owned by another user (dubious ownership). "
                + $"If you trust it, run: git config --global --add safe.directory \"{repositoryPath}\"";
        }

        if (message.Contains("must be run in a work tree", StringComparison.OrdinalIgnoreCase))
        {
            return $"{repositoryPath} is a bare repository: there is no working tree, so no changes are shown.";
        }

        if (message.Contains("index.lock", StringComparison.OrdinalIgnoreCase))
        {
            return "Another git process is using this repository (index.lock exists). If none is running, delete .git/index.lock. " + FirstLine(message);
        }

        return FirstLine(message);
    }

    private static string FirstLine(string message)
    {
        int end = message.IndexOfAny(['\r', '\n']);
        string line = end < 0 ? message : message[..end];
        return line.Length > 400 ? line[..400] + "…" : line;
    }
}
