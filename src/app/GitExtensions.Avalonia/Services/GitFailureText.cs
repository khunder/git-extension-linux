namespace GitExtensions.Desktop.Services;

/// <summary>
///  Picks the useful part of git's output when a command failed.
/// </summary>
public static class GitFailureText
{
    public static string Describe(IEnumerable<string> outputLines, bool userCancelledPrompt = false)
    {
        string[] lines = [.. outputLines.Select(line => line.Trim()).Where(line => line.Length > 0)];
        string[] errors = [.. lines.Where(line => line.StartsWith("fatal:", StringComparison.OrdinalIgnoreCase) || line.StartsWith("error:", StringComparison.OrdinalIgnoreCase))];
        string text = string.Join(' ', (errors.Length > 0 ? errors : lines).TakeLast(3));
        if (text.Length == 0)
        {
            text = "git exited with an error.";
        }

        if (text.Contains("terminal prompts disabled", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Authentication failed", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Permission denied", StringComparison.OrdinalIgnoreCase))
        {
            text += userCancelledPrompt
                ? " The credential prompt was cancelled."
                : " Authentication failed: check the credentials, or configure a credential helper or an SSH key.";
        }

        return text;
    }

    /// <summary>
    ///  Explains git's "who are you" failure for a commit.
    /// </summary>
    public static string DescribeCommitFailure(string gitMessage)
        => gitMessage.Contains("identity", StringComparison.OrdinalIgnoreCase) || gitMessage.Contains("user.name", StringComparison.OrdinalIgnoreCase) || gitMessage.Contains("user.email", StringComparison.OrdinalIgnoreCase)
            ? "The repository was created, but the initial commit failed because git does not know who you are. Set your name and email in Settings (or run: git config --global user.name \"Your Name\" and git config --global user.email you@example.com). Git said: " + gitMessage.Trim()
            : "The repository was created, but the initial commit failed: " + gitMessage.Trim();
}
