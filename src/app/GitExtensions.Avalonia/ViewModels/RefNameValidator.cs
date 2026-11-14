using GitCommands;
using GitExtensions.Extensibility;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  Checks branch and tag names: cheap local rules first, then <c>git check-ref-format</c> as the authority.
/// </summary>
public static class RefNameValidator
{
    /// <summary>Rules that need no git; returns an explanation, or <see langword="null"/> when the name passes.</summary>
    public static string? PreCheck(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "The name must not be empty.";
        }

        if (name.StartsWith('-'))
        {
            return "The name must not start with '-'.";
        }

        if (name.Any(char.IsWhiteSpace))
        {
            return "The name must not contain spaces.";
        }

        if (name.Contains('"') || name.Contains('\''))
        {
            return "The name must not contain quotes.";
        }

        if (name.StartsWith("@{", StringComparison.Ordinal))
        {
            return "The name must not be a revision expression.";
        }

        return null;
    }

    /// <summary>
    ///  Validates a name with git (blocks: call off the UI thread).
    /// </summary>
    /// <returns>A message describing the problem, or <see langword="null"/> when the name is valid.</returns>
    public static string? Validate(IExecutable git, string? name, bool isTag = false)
    {
        string? problem = PreCheck(name);
        if (problem is not null)
        {
            return problem;
        }

        ExecutionResult result = git.Execute(
            isTag ? $"check-ref-format \"refs/tags/{name}\"" : $"check-ref-format --branch \"{name}\"",
            throwOnErrorExit: false);
        return result.ExitedSuccessfully ? null : $"'{name}' is not a valid {(isTag ? "tag" : "branch")} name.";
    }
}
