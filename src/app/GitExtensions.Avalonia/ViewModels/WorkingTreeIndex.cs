using GitCommands;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  Index and .gitignore operations on changed files, shared by the Commit window and the Changes tab. They block (run
///  them off the UI thread) and return git's error output, or <see langword="null"/> on success.
/// </summary>
public static class WorkingTreeIndex
{
    public static string? Stage(GitModule module, IEnumerable<ChangedFileViewModel> files)
        => module.StageFiles([.. Items(files)], out string output) ? null : output;

    public static string? Unstage(GitModule module, IEnumerable<ChangedFileViewModel> files)
        => module.UnstageFiles([.. Items(files)], out string output) ? null : output;

    /// <summary>
    ///  Appends <paramref name="patterns"/> (e.g. "/dir/file" or "*.log") to the .gitignore of <paramref name="workingDir"/>,
    ///  skipping the ones already listed and creating the file when missing.
    /// </summary>
    public static void AppendToGitIgnore(string workingDir, IEnumerable<string> patterns)
    {
        string path = Path.Combine(workingDir, ".gitignore");
        string existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        HashSet<string> lines = [.. existing.Split('\n').Select(l => l.TrimEnd('\r'))];
        string prefix = existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : string.Empty;
        string add = string.Concat(patterns.Distinct().Where(p => !lines.Contains(p)).Select(p => p + "\n"));
        if (add.Length > 0)
        {
            File.AppendAllText(path, prefix + add);
        }
    }

    // Status-only entries (e.g. a submodule's summary) cannot be staged.
    private static IEnumerable<GitItemStatus> Items(IEnumerable<ChangedFileViewModel> files)
        => files.Select(f => f.Item).Where(i => !i.IsStatusOnly);
}
