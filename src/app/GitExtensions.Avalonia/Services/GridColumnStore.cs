using System.Text.Json;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  The persisted widths of the revision grid columns (the message column takes the rest).
/// </summary>
public sealed record GridColumnSettings(
    double AvatarWidth = 32,
    double AuthorWidth = 140,
    double DateWidth = 110,
    double HashWidth = 84,
    double StatusWidth = 22)
{
    public const double MinWidth = 24;
    public const double MaxWidth = 600;

    /// <summary>A copy in which every width is finite and within sane bounds.</summary>
    public GridColumnSettings Clamp()
    {
        GridColumnSettings d = new();
        return new GridColumnSettings(
            Limit(AvatarWidth, d.AvatarWidth),
            Limit(AuthorWidth, d.AuthorWidth),
            Limit(DateWidth, d.DateWidth),
            Limit(HashWidth, d.HashWidth),
            Limit(StatusWidth, d.StatusWidth));
    }

    private static double Limit(double value, double fallback)
        => double.IsFinite(value) ? Math.Clamp(value, MinWidth, MaxWidth) : fallback;
}

/// <summary>Persists the <see cref="GridColumnSettings"/> in a small JSON file; the path can be overridden for tests.</summary>
public sealed class GridColumnStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public GridColumnStore(string? filePath = null)
        => FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitExtensions", "avalonia-grid-columns.json");

    /// <summary>The store used by the revision grid; replaceable so tests can point it at a temporary file.</summary>
    public static GridColumnStore Current { get; set; } = new();

    public string FilePath { get; }

    public GridColumnSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new GridColumnSettings();
            }

            return JsonSerializer.Deserialize<GridColumnSettings>(File.ReadAllText(FilePath), JsonOptions)?.Clamp() ?? new GridColumnSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new GridColumnSettings();
        }
    }

    public void Save(GridColumnSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings.Clamp(), JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the widths is not worth bothering the user.
        }
    }
}
