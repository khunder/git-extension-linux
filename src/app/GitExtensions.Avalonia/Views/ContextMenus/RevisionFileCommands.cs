using System.Diagnostics;

namespace GitExtensions.Desktop.Views.ContextMenus;

/// <summary>
///  The git work behind the file menu of a commit's files (Diff tab, Commit tab, File tree), after the original's
///  RevisionDiff menu: reset the files to a revision, cherry-pick a file's changes, the difftool, open or save a file as it
///  was in a revision and rename a working directory file. Every method returns the failure message, or null.
/// </summary>
public static class RevisionFileCommands
{
    /// <summary>The tree of an empty repository: the "parent" of a root commit.</summary>
    public const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    /// <summary>The revision the changes are compared with: <paramref name="baseRevision"/>, else the first parent, else the empty tree.</summary>
    public static async Task<string> ResolveBaseAsync(string workingDir, string revision, string? baseRevision)
    {
        string candidate = baseRevision ?? revision + "^";
        (int exit, string output, _) = await RunAsync(workingDir, ["rev-parse", "--verify", "--quiet", candidate + "^{commit}"]);
        return exit == 0 && output.Trim().Length > 0 ? output.Trim() : EmptyTree;
    }

    /// <summary>
    ///  Makes the working directory and the index hold the files as they are in <paramref name="revision"/>
    ///  (<c>git checkout rev -- path</c>); a file that does not exist there is removed.
    /// </summary>
    public static async Task<string?> ResetToAsync(string workingDir, string revision, IReadOnlyList<string> paths)
    {
        foreach (string path in paths)
        {
            bool exists = revision != EmptyTree && (await RunAsync(workingDir, ["cat-file", "-e", $"{revision}:{path}"])).Exit == 0;
            (int exit, _, string error) = exists
                ? await RunAsync(workingDir, ["checkout", revision, "--", path])
                : await RunAsync(workingDir, ["rm", "-f", "-q", "--ignore-unmatch", "--", path]);
            if (exit != 0)
            {
                return error.Trim();
            }
        }

        return null;
    }

    /// <summary>Applies what <paramref name="revision"/> changed in the files (against <paramref name="baseRevision"/>) to the working directory, with a three-way merge.</summary>
    public static async Task<string?> CherryPickChangesAsync(string workingDir, string baseRevision, string revision, IReadOnlyList<string> paths)
    {
        (int exit, string patch, string error) = await RunAsync(workingDir, ["diff", "--binary", baseRevision, revision, "--", .. paths]);
        if (exit != 0)
        {
            return error.Trim();
        }

        if (patch.Length == 0)
        {
            return "There are no changes to apply.";
        }

        string file = Path.Combine(Path.GetTempPath(), $"ge-cherry-pick-{Guid.NewGuid():N}.patch");
        try
        {
            await File.WriteAllTextAsync(file, patch);
            (exit, _, error) = await RunAsync(workingDir, ["apply", "--3way", "--whitespace=nowarn", file]);
            return exit == 0 ? null : error.Trim();
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>Starts the configured difftool on one file: <paramref name="right"/> null compares with the working directory.</summary>
    public static async Task<string?> DifftoolAsync(string workingDir, string left, string? right, string path)
    {
        (int exit, string tool, _) = await RunAsync(workingDir, ["config", "--get", "diff.tool"]);
        if (exit != 0 || tool.Trim().Length == 0)
        {
            return "There is no difftool configured (Settings > Git > Config: diff.tool).";
        }

        try
        {
            ProcessStartInfo info = Git(workingDir, right is null ? ["difftool", "--no-prompt", left, "--", path] : ["difftool", "--no-prompt", left, right, "--", path]);
            _ = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Writes the file as it is in <paramref name="revision"/> to <paramref name="target"/>.</summary>
    public static async Task<string?> SaveAsync(string workingDir, string revision, string path, string target)
    {
        try
        {
            ProcessStartInfo info = Git(workingDir, ["show", $"{revision}:{path}"]);
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            using Process process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
            Task<string> error = process.StandardError.ReadToEndAsync();
            await using (FileStream output = File.Create(target))
            {
                await process.StandardOutput.BaseStream.CopyToAsync(output);
            }

            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                File.Delete(target);
                return (await error).Trim();
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Saves the file of <paramref name="revision"/> under a temporary folder (keeping its name, so it opens with the right program) and returns that path.</summary>
    public static async Task<(string? File, string? Error)> SaveTemporaryAsync(string workingDir, string revision, string path)
    {
        string prefix = revision.Length > 8 ? revision[..8] : revision;
        string folder = Path.Combine(Path.GetTempPath(), "GitExtensions", prefix);
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, Path.GetFileName(path));
        string? error = await SaveAsync(workingDir, revision, path, target);
        return error is null ? (target, null) : (null, error);
    }

    /// <summary>Renames or moves a working directory file (<c>git mv</c>).</summary>
    public static async Task<string?> MoveAsync(string workingDir, string from, string to)
    {
        string? folder = Path.GetDirectoryName(Path.Combine(workingDir, to));
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
        }

        (int exit, _, string error) = await RunAsync(workingDir, ["mv", "--", from, to]);
        return exit == 0 ? null : error.Trim();
    }

    private static ProcessStartInfo Git(string workingDir, IEnumerable<string> arguments)
    {
        ProcessStartInfo info = new("git") { WorkingDirectory = workingDir, UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(string workingDir, IEnumerable<string> arguments)
    {
        try
        {
            ProcessStartInfo info = Git(workingDir, arguments);
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            using Process process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return (process.ExitCode, await output, await error);
        }
        catch (Exception ex)
        {
            return (-1, string.Empty, ex.Message);
        }
    }
}
