using System.Diagnostics;
using System.Text;
using GitCommands.Git.Extensions;
using GitExtensions.Extensibility;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  Runs a git command and streams its stdout and stderr as they are produced. Never lets git prompt for credentials.
/// </summary>
public static class GitProcessRunner
{
    /// <summary>
    ///  Gets or sets the provider used for commands that are not given an explicit <see cref="AskPassOperation"/>.
    ///  The application sets it so every command it starts can prompt in the window.
    /// </summary>
    public static IAskPassProvider? DefaultAskPass { get; set; }

    /// <summary>
    ///  Runs git with <paramref name="arguments"/> in the executable's working directory.
    /// </summary>
    /// <param name="onOutput">Called from background threads with raw chunks (including carriage returns of progress meters).</param>
    /// <param name="askPass">The askpass registration whose environment is given to git; when null, <see cref="DefaultAskPass"/> is used if set.</param>
    /// <returns>The exit code. If <paramref name="cancellationToken"/> fires, the process tree is killed and an
    /// <see cref="OperationCanceledException"/> is thrown.</returns>
    public static async Task<int> RunAsync(IExecutable git, IReadOnlyList<string> arguments, Action<string> onOutput, CancellationToken cancellationToken, AskPassOperation? askPass = null)
    {
        using AskPassOperation? ownOperation = askPass is null ? DefaultAskPass?.Begin(cancellationToken) : null;
        askPass ??= ownOperation;

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

        // Fail fast with an authentication error instead of waiting for input nobody can give.
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "never";

        // Credential and passphrase requests go to the in-window prompt through the askpass helper.
        if (askPass is not null)
        {
            foreach ((string name, string value) in askPass.Environment)
            {
                startInfo.Environment[name] = value;
            }
        }

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

        using Process process = new() { StartInfo = startInfo };
        process.Start();
        process.StandardInput.Close();

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            try
            {
                process.TerminateTree();
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
        });

        Task stdout = PumpAsync(process.StandardOutput, onOutput);
        Task stderr = PumpAsync(process.StandardError, onOutput);
        await Task.WhenAll(stdout, stderr);
        await process.WaitForExitAsync(CancellationToken.None);

        cancellationToken.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    private static Task PumpAsync(StreamReader reader, Action<string> onOutput)
        => Task.Run(async () =>
        {
            char[] buffer = new char[1024];
            int read;
            while ((read = await reader.ReadAsync(buffer)) > 0)
            {
                onOutput(new string(buffer, 0, read));
            }
        });

    private static string Quote(string argument)
        => argument.Length > 0 && argument.IndexOfAny([' ', '"', '\t']) < 0 ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";
}
