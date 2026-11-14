using System.Diagnostics;
using System.Text;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  Minimal one-shot git invocation for settings (version check, <c>git config</c>). Never prompts.
/// </summary>
public static class GitCli
{
    public readonly record struct Result(int ExitCode, string Output, string Error)
    {
        public bool Succeeded => ExitCode == 0;
    }

    /// <summary>
    ///  Runs <paramref name="gitPath"/> with the arguments. Throws if the program cannot be started.
    /// </summary>
    public static async Task<Result> RunAsync(string gitPath, string? workingDir, params string[] arguments)
    {
        ProcessStartInfo startInfo = new(gitPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (!string.IsNullOrEmpty(workingDir))
        {
            startInfo.WorkingDirectory = workingDir;
        }

        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = await Task.Run(() => Process.Start(startInfo)) ?? throw new InvalidOperationException("The process did not start.");
        process.StandardInput.Close();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new Result(process.ExitCode, (await output).Trim(), (await error).Trim());
    }
}
