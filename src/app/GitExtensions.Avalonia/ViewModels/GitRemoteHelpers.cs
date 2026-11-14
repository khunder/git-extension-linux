using GitCommands;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>Small git queries shared by the pull and push dialogs (run them off the UI thread).</summary>
internal static class GitRemoteHelpers
{
    public static IReadOnlyList<string> RemoteBranches(GitModule module, string remote)
    {
        var result = module.GitExecutable.Execute($"for-each-ref --format=%(refname:short) refs/remotes/{remote}/", throwOnErrorExit: false);
        string prefix = remote + "/";
        return result.ExitedSuccessfully
            ? [.. result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name != prefix + "HEAD")
                .Select(name => name[prefix.Length..])]
            : [];
    }

    public static IReadOnlyList<string> LocalBranches(GitModule module)
    {
        var result = module.GitExecutable.Execute("for-each-ref --format=%(refname:short) refs/heads/", throwOnErrorExit: false);
        return result.ExitedSuccessfully
            ? [.. result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : [];
    }

    /// <summary>Gets the branch (without "refs/heads/") that <paramref name="branch"/> tracks on <paramref name="remote"/>, if any.</summary>
    public static string? UpstreamBranch(GitModule module, string branch, string? remote)
    {
        string? configuredRemote = Config(module, $"branch.{branch}.remote");
        string? merge = Config(module, $"branch.{branch}.merge");
        return merge is not null && (remote is null || configuredRemote == remote) ? merge.Replace("refs/heads/", string.Empty, StringComparison.Ordinal) : null;
    }

    public static string? UpstreamRemote(GitModule module, string branch) => Config(module, $"branch.{branch}.remote");

    public static bool IsShallow(GitModule module)
    {
        var result = module.GitExecutable.Execute("rev-parse --is-shallow-repository", throwOnErrorExit: false);
        return result.ExitedSuccessfully && result.StandardOutput.Trim() == "true";
    }

    private static string? Config(GitModule module, string key)
    {
        var result = module.GitExecutable.Execute($"config --get {key}", throwOnErrorExit: false);
        string value = result.StandardOutput.Trim();
        return result.ExitedSuccessfully && value.Length > 0 ? value : null;
    }
}
