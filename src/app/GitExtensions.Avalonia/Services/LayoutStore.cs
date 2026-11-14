using System.Text.Json;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  The persisted sizes of the browse page's panes: the width of the left panel and the relative weights of the
///  revision grid (top) and the tabs (bottom). The heights are relative weights (the last measured pixel size),
///  so they scale to whatever window size is current.
/// </summary>
public sealed record LayoutSettings(
    double SidebarWidth = LayoutSettings.DefaultSidebarWidth,
    double GridHeight = 330,
    double TabsHeight = 270,
    bool SidebarCollapsed = false)
{
    public const double DefaultSidebarWidth = 220;
    public const double MinSidebarWidth = 150;
    public const double MaxSidebarWidth = 500;
    public const double MinWeight = 60;
    public const double MaxWeight = 3000;

    /// <summary>
    ///  Returns a copy in which every size is finite and within sane bounds, so a corrupt or absurd
    ///  stored value cannot make the window unusable.
    /// </summary>
    public LayoutSettings Clamp()
    {
        LayoutSettings defaults = new();
        return new LayoutSettings(
            Limit(SidebarWidth, MinSidebarWidth, MaxSidebarWidth, defaults.SidebarWidth),
            Limit(GridHeight, MinWeight, MaxWeight, defaults.GridHeight),
            Limit(TabsHeight, MinWeight, MaxWeight, defaults.TabsHeight),
            SidebarCollapsed);
    }

    private static double Limit(double value, double min, double max, double fallback)
        => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

/// <summary>
///  Persists the <see cref="LayoutSettings"/> in a small JSON file next to the window-state file.
/// </summary>
public sealed class LayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public LayoutStore(string? filePath = null)
        => FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitExtensions", "avalonia-layout.json");

    public string FilePath { get; }

    /// <summary>
    ///  Returns the stored (clamped) layout, or the defaults if there is none or the file is unreadable.
    /// </summary>
    public LayoutSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new LayoutSettings();
            }

            LayoutSettings? settings = JsonSerializer.Deserialize<LayoutSettings>(File.ReadAllText(FilePath), JsonOptions);
            return settings?.Clamp() ?? new LayoutSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new LayoutSettings();
        }
    }

    public void Save(LayoutSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings.Clamp(), JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the layout is not worth bothering the user.
        }
    }
}
