using System.Diagnostics;
using System.Runtime.CompilerServices;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensionsDesktopTests;

/// <summary>Helpers for the repository tools tests (reflog, submodules, worktrees).</summary>
internal static class ToolsTestSupport
{
    // Local-path submodules need file transport; allow it for every git started by the tests (inherited by the app's git calls).
    [ModuleInitializer]
    internal static void AllowFileProtocol()
    {
        Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", "1");
        Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", "protocol.file.allow");
        Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", "always");
    }

    public sealed class Context
    {
        public required ToolsContext Tools { get; init; }

        public required GitModule Module { get; init; }

        public List<string> Errors { get; } = [];

        public List<string> Statuses { get; } = [];

        public List<string> Opened { get; } = [];

        public int Refreshes { get; set; }
    }

    public static Context CreateContext(TestRepository repository)
    {
        repository.ConfigureIdentity();
        GitModule module = GitEnvironment.Initialize().OpenRepository(repository.Path)!;
        Context[] holder = new Context[1];
        ToolsContext tools = new(
            () => module,
            () =>
            {
                holder[0].Refreshes++;
                return Task.CompletedTask;
            },
            message => holder[0].Errors.Add(message),
            message => holder[0].Statuses.Add(message),
            path =>
            {
                holder[0].Opened.Add(path);
                return Task.CompletedTask;
            });
        holder[0] = new Context { Tools = tools, Module = module };
        return holder[0];
    }

    /// <summary>Commits a file in the repository.</summary>
    public static string Commit(this TestRepository repository, string file, string content, string message)
    {
        repository.ConfigureIdentity();
        File.WriteAllText(Path.Combine(repository.Path, file), content);
        repository.RunGit("add", file);
        repository.RunGit("commit", "--quiet", "-m", message);
        return repository.HeadId();
    }

    public static string HeadId(this TestRepository repository) => repository.GitOutput("rev-parse", "HEAD").Trim();

    /// <summary>Discards the working tree changes the shared test repository starts with.</summary>
    public static void CleanWorkingTree(this TestRepository repository)
    {
        repository.RunGit("checkout", "--", ".");
        repository.RunGit("clean", "-fdq");
    }

    /// <summary>Runs git in any folder and returns its output; throws when git fails.</summary>
    public static string RunIn(string directory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        string error = string.Empty;
        process.ErrorDataReceived += (_, e) => error += e.Data + "\n";
        process.BeginErrorReadLine();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error}");
        }

        return output;
    }

    public static string MakeTempFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "ge-avalonia-tools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteFolder(string path)
    {
        try
        {
            // Git marks some files read-only.
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    ///  A superproject with one local-path submodule <c>lib</c> (cloned into place by <c>submodule add</c>) and the library repository itself.
    /// </summary>
    public sealed class SubmoduleScenario : IDisposable
    {
        public SubmoduleScenario()
        {
            Root = MakeTempFolder();
            LibPath = Path.Combine(Root, "lib-origin");
            Directory.CreateDirectory(LibPath);
            RunIn(LibPath, "init", "--quiet", "--initial-branch=main");
            File.WriteAllText(Path.Combine(LibPath, "lib.txt"), "lib");
            RunIn(LibPath, "add", ".");
            Commit(LibPath, "Lib initial");

            Super = new TestRepository();
            Super.CleanWorkingTree();
            Super.RunGit("submodule", "add", "--quiet", LibPath, "lib");
            Super.ConfigureIdentity();
            Super.RunGit("commit", "--quiet", "-m", "Add lib submodule");
        }

        public string Root { get; }

        public string LibPath { get; }

        public TestRepository Super { get; }

        public string SubmoduleFolder => Path.Combine(Super.Path, "lib");

        public static void Commit(string directory, string message)
            => RunIn(directory, "-c", "user.name=T", "-c", "user.email=t@e.com", "commit", "--quiet", "--allow-empty", "-m", message);

        /// <summary>A clone of the superproject in which the submodule is not initialized yet.</summary>
        public string CloneSuper()
        {
            string target = Path.Combine(Root, "clone-" + Guid.NewGuid().ToString("N")[..6]);
            RunIn(Root, "clone", "--quiet", Super.Path, target);
            return target;
        }

        public void Dispose()
        {
            Super.Dispose();
            DeleteFolder(Root);
        }
    }
}
