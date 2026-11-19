using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  The user's theme preference. <see cref="System"/> follows the operating system.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThemeChoice
{
    System,
    Light,
    Dark,

    /// <summary>Neutral dark grey, like GitHub Desktop's dark theme.</summary>
    Graphite,

    /// <summary>Soft light grey.</summary>
    Mist,

    /// <summary>Dimmed blue-grey dark.</summary>
    Dusk,

    /// <summary>Warm, paper-like light.</summary>
    Sand
}

/// <summary>
///  Persists the <see cref="ThemeChoice"/> in a small JSON file next to the window-state file.
/// </summary>
public sealed class ThemeStore
{
    private sealed record Document(ThemeChoice Theme);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ThemeStore(string? filePath = null)
        => FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitExtensions", "avalonia-theme.json");

    public string FilePath { get; }

    /// <summary>
    ///  Returns the stored choice, or <see cref="ThemeChoice.System"/> if there is none or the file is unreadable.
    /// </summary>
    public ThemeChoice Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return ThemeChoice.System;
            }

            Document? document = JsonSerializer.Deserialize<Document>(File.ReadAllText(FilePath), JsonOptions);
            return document is not null && Enum.IsDefined(document.Theme) ? document.Theme : ThemeChoice.System;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return ThemeChoice.System;
        }
    }

    public void Save(ThemeChoice choice)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Document(choice), JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the theme preference is not worth bothering the user.
        }
    }
}
