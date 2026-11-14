using System.Diagnostics;
using System.Text;
using GitExtensions.Extensibility;

namespace GitExtensions.Desktop.Services;

/// <summary>The outcome of one git invocation.</summary>
public readonly record struct GitRunResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>The most useful text to show when the command failed.</summary>
    public string Message => (string.IsNullOrWhiteSpace(Error) ? Output : Error).Trim();
}

/// <summary>
///  Runs git for merge, rebase and conflict resolution. Git can never open an editor or wait for input:
///  the editors are replaced by <c>true</c> and stdin is closed.
/// </summary>
public static class GitOperationRunner
{
    public static async Task<GitRunResult> RunAsync(IExecutable git, params string[] arguments)
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
        startInfo.Environment["GIT_EDITOR"] = "true";
        startInfo.Environment["GIT_SEQUENCE_EDITOR"] = "true";
        startInfo.Environment["GIT_MERGE_AUTOEDIT"] = "no";

        if (!string.IsNullOrWhiteSpace(git.PrefixArguments))
        {
            startInfo.Arguments = git.PrefixArguments + " " + string.Join(' ', arguments.Select(Quote));
        }
        else
        {
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        using Process process = await Task.Run(() => Process.Start(startInfo)) ?? throw new InvalidOperationException("The git process did not start.");
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new GitRunResult(process.ExitCode, await output, await error);
    }

    private static string Quote(string argument)
        => argument.Length > 0 && argument.IndexOfAny([' ', '"', '\t']) < 0 ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";
}
