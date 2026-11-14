using System.Text.Json;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  Small user preferences of this UI host that have no counterpart in <c>AppSettings</c>, persisted as JSON
///  next to the theme file. "Show stashes in history" is not stored here: the core reads <c>AppSettings.ShowStashes</c>.
/// </summary>
public sealed class BehaviorSettings
{
    public const int DefaultHistoryLimit = 2000;
    public const int MaxHistoryLimit = 1_000_000;

    private sealed record Document(int HistoryLimit);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public BehaviorSettings(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitExtensions", "avalonia-behavior.json");
        HistoryLimit = Load();
    }

    /// <summary>
    ///  The settings used by the UI. Replaceable so tests can point them at a temporary file.
    /// </summary>
    public static BehaviorSettings Current { get; set; } = new();

    public string FilePath { get; }

    /// <summary>
    ///  The maximum number of commits loaded into the history list.
    /// </summary>
    public int HistoryLimit { get; private set; }

    public static bool IsValidHistoryLimit(int value) => value is >= 1 and <= MaxHistoryLimit;

    public void SetHistoryLimit(int value)
    {
        if (!IsValidHistoryLimit(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        HistoryLimit = value;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Document(value), JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Could not save {FilePath}: {ex.Message}", ex);
        }
    }

    private int Load()
    {
        try
        {
            if (File.Exists(FilePath)
                && JsonSerializer.Deserialize<Document>(File.ReadAllText(FilePath), JsonOptions) is { } document
                && IsValidHistoryLimit(document.HistoryLimit))
            {
                return document.HistoryLimit;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
        }

        return DefaultHistoryLimit;
    }
}
