using System.Diagnostics;
using System.Text;
using GitExtensions.Extensibility;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  Runs git for the history editing commands (fixup, squash, edit, reword, autosquash). Git never waits for an editor:
///  the sequence editor and the message editor are replaced by the given commands and stdin is closed.
/// </summary>
public static class GitHistoryRunner
{
    /// <summary>A sequence editor that accepts the todo list as git prepared it (what <c>--autosquash</c> needs).</summary>
    public const string AcceptTodo = "true";

    /// <summary>The sequence editor that turns the first <c>pick</c> of the todo list into <paramref name="command"/> (edit, reword).</summary>
    public static string ChangeFirstPick(string command) => $"sed -i -re '0,/pick/s//{command}/'";

    /// <summary>A message editor that replaces git's prepared message by the content of <paramref name="messageFile"/>.</summary>
    public static string UseMessageFile(string messageFile) => $"cp '{messageFile.Replace("'", "'\\''")}'";

    public static async Task<GitRunResult> RunAsync(IExecutable git, string? sequenceEditor, string? messageEditor, params string[] arguments)
    {
        ProcessStartInfo startInfo = new(git.Command)
        {
            WorkingDirectory = git.WorkingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";
        startInfo.Environment["GIT_EDITOR"] = messageEditor ?? "true";
        startInfo.Environment["GIT_SEQUENCE_EDITOR"] = sequenceEditor ?? AcceptTodo;

        foreach (string argument in git.PrefixArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = await Task.Run(() => Process.Start(startInfo)) ?? throw new InvalidOperationException("The git process did not start.");
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new GitRunResult(process.ExitCode, await output, await error);
    }
}
