using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;

namespace GitCommands.Settings;

/// <summary>
///  Per-user key/value store for the few flags that Git Extensions historically kept in the Windows registry
///  (<c>HKCU\Software\GitExtensions</c>). On Windows it still uses the registry; elsewhere it falls back to a small
///  JSON file in the user's application data folder.
/// </summary>
internal static class UserRegistry
{
    private const string KeyPath = "Software\\GitExtensions";
    private const string LegacySubKey = "GitExtensions";

    private static readonly Lock _fileLock = new();
    private static RegistryKey? _key;

    private static string FilePath
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitExtensions", "user-values.json");

    public static string? GetValue(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            return GetKey().GetValue(name) as string;
        }

        lock (_fileLock)
        {
            return ReadFile().TryGetValue(name, out string? value) ? value : null;
        }
    }

    public static void SetValue(string name, string value)
    {
        if (OperatingSystem.IsWindows())
        {
            GetKey().SetValue(name, value);
            return;
        }

        lock (_fileLock)
        {
            Dictionary<string, string> values = ReadFile();
            values[name] = value;
            WriteFile(values);
        }
    }

    /// <summary>
    ///  Values saved by very old versions that stored their whole configuration in the registry.
    ///  Always empty on platforms without a registry.
    /// </summary>
    public static IEnumerable<(string name, string? value)> GetLegacySettings()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        using RegistryKey? oldSettings = GetKey().OpenSubKey(LegacySubKey);
        if (oldSettings is null)
        {
            yield break;
        }

        foreach (string name in oldSettings.GetValueNames())
        {
            object? value = oldSettings.GetValue(name, null);
            if (value is not null)
            {
                yield return (name, value.ToString());
            }
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static RegistryKey GetKey()
    {
        _key ??= Registry.CurrentUser.CreateSubKey(KeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree)
            ?? throw new InvalidOperationException($"Cannot open HKCU\\{KeyPath}");
        return _key;
    }

    private static Dictionary<string, string> ReadFile()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Trace.WriteLine(ex);
            return [];
        }
    }

    private static void WriteFile(Dictionary<string, string> values)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(values));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine(ex);
        }
    }
}
