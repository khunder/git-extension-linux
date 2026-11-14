using System.Diagnostics;
using Avalonia.Controls;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.Views.ContextMenus;

/// <summary>
///  The file-list menu commands that run programs or git on working directory files: open with a program, edit,
///  stop tracking and delete. Failures are shown in a notice dialog; <see cref="FileMenuContext.Changed"/> refreshes the lists.
/// </summary>
public static class FileCommands
{
    private static readonly string[] TerminalEditors = ["vi", "vim", "nvim", "nano", "pico", "emacs", "ed", "micro", "joe"];

    /// <summary>The program last typed in "Open with…", offered again next time.</summary>
    public static string LastOpenWith { get; set; } = string.Empty;

    /// <summary>Starts <paramref name="command"/> (a program and optional arguments) with the file as its last argument.</summary>
    public static string? StartWith(string command, string file)
    {
        string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "Enter the program to open the file with.";
        }

        try
        {
            ProcessStartInfo info = new(parts[0]) { UseShellExecute = false };
            foreach (string argument in parts.Skip(1))
            {
                info.ArgumentList.Add(argument);
            }

            info.ArgumentList.Add(file);
            _ = Process.Start(info) ?? throw new InvalidOperationException("The program did not start.");
            return null;
        }
        catch (Exception ex)
        {
            return $"Could not start '{parts[0]}': {ex.Message}";
        }
    }

    /// <summary>Opens the file in the editor configured for git (GIT_EDITOR, core.editor, VISUAL, EDITOR), unless that is a terminal editor.</summary>
    public static string? Edit(string workingDir, string file)
    {
        string? editor = Environment.GetEnvironmentVariable("GIT_EDITOR");
        editor = string.IsNullOrWhiteSpace(editor) ? ConfiguredEditor(workingDir) : editor;
        editor = string.IsNullOrWhiteSpace(editor) ? Environment.GetEnvironmentVariable("VISUAL") : editor;
        editor = string.IsNullOrWhiteSpace(editor) ? Environment.GetEnvironmentVariable("EDITOR") : editor;
        string program = editor is null ? string.Empty : Path.GetFileName(editor.Trim().Split(' ')[0]);
        if (program.Length == 0 || TerminalEditors.Contains(program))
        {
            // A terminal editor cannot run without a terminal: the desktop's default editor takes over.
            ShellLauncher.Open(file);
            return null;
        }

        return StartWith(editor!, file);
    }

    /// <summary>Runs <c>git rm --cached</c> on the paths: they stay on disk but are no longer tracked. Returns the failure, or null.</summary>
    public static async Task<string?> StopTrackingAsync(string workingDir, IReadOnlyList<string> paths)
        => await Task.Run(() =>
        {
            ProcessStartInfo info = new("git") { WorkingDirectory = workingDir, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (string argument in new[] { "rm", "--cached", "-r", "--ignore-unmatch", "--quiet", "--" }.Concat(paths))
            {
                info.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(info) ?? throw new InvalidOperationException("git did not start.");
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? null : error.Trim();
        });

    /// <summary>Deletes the files or folders from the working directory. Returns the failure, or null.</summary>
    public static string? Delete(string workingDir, IReadOnlyList<string> paths)
    {
        try
        {
            foreach (string path in paths)
            {
                string full = Path.GetFullPath(Path.Combine(workingDir, path));
                if (!full.StartsWith(Path.GetFullPath(workingDir), StringComparison.Ordinal))
                {
                    return $"'{path}' is outside the repository.";
                }

                if (Directory.Exists(full))
                {
                    Directory.Delete(full, recursive: true);
                }
                else if (File.Exists(full))
                {
                    File.Delete(full);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string? ConfiguredEditor(string workingDir)
    {
        try
        {
            ProcessStartInfo info = new("git") { WorkingDirectory = workingDir, UseShellExecute = false, RedirectStandardOutput = true };
            info.ArgumentList.Add("config");
            info.ArgumentList.Add("--get");
            info.ArgumentList.Add("core.editor");
            using Process process = Process.Start(info)!;
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static Window? OwnerOf(FileMenuContext context)
        => context.Owner ?? DialogWindow.Open.LastOrDefault() ?? (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
