using GitCommands;
using GitExtensions.Desktop.Diff;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;
using GitUIPluginInterfaces;

namespace GitExtensions.Desktop.ViewModels;

// Compare a commit with the working directory (the tracked files as they are on disk now).
public sealed partial class RevisionDiffViewModel
{
    /// <summary>Shows what changed between <paramref name="from"/> and the working directory.</summary>
    public Task ShowDiffToWorkingDirectoryAsync(GitModule module, ObjectId from)
    {
        string sha = from.ToString();
        return ShowAsync(
            null,
            string.Empty,
            $"{from.ToShortString()} ↔ working directory",
            cancellationToken => module.GetDiffFilesWithUntracked(sha, GitRevision.WorkTreeGuid, StagedStatus.None, noCache: true, cancellationToken: cancellationToken),
            (file, cancellationToken) => Task.Run(
                () =>
                {
                    GitArgumentBuilder arguments = new("diff", gitOptions: "-c core.quotepath=false") { "--no-color", "--no-ext-diff", "--find-renames", sha, "--", file.Name.Quote() };
                    ExecutionResult result = module.GitExecutable.Execute(arguments, throwOnErrorExit: false, cancellationToken: cancellationToken);
                    if (!result.ExitedSuccessfully)
                    {
                        return (IReadOnlyList<DiffLine>)[new DiffLine(DiffLineKind.Info, result.StandardError.Trim(), null, null)];
                    }

                    List<DiffLine> lines = [.. UnifiedDiffParser.Parse(result.StandardOutput)];
                    return lines.Count == 0 ? [new DiffLine(DiffLineKind.Info, file.Item.IsNew ? "Untracked file." : "No textual changes.", null, null)] : lines;
                },
                cancellationToken));
    }
}
