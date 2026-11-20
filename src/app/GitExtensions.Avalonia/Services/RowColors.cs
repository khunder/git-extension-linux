using Avalonia;
using Avalonia.Media;
using GitCommands;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  The striping of the revision grid rows (Settings > Appearance > Colors): whether alternate rows are tinted
///  (the existing "Draw alternate background color for rows" setting) and, optionally, custom colors for the stripe of
///  normal rows and of the rows of your own commits. Without a custom color the theme's subtle default is used.
/// </summary>
public static class RowColors
{
    public const string NormalResourceKey = "CustomAlternateRow";
    public const string MineResourceKey = "CustomMineAlternateRow";

    private static string _normal = string.Empty;
    private static string _mine = string.Empty;
    private static bool _loaded;

    public static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitExtensions", "avalonia-row-colors.txt");

    /// <summary>Raised when the striping changed; the revision grid then updates its rows.</summary>
    public static event Action? Changed;

    public static bool Enabled => AppSettings.RevisionGraphDrawAlternateBackColor;

    /// <summary>The custom stripe color of normal rows as text ("#AARRGGBB"), or empty for the theme default.</summary>
    public static string Normal
    {
        get
        {
            Load();
            return _normal;
        }
    }

    /// <summary>The custom stripe color of the rows of your own commits, or empty for the theme default.</summary>
    public static string Mine
    {
        get
        {
            Load();
            return _mine;
        }
    }

    /// <summary>The theme's stripe color of normal rows (what is used without a custom color).</summary>
    public static Color DefaultNormal(bool dark) => dark ? Color.Parse("#08FFFFFF") : Color.Parse("#0C808080");

    /// <summary>The theme's stripe color of the rows of your own commits.</summary>
    public static Color DefaultMine(bool dark) => dark ? Color.Parse("#3A4F7FBF") : Color.Parse("#FFE1ECFE");

    /// <summary>The color shown in the settings: the custom one, or the default of the current theme.</summary>
    public static Color EffectiveNormal => TryParse(Normal, out Color color) ? color : DefaultNormal(IsDarkTheme);

    public static Color EffectiveMine => TryParse(Mine, out Color color) ? color : DefaultMine(IsDarkTheme);

    private static bool IsDarkTheme => ThemePalettes.IsDark(Application.Current?.ActualThemeVariant);

    public static bool HasNormal => TryParse(Normal, out _);

    public static bool HasMine => TryParse(Mine, out _);

    public static void SetNormal(string value)
    {
        Load();
        _normal = value.Trim();
        Apply();
        Save();
        Changed?.Invoke();
    }

    public static void SetMine(string value)
    {
        Load();
        _mine = value.Trim();
        Apply();
        Save();
        Changed?.Invoke();
    }

    /// <summary>Tells the grid that <see cref="Enabled"/> changed.</summary>
    public static void NotifyEnabledChanged() => Changed?.Invoke();

    /// <summary>Puts the custom colors into the application resources (call once the application exists).</summary>
    public static void Apply()
    {
        Load();
        if (Application.Current?.Resources is not { } resources)
        {
            return;
        }

        resources[NormalResourceKey] = TryParse(_normal, out Color normal) ? new SolidColorBrush(normal) : Brushes.Transparent;
        resources[MineResourceKey] = TryParse(_mine, out Color mine) ? new SolidColorBrush(mine) : Brushes.Transparent;
    }

    public static bool TryParse(string text, out Color color)
    {
        color = default;
        return !string.IsNullOrWhiteSpace(text) && Color.TryParse(text.Trim(), out color);
    }

    /// <summary>True for an empty text (use the default) or a color that can be parsed.</summary>
    public static bool IsValid(string text) => string.IsNullOrWhiteSpace(text) || TryParse(text, out _);

    private static void Load()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            if (File.Exists(FilePath))
            {
                string[] lines = File.ReadAllLines(FilePath);
                _normal = lines.Length > 0 ? lines[0].Trim() : string.Empty;
                _mine = lines.Length > 1 ? lines[1].Trim() : string.Empty;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(FilePath, [_normal, _mine]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
