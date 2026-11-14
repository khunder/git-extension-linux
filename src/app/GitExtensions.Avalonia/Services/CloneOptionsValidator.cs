namespace GitExtensions.Desktop.Services;

/// <summary>
///  The problems found in the clone dialog's input; a null entry means that field is fine.
/// </summary>
public sealed record CloneValidation(string? Url, string? ParentFolder, string? TargetName, string? Depth, string? Branch)
{
    public bool IsValid => Url is null && ParentFolder is null && TargetName is null && Depth is null && Branch is null;
}

/// <summary>
///  Validates the options of the clone dialog without running git.
/// </summary>
public static class CloneOptionsValidator
{
    private static readonly char[] _forbiddenInBranch = [' ', '~', '^', ':', '?', '*', '[', '\\'];

    public static CloneValidation Validate(string? url, string? parentFolder, string? targetName, string? depth, string? branch)
        => new(ValidateUrl(url), ValidateParent(parentFolder), ValidateTargetName(parentFolder, targetName), ValidateDepth(depth), ValidateBranch(branch));

    /// <summary>
    ///  Parses an optional shallow depth: empty means a full clone.
    /// </summary>
    public static bool TryParseDepth(string? text, out int? depth)
    {
        depth = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (int.TryParse(text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value) && value > 0)
        {
            depth = value;
            return true;
        }

        return false;
    }

    private static string? ValidateUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "Enter the URL or path of the repository to clone.";
        }

        // An argument starting with a dash would be read as an option by git.
        return url.TrimStart().StartsWith('-') ? "The URL cannot start with '-'." : null;
    }

    private static string? ValidateParent(string? parentFolder)
    {
        if (string.IsNullOrWhiteSpace(parentFolder))
        {
            return "Choose the folder to clone into.";
        }

        string path = parentFolder.Trim();
        if (!Path.IsPathRooted(path))
        {
            return "Enter a full folder path.";
        }

        return File.Exists(path) ? "The destination is a file, not a folder." : null;
    }

    private static string? ValidateTargetName(string? parentFolder, string? targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName))
        {
            return "Enter a name for the new folder.";
        }

        string name = targetName.Trim();
        if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "The folder name contains characters that are not allowed.";
        }

        if (string.IsNullOrWhiteSpace(parentFolder) || !Path.IsPathRooted(parentFolder.Trim()))
        {
            return null;
        }

        string target = Path.Combine(parentFolder.Trim(), name);
        try
        {
            if (File.Exists(target))
            {
                return $"'{target}' already exists and is a file.";
            }

            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            {
                return $"'{target}' already exists and is not empty.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Cannot read '{target}': {ex.Message}";
        }

        return null;
    }

    private static string? ValidateDepth(string? depth)
        => TryParseDepth(depth, out _) ? null : "The depth must be a positive whole number.";

    private static string? ValidateBranch(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            return null;
        }

        string name = branch.Trim();
        bool bad = name.StartsWith('-')
            || name.StartsWith('/')
            || name.EndsWith('/')
            || name.EndsWith('.')
            || name.EndsWith(".lock", StringComparison.Ordinal)
            || name.Contains("..", StringComparison.Ordinal)
            || name.Contains("@{", StringComparison.Ordinal)
            || name.Contains("//", StringComparison.Ordinal)
            || name.IndexOfAny(_forbiddenInBranch) >= 0
            || name.Any(char.IsControl);
        return bad ? "That is not a valid branch or tag name." : null;
    }
}
