using System.Text;
using GitCommands;
using GitExtensions.Desktop.Diff;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;
using GitUIPluginInterfaces;

namespace GitExtensions.Desktop.Revision;

/// <summary>The content of a file at a revision, or the reason it cannot be shown.</summary>
public sealed record FileContentResult(IReadOnlyList<string> Lines, string Message);

/// <summary>
///  The (blocking) git calls behind the revision tabs. Call from a background thread.
/// </summary>
public static class RevisionGit
{
    /// <summary>The most entries the file tree lists.</summary>
    public const int MaxTreeEntries = 200_000;

    /// <summary>The largest file whose content is shown.</summary>
    public const long MaxFileBytes = 2L * 1024 * 1024;

    /// <summary>The most lines of a file shown.</summary>
    public const int MaxFileLines = 50_000;

    public static IReadOnlyList<string> GetContainingBranches(GitModule module, string revision, CancellationToken cancellationToken)
    {
        ExecutionResult result = module.GitExecutable.Execute(
            new GitArgumentBuilder("branch") { "-a", "--contains", revision, "--format=%(refname)" },
            throwOnErrorExit: false,
            cancellationToken: cancellationToken);
        return result.ExitedSuccessfully ? RefListParser.ParseBranches(result.StandardOutput) : [];
    }

    public static IReadOnlyList<string> GetContainingTags(GitModule module, string revision, CancellationToken cancellationToken)
    {
        ExecutionResult result = module.GitExecutable.Execute(
            new GitArgumentBuilder("tag") { "--contains", revision },
            throwOnErrorExit: false,
            cancellationToken: cancellationToken);
        return result.ExitedSuccessfully ? RefListParser.ParseTags(result.StandardOutput) : [];
    }

    public static DescribeParser.Description? Describe(GitModule module, string revision, CancellationToken cancellationToken)
    {
        ExecutionResult result = module.GitExecutable.Execute(
            new GitArgumentBuilder("describe") { "--tags", "--long", revision },
            throwOnErrorExit: false,
            cancellationToken: cancellationToken);
        return result.ExitedSuccessfully ? DescribeParser.Parse(result.StandardOutput) : null;
    }

    /// <summary>The commit a tag points at (peeling annotated tags), or <see langword="null"/>.</summary>
    public static ObjectId? GetTagCommit(GitModule module, string tag, CancellationToken cancellationToken)
    {
        ExecutionResult result = module.GitExecutable.Execute(
            new GitArgumentBuilder("rev-parse") { "--verify", "--quiet", $"refs/tags/{tag}^{{commit}}".Quote() },
            throwOnErrorExit: false,
            cancellationToken: cancellationToken);
        return result.ExitedSuccessfully && ObjectId.TryParse(result.StandardOutput.Trim(), out ObjectId id) ? id : null;
    }

    /// <summary>The web address of the origin remote, or <see langword="null"/>.</summary>
    public static string? GetRepositoryWebUrl(GitModule module, CancellationToken cancellationToken)
    {
        ExecutionResult result = module.GitExecutable.Execute(
            new GitArgumentBuilder("config") { "--get", "remote.origin.url" },
            throwOnErrorExit: false,
            cancellationToken: cancellationToken);
        return result.ExitedSuccessfully ? MessageLinker.GetRepositoryWebUrl(result.StandardOutput) : null;
    }

    /// <summary>
    ///  Every file of the tree at <paramref name="revision"/> (slash separated, not quoted), sorted by git.
    /// </summary>
    public static (IReadOnlyList<string> Paths, bool Truncated, string? Error) ListTree(GitModule module, string revision, CancellationToken cancellationToken)
    {
        ExecutionResult result = module.GitExecutable.Execute(
            new GitArgumentBuilder("ls-tree") { "-r", "-z", "--name-only", revision },
            outputEncoding: Encoding.UTF8,
            stripAnsiEscapeCodes: false,
            throwOnErrorExit: false,
            cancellationToken: cancellationToken);
        if (!result.ExitedSuccessfully)
        {
            return ([], false, result.StandardError.Trim());
        }

        string[] paths = result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        return paths.Length > MaxTreeEntries ? ([.. paths.Take(MaxTreeEntries)], true, null) : (paths, false, null);
    }

    public static FileContentResult ReadFile(GitModule module, string revision, string path, CancellationToken cancellationToken)
    {
        string spec = $"{revision}:{path}".Quote();
        ExecutionResult size = module.GitExecutable.Execute(
            new GitArgumentBuilder("cat-file") { "-s", spec },
            throwOnErrorExit: false,
            cancellationToken: cancellationToken);
        if (!size.ExitedSuccessfully || !long.TryParse(size.StandardOutput.Trim(), out long bytes))
        {
            return new FileContentResult([], "The file is not available at this revision (it may be a submodule).");
        }

        if (bytes > MaxFileBytes)
        {
            return new FileContentResult([], $"File too large to show ({bytes / 1024.0 / 1024.0:0.#} MB; the limit is {MaxFileBytes / 1024 / 1024} MB).");
        }

        ExecutionResult content = module.GitExecutable.Execute(
            new GitArgumentBuilder("cat-file") { "blob", spec },
            outputEncoding: Encoding.UTF8,
            stripAnsiEscapeCodes: false,
            throwOnErrorExit: false,
            cancellationToken: cancellationToken);
        if (!content.ExitedSuccessfully)
        {
            return new FileContentResult([], content.StandardError.Trim());
        }

        return ToLines(content.StandardOutput);
    }

    /// <summary>Splits file text into lines; NUL bytes mean a binary file.</summary>
    public static FileContentResult ToLines(string text)
    {
        if (text.Contains('\0'))
        {
            return new FileContentResult([], "Binary file");
        }

        if (text.Length == 0)
        {
            return new FileContentResult([], "The file is empty.");
        }

        List<string> lines = [.. text.Split('\n').Select(line => line.TrimEnd('\r'))];
        if (lines.Count > 1 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count > MaxFileLines)
        {
            int total = lines.Count;
            lines = [.. lines.Take(MaxFileLines)];
            return new FileContentResult(lines, $"Showing the first {MaxFileLines:N0} of {total:N0} lines.");
        }

        return new FileContentResult(lines, string.Empty);
    }

    /// <summary>
    ///  The files changed by <paramref name="to"/> relative to <paramref name="from"/> (null: its first parent; root commits list what they added).
    /// </summary>
    public static IReadOnlyList<GitItemStatus> GetChangedFiles(GitModule module, string? from, string to, bool hasParent, CancellationToken cancellationToken)
    {
        if (from is not null || hasParent)
        {
            return module.GetDiffFilesWithUntracked(from ?? $"{to}^", to, StagedStatus.None, noCache: true, cancellationToken: cancellationToken);
        }

        ExecutionResult result = module.GitExecutable.Execute(
            new GitArgumentBuilder("diff-tree", gitOptions: "-c core.quotepath=false") { "--root", "--no-commit-id", "-r", "--raw", "-z", "--find-renames", to },
            throwOnErrorExit: false,
            cancellationToken: cancellationToken);

        // "-z" raw output is ":<modes> <ids> A\0path\0..."; every file of a root commit is an addition.
        return [.. result.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => !entry.StartsWith(':'))
            .Select(path => new GitItemStatus(path) { IsNew = true })];
    }

    /// <summary>
    ///  The unified diff of one file between two revisions (<paramref name="from"/> null: against the first parent, or the empty tree for a root commit).
    /// </summary>
    public static IReadOnlyList<DiffLine> GetFileDiff(GitModule module, string? from, string to, bool hasParent, GitItemStatus file, CancellationToken cancellationToken)
    {
        GitArgumentBuilder arguments;
        if (from is not null || hasParent)
        {
            arguments = new GitArgumentBuilder("diff", gitOptions: "-c core.quotepath=false") { "--no-color", "--no-ext-diff", "--find-renames", from ?? $"{to}^", to, "--" };
        }
        else
        {
            arguments = new GitArgumentBuilder("diff-tree", gitOptions: "-c core.quotepath=false") { "--root", "-p", "--no-commit-id", "--no-color", "--no-ext-diff", "--find-renames", to, "--" };
        }

        // Both names, or git would show a rename as a plain addition.
        arguments.Add(file.Name.Quote());
        if (file.IsRenamed && !string.IsNullOrEmpty(file.OldName))
        {
            arguments.Add(file.OldName.Quote());
        }

        ExecutionResult result = module.GitExecutable.Execute(arguments, throwOnErrorExit: false, cancellationToken: cancellationToken);
        if (!result.ExitedSuccessfully)
        {
            return [new DiffLine(DiffLineKind.Info, result.StandardError.Trim(), null, null)];
        }

        List<DiffLine> lines = [.. UnifiedDiffParser.Parse(result.StandardOutput)];
        if (result.StandardOutput.Contains("Binary files ", StringComparison.Ordinal) || result.StandardOutput.Contains("GIT binary patch", StringComparison.Ordinal))
        {
            lines.Add(new DiffLine(DiffLineKind.Info, "Binary file differs.", null, null));
        }

        return lines;
    }
}
