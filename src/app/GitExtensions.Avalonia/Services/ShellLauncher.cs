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

    private static string? FindOnPath(string program)
        => (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, program))
            .FirstOrDefault(File.Exists);
}
