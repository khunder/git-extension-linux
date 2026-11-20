using System.Diagnostics;

namespace GitExtensionsDesktopTests;

/// <summary>
///  A bare repository acting as <c>origin</c> plus two working copies cloned from it (<see cref="Alpha"/> and <see cref="Beta"/>).
/// </summary>
internal sealed class RemoteScenario : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ge-avalonia-remote-" + Guid.NewGuid().ToString("N"));

    public RemoteScenario()
    {
        Directory.CreateDirectory(_root);
        Origin = Path.Combine(_root, "origin.git");
        Alpha = Path.Combine(_root, "alpha");
        Beta = Path.Combine(_root, "beta");

        Run(_root, "init", "--quiet", "--bare", "--initial-branch=main", Origin);
        Run(_root, "clone", "--quiet", Origin, Alpha);
        Configure(Alpha);
        Run(Alpha, "checkout", "-q", "-B", "main");
        Commit(Alpha, "a.txt", "one", "Add a.txt");
        Run(Alpha, "push", "--quiet", "--set-upstream", "origin", "main");
        Run(_root, "clone", "--quiet", Origin, Beta);
        Configure(Beta);
    }

    public string Origin { get; }

    public string Alpha { get; }

    public string Beta { get; }

    public static string Run(string directory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }

        return output.Trim();
    }

    public static void Commit(string directory, string file, string content, string message)
    {
        File.WriteAllText(Path.Combine(directory, file), content);
        Run(directory, "add", file);
        Run(directory, "commit", "--quiet", "-m", message);
    }

    public void Dispose()
    {
        try
        {
            TestFiles.DeleteTree(_root);
        }
        catch (IOException)
        {
            // Best effort cleanup of a temp folder.
        }
    }

    private static void Configure(string directory)
    {
        Run(directory, "config", "user.name", "Test Author");
        Run(directory, "config", "user.email", "test@example.com");
        Run(directory, "config", "pull.rebase", "false");
    }
}
