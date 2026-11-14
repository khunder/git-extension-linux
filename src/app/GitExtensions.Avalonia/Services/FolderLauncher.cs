using System.Diagnostics;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  Shows a folder in the platform's file manager (Explorer, Finder, xdg-open).
/// </summary>
public static class FolderLauncher
{
    /// <summary>
    ///  Opens <paramref name="folder"/> in the file manager. Returns false when no file manager could be started.
    /// </summary>
    public static bool TryOpen(string folder)
    {
        try
        {
            string program = OperatingSystem.IsWindows() ? "explorer.exe" : OperatingSystem.IsMacOS() ? "open" : "xdg-open";
            ProcessStartInfo startInfo = new(program) { UseShellExecute = false, CreateNoWindow = true };
            startInfo.ArgumentList.Add(folder);
            using Process? process = Process.Start(startInfo);
            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
