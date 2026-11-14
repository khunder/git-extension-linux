using System.Diagnostics;

namespace GitExtensionsDesktopTests;

/// <summary>
///  A repository for file history and blame tests: <c>story.txt</c> edited by two authors over several commits,
///  then renamed to <c>tale.txt</c> and edited again, plus an empty file, a binary file and an untracked file.
///  Line authors at HEAD of <c>tale.txt</c>: 1 Bob; 2-3 Bob; 4-5 Alice; 6-7 Alice.
/// </summary>
internal sealed class FileHistoryRepository : IDisposable
{
    public FileHistoryRepository()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ge-avalonia-fh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        Git("init", "--quiet", "--initial-branch=main");

        Write("story.txt", "line 1\nline 2\nline 3\nline 4\nline 5\n");
        Commit("Alice", "Add story", "story.txt");
        Write("story.txt", "line 1\nline two (bob)\nline three (bob)\nline 4\nline 5\n");
        Commit("Bob", "Bob edits middle", "story.txt");
        Write("story.txt", "line 1\nline two (bob)\nline three (bob)\nline 4\nline 5\nline 6\nline 7\n");
        Commit("Alice", "Append ending", "story.txt");
        Git("mv", "story.txt", "tale.txt");
        Commit("Alice", "Rename story to tale");
        Write("tale.txt", "first (bob)\nline two (bob)\nline three (bob)\nline 4\nline 5\nline 6\nline 7\n");
        Commit("Bob", "Edit first line", "tale.txt");

        Write("empty.txt", string.Empty);
        File.WriteAllBytes(System.IO.Path.Combine(Path, "blob.bin"), [1, 2, 0, 3, 0, 255, 4]);
        Commit("Alice", "Add empty and binary files", "empty.txt", "blob.bin");

        Write("untracked.txt", "not in git\n");
    }

    public string Path { get; }

    public string Head => GitOutput("rev-parse", "HEAD").Trim();

    public string HashOf(string subject)
        => GitOutput("log", "--format=%H", "--grep=^" + subject + "$").Trim();

    public string GitOutput(params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git") { WorkingDirectory = Path, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 ? output : throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of a temp folder.
        }
    }

    private void Write(string file, string content) => File.WriteAllText(System.IO.Path.Combine(Path, file), content);

    private void Commit(string author, string message, params string[] files)
    {
        foreach (string file in files)
        {
            GitOutput("add", file);
        }

        GitOutput("-c", "user.name=" + author, "-c", "user.email=" + author.ToLowerInvariant() + "@example.com", "commit", "--quiet", "-m", message);
    }

    private void Git(params string[] arguments) => _ = GitOutput(arguments);
}
