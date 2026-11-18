using System.Diagnostics;

namespace GitExtensionsDesktopTests;

/// <summary>
///  A throwaway git repository with a small, known history.
/// </summary>
internal sealed class TestRepository : IDisposable
{
    public TestRepository()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ge-avalonia-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);

        Git("init", "--quiet", "--initial-branch=main");
        Commit("a.txt", "first", "Add a.txt");
        Commit("b.txt", "second", "Add b.txt");
        Git("tag", "v1.0");

        // Working tree changes: one modified tracked file, one new untracked file.
        File.WriteAllText(System.IO.Path.Combine(Path, "a.txt"), "changed");
        File.WriteAllText(System.IO.Path.Combine(Path, "c.txt"), "new");
    }

    public string Path { get; }

    /// <summary>
    ///  Runs git in the repository and returns its standard output.
    /// </summary>
    public string GitOutput(params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git") { WorkingDirectory = Path, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return output;
    }

    /// <summary>
    ///  Adds branches <c>develop</c> and <c>feature/x</c> (both at HEAD), a fake remote <c>origin</c> with
    ///  <c>origin/main</c> and <c>origin/feature/y</c>, and an extra tag <c>release/2.0</c>. Stays on <c>main</c>.
    /// </summary>
    public void AddBranchesRemoteAndTags()
    {
        Git("branch", "develop");
        Git("branch", "feature/x");
        Git("tag", "release/2.0");
        Git("remote", "add", "origin", "https://example.invalid/repo.git");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");
        Git("update-ref", "refs/remotes/origin/feature/y", "HEAD");
    }

    public string RunGit(params string[] arguments)
    {
        Git(arguments);
        return string.Empty;
    }

    public void Dispose()
    {
        try
        {
            // Git writes its object files read-only; on Windows that blocks a recursive delete.
            foreach (string file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup of a temp folder.
        }
    }

    /// <summary>
    ///  Adds two commits on top of the base history: one adding a file with a long content, and one with a multi-line
    ///  message that deletes <c>b.txt</c>, renames that file and modifies <c>a.txt</c>.
    /// </summary>
    public void AddRichHistory()
    {
        Commit("d.txt", string.Join('\n', Enumerable.Range(1, 20).Select(i => "line " + i)), "Add d.txt");

        Git("rm", "--quiet", "b.txt");
        Git("mv", "d.txt", "e.txt");
        File.WriteAllText(System.IO.Path.Combine(Path, "a.txt"), "committed change");
        Git("add", "a.txt");
        Git("-c", "user.name=Test Author", "-c", "user.email=test@example.com", "commit", "--quiet", "-m", "Rework files", "-m", "First body paragraph.\nSecond body line.", "-m", "Final paragraph.");
    }

    private void Commit(string file, string content, string message)
    {
        File.WriteAllText(System.IO.Path.Combine(Path, file), content);
        Git("add", file);
        Git("-c", "user.name=Test Author", "-c", "user.email=test@example.com", "commit", "--quiet", "-m", message);
    }

    private void Git(params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git") { WorkingDirectory = Path, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {process.StandardError.ReadToEnd()}");
        }
    }
}
