using System.Diagnostics;
using GitCommands;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  Starts external programs for the menu: the default handler of a file, folder or URL, and a terminal in a folder.
/// </summary>
public static class ShellLauncher
{
    private static readonly string[] LinuxTerminals = ["x-terminal-emulator", "gnome-terminal", "konsole", "xfce4-terminal", "alacritty", "kitty", "xterm"];

    /// <summary>
    ///  Opens a file or a folder with the desktop's default handler.
    /// </summary>
    public static void Open(string path) => OsShellUtil.Open(path);

    public static void OpenUrl(string url) => OsShellUtil.OpenUrlInDefaultBrowser(url);

    /// <summary>
    ///  Opens a terminal whose working directory is <paramref name="folder"/>. Returns an error message, or null on success.
    /// </summary>
    public static string? OpenTerminal(string folder)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (FindGitBash() is { } gitBash)
                {
                    _ = Process.Start(new ProcessStartInfo(gitBash) { WorkingDirectory = folder, UseShellExecute = false, ArgumentList = { "--cd=" + folder } });
                    return null;
                }

                _ = Process.Start(new ProcessStartInfo("cmd.exe") { WorkingDirectory = folder, UseShellExecute = true });
                return null;
            }

            if (OperatingSystem.IsMacOS())
            {
                _ = Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-a", "Terminal", folder } });
                return null;
            }

            foreach (string terminal in LinuxTerminals)
            {
                if (FindOnPath(terminal) is null)
                {
                    continue;
                }

                _ = Process.Start(new ProcessStartInfo(terminal) { WorkingDirectory = folder });
                return null;
            }

            return "No terminal emulator was found.";
        }
        catch (Exception ex)
        {
            return $"Could not start a terminal: {ex.Message}";
        }
    }

    /// <summary>Git for Windows' <c>git-bash.exe</c>: next to the <c>git.exe</c> on the PATH, or in the usual install folders.</summary>
    private static string? FindGitBash()
    {
        List<string> candidates = [];
        if (FindOnPath("git.exe") is { } git && Path.GetDirectoryName(git) is { } gitDir)
        {
            // <root>\cmd\git.exe, <root>\bin\git.exe or <root>\mingw64\bin\git.exe
            for (string? dir = gitDir; dir is not null && candidates.Count < 4; dir = Path.GetDirectoryName(dir))
            {
                candidates.Add(Path.Combine(dir, "git-bash.exe"));
            }
        }

        foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            candidates.Add(Path.Combine(Environment.GetFolderPath(folder), "Git", "git-bash.exe"));
        }

        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "git-bash.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindOnPath(string program)
        => (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, program))
            .FirstOrDefault(File.Exists);
}
