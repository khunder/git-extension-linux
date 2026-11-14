using GitCommands;
using GitExtensions.Desktop.Diff;
using GitExtensions.Extensibility;
using GitExtUtils;

namespace GitExtensions.Desktop.FileHistory;

/// <summary>
///  The (blocking) git calls behind the file history and blame views. Call from a background thread.
/// </summary>
public static class FileHistoryGit
{
    public const int MaxEntries = 5000;

    /// <summary>The most lines a blame shows.</summary>
    public const int MaxBlameLines = 250_000;

    /// <summary>The "branch" value that makes the history span every ref.</summary>
    public const string AllBranches = "--all";

    /// <summary>The largest file the View tab shows.</summary>
    public const int MaxViewBytes = 4 * 1024 * 1024;

    public static string LogFormat { get; } = $"--format={FileLogParser.RecordMarker}%H{FileLogParser.FieldSeparator}%an{FileLogParser.FieldSeparator}%aI{FileLogParser.FieldSeparator}%s{FileLogParser.FieldSeparator}%D";

    /// <summary>
    ///  The commits that touched <paramref name="path"/>, newest first, following renames.
    /// </summary>
    public static IReadOnlyList<FileHistoryEntry> GetHistory(GitModule module, string path, CancellationToken cancellationToken)
        => GetHistory(module, path, branch: null, follow: true, cancellationToken);

    /// <summary>
    ///  The commits that touched <paramref name="path"/>. <paramref name="branch"/> is a branch/revision to start from,
    ///  <see cref="AllBranches"/> for every ref, or <see langword="null"/> for HEAD.
    /// </summary>
    public static IReadOnlyList<FileHistoryEntry> GetHistory(GitModule module, string path, string? branch, bool follow, CancellationToken cancellationToken)
    {
        ExecutionResult result = module.GitExecutable.Execute(
            new GitArgumentBuilder("log", gitOptions: "-c core.quotepath=false")
            {
                { follow, "--follow" },
                "--name-only",
                "--decorate=full",
                $"--max-count={MaxEntries}",
                LogFormat,
                { !string.IsNullOrEmpty(branch), branch is null ? string.Empty : branch.Quote() },
                "--",
                path.Quote()
            },
            throwOnErrorExit: false,
            cancellationToken: cancellationToken);

        // A path git has never seen makes "log" fail with "ambiguous argument"/empty output: both mean no history.
        return result.ExitedSuccessfully ? FileLogParser.Parse(result.StandardOutput) : [];
    }

    /// <summary>
    ///  The diff the commit made to just this file (shown as a rename when it was renamed there).
    /// </summary>
    public static IReadOnlyList<DiffLine> GetFileDiff(GitModule module, FileHistoryEntry entry, CancellationToken cancellationToken)
    {
        ExecutionResult result = module.GitExecutable.Execute(
            new GitArgumentBuilder("log", gitOptions: "-c core.quotepath=false") { "-1", "--follow", "-p", "--format=", "--no-color", "--no-ext-diff", "--find-renames", entry.Hash, "--", entry.Path.Quote() },
            throwOnErrorExit: false,
            cancellationToken: cancellationToken);
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

    /// <summary>
    ///  The local branches (current one first) and the name of the current branch (null when detached).
    /// </summary>
    public static (string? Current, IReadOnlyList<string> Branches) GetBranches(GitModule module, CancellationToken cancellationToken)
    {
        ExecutionResult current = module.GitExecutable.Execute(
            new GitArgumentBuilder("symbolic-ref") { "--short", "-q", "HEAD" },
            throwOnErrorExit: false,
            cancellationToken: cancellationToken);
        string? currentName = current.ExitedSuccessfully ? current.StandardOutput.Trim() : null;
        if (string.IsNullOrEmpty(currentName))
        {
            currentName = null;
        }

        ExecutionResult all = module.GitExecutable.Execute(
            new GitArgumentBuilder("for-each-ref") { "--format=%(refname:short)", "--sort=refname", "refs/heads" },
            throwOnErrorExit: false,
            cancellationToken: cancellationToken);
        List<string> branches = all.ExitedSuccessfully
            ? [.. all.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : [];
        return (currentName, branches);
    }

    /// <summary>
    ///  The lines of <paramref name="path"/> as stored in <paramref name="revision"/>, or a message when it cannot be shown.
    /// </summary>
    public static (IReadOnlyList<string> Lines, string Message) GetFileContent(GitModule module, string revision, string path, CancellationToken cancellationToken)
    {
        ExecutionResult result = module.GitExecutable.Execute(
            new GitArgumentBuilder("show") { $"{revision}:{path}".Quote() },
            stripAnsiEscapeCodes: false,
            throwOnErrorExit: false,
            cancellationToken: cancellationToken);
        if (!result.ExitedSuccessfully)
        {
            return ([], $"The file cannot be shown at this revision: {result.StandardError.Trim()}");
        }

        string text = result.StandardOutput;
        if (text.Contains('\0'))
        {
            return ([], "Binary file: no preview.");
        }

        if (text.Length > MaxViewBytes)
        {
            return ([], $"The file is too large to show ({text.Length / 1024:N0} KB).");
        }

        if (text.Length == 0)
        {
            return ([], "The file is empty.");
        }

        List<string> lines = [.. text.Split('\n').Select(line => line.TrimEnd('\r'))];
        if (lines.Count > 1 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return (lines, string.Empty);
    }

    /// <summary>
    ///  Blames <paramref name="path"/> as of <paramref name="revision"/>.
    /// </summary>
    public static BlameResult GetBlame(GitModule module, string path, string revision, CancellationToken cancellationToken)
    {
        ExecutionResult result = module.GitExecutable.Execute(
            new GitArgumentBuilder("blame") { "--porcelain", revision.Quote(), "--", path.Quote() },
            stripAnsiEscapeCodes: false,
            throwOnErrorExit: false,
            cancellationToken: cancellationToken);

        if (!result.ExitedSuccessfully)
        {
            string error = result.StandardError.Trim();
            return error.Contains("no such path", StringComparison.OrdinalIgnoreCase) || error.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                ? new BlameResult([], "No blame available: the file is not tracked at this revision.")
                : new BlameResult([], $"Blame failed: {error}");
        }

        if (result.StandardOutput.Contains('\0'))
        {
            return new BlameResult([], "Binary file: no blame shown.");
        }

        IReadOnlyList<BlameEntry> entries = BlameParser.Parse(result.StandardOutput);
        if (entries.Count > MaxBlameLines)
        {
            // The notice is a last, uncommitted-looking row (all-zero hash) so it does not cover the lines like the message overlay would.
            BlameEntry notice = new(0, new string('0', 40), string.Empty, DateTimeOffset.UnixEpoch, string.Empty, $"… file too large: showing the first {MaxBlameLines:N0} of {entries.Count:N0} lines.");
            return new BlameResult([.. entries.Take(MaxBlameLines), notice], string.Empty);
        }

        return new BlameResult(entries, entries.Count == 0 ? "The file is empty." : string.Empty);
    }
}

/// <summary>
///  Blame lines, or a <see cref="Message"/> explaining why there are none.
/// </summary>
public sealed record BlameResult(IReadOnlyList<BlameEntry> Entries, string Message);
