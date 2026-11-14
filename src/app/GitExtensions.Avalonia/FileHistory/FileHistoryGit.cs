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

    public static string LogFormat { get; } = $"--format={FileLogParser.RecordMarker}%H{FileLogParser.FieldSeparator}%an{FileLogParser.FieldSeparator}%aI{FileLogParser.FieldSeparator}%s";

    /// <summary>
    ///  The commits that touched <paramref name="path"/>, newest first, following renames.
    /// </summary>
    public static IReadOnlyList<FileHistoryEntry> GetHistory(GitModule module, string path, CancellationToken cancellationToken)
    {
        ExecutionResult result = module.GitExecutable.Execute(
            new GitArgumentBuilder("log", gitOptions: "-c core.quotepath=false") { "--follow", "--name-only", $"--max-count={MaxEntries}", LogFormat, "--", path.Quote() },
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
