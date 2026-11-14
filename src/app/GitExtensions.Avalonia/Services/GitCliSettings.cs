namespace GitExtensions.Desktop.Services;

/// <summary>The configured git executable, for callers of <see cref="GitCli"/>.</summary>
internal static class GitCliSettings
{
    public static string GitPath => string.IsNullOrWhiteSpace(GitCommands.AppSettings.GitCommand) ? "git" : GitCommands.AppSettings.GitCommand;
}
