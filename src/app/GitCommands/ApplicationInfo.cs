using System.Diagnostics;
using System.Reflection;

namespace GitCommands;

/// <summary>
///  Platform-neutral replacements for the <c>System.Windows.Forms.Application</c> metadata properties,
///  derived from the entry assembly in the same way.
/// </summary>
internal static class ApplicationInfo
{
    private static readonly Assembly? _entryAssembly = Assembly.GetEntryAssembly();

    /// <summary>
    ///  Full path of the running executable.
    /// </summary>
    public static string ExecutablePath => Environment.ProcessPath ?? string.Empty;

    public static string ProductName
        => _entryAssembly?.GetCustomAttribute<AssemblyProductAttribute>()?.Product
            ?? AppSettings.ApplicationId;

    public static string CompanyName
        => _entryAssembly?.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company
            ?? AppSettings.ApplicationId;

    public static string ProductVersion
    {
        get
        {
            string? location = _entryAssembly?.Location;
            if (!string.IsNullOrEmpty(location))
            {
                string? version = FileVersionInfo.GetVersionInfo(location).ProductVersion;
                if (!string.IsNullOrEmpty(version))
                {
                    return version;
                }
            }

            return _entryAssembly?.GetName().Version?.ToString() ?? "1.0.0.0";
        }
    }

    /// <summary>
    ///  Per-user, per-version application data path: <c>{ApplicationData}/{Company}/{Product}/{ProductVersion}</c>.
    ///  The folder is created if it does not exist, like <c>Application.UserAppDataPath</c> does.
    /// </summary>
    public static string UserAppDataPath
    {
        get
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                CompanyName,
                ProductName,
                ProductVersion);
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
