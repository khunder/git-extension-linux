using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;
using System.Xml.XPath;

namespace GitCommands.Settings;

/// <summary>
///  Reads the "IsPortable" flag from the application's <c>.config</c> file
///  (the same file and XPath that the publish targets rewrite for portable builds).
/// </summary>
internal static class PortableModeConfig
{
    private const string IsPortableXPath = "configuration/applicationSettings/GitCommands.Properties.Settings/setting[@name='IsPortable']/value";

    public static bool IsPortable { get; } = Read();

    private static bool Read()
    {
        try
        {
            string? assemblyPath = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(assemblyPath))
            {
                return false;
            }

            string configPath = assemblyPath + ".config";
            if (!File.Exists(configPath))
            {
                return false;
            }

            string? value = XDocument.Load(configPath).XPathSelectElement(IsPortableXPath)?.Value;
            return bool.TryParse(value, out bool isPortable) && isPortable;
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            return false;
        }
    }
}
