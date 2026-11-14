using GitCommands;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  Throws away the changes of files of the working tree (the "Reset" of the Commit window, the "R" key of the Diff tab).
/// </summary>
internal static class WorkingTreeReset
{
    /// <summary>
    ///  Restores each file from HEAD (staged and unstaged changes), removes files that are new in the index and deletes untracked
    ///  files. Blocks: run it off the UI thread. Returns the git error text, or <see langword="null"/> on success.
    /// </summary>
    public static string? ResetFiles(GitModule module, IEnumerable<GitItemStatus> items)
    {
        bool hasHead = module.GitExecutable.Execute(new GitArgumentBuilder("rev-parse") { "--verify", "--quiet", "HEAD" }, throwOnErrorExit: false).ExitedSuccessfully;
        foreach (GitItemStatus item in items.DistinctBy(i => i.Name))
        {
            string full = Path.Combine(module.WorkingDir, item.Name);
            if (!item.IsTracked && item.Staged != StagedStatus.Index)
            {
                // Untracked: delete from disk.
                if (File.Exists(full))
                {
                    File.Delete(full);
                }
                else if (Directory.Exists(full))
                {
                    Directory.Delete(full, recursive: true);
                }

                continue;
            }

            ExecutionResult result = hasHead && !(item.IsNew && item.Staged == StagedStatus.Index)
                ? module.GitExecutable.Execute(new GitArgumentBuilder("checkout") { "HEAD", "--", item.Name.QuoteNE() }, throwOnErrorExit: false)
                : module.GitExecutable.Execute(new GitArgumentBuilder("rm") { "-f", "-q", "--", item.Name.QuoteNE() }, throwOnErrorExit: false);
            if (!result.ExitedSuccessfully)
            {
                string text = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
                return text.Trim();
            }
        }

        return null;
    }
}
