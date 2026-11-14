using System.Text.Json;
using Avalonia;
using Avalonia.Controls;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  The persisted placement of the main window.
/// </summary>
public sealed record WindowPlacement(double Width, double Height, int? X, int? Y, bool IsMaximized);

/// <summary>
///  Remembers the window size, position and maximized state in a small JSON file in the user's app data folder.
/// </summary>
public sealed class WindowStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public WindowStateStore(string? filePath = null)
        => FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitExtensions", "avalonia-window.json");

    public string FilePath { get; }

    /// <summary>
    ///  Returns the stored placement, or <see langword="null"/> if there is none or it is unreadable.
    /// </summary>
    public WindowPlacement? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            WindowPlacement? placement = JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(FilePath));
            return placement is { Width: > 0, Height: > 0 } ? placement : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Save(WindowPlacement placement)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(placement, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the window placement is not worth bothering the user.
        }
    }

    /// <summary>
    ///  Applies the stored placement (if any) to <paramref name="window"/>. Call before it is shown.
    /// </summary>
    public void Apply(Window window)
    {
        WindowPlacement? placement = Load();
        if (placement is null)
        {
            return;
        }

        window.Width = placement.Width;
        window.Height = placement.Height;
        if (placement.X is int x && placement.Y is int y && x > -10000 && y > -10000 && x < 100000 && y < 100000)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(x, y);
        }

        if (placement.IsMaximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    /// <summary>
    ///  Stores the placement of <paramref name="window"/>. While maximized the previous restored bounds are kept.
    /// </summary>
    public void Capture(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            return;
        }

        if (window.WindowState == WindowState.Maximized)
        {
            WindowPlacement? previous = Load();
            Save(previous is null
                ? new WindowPlacement(window.Width, window.Height, null, null, IsMaximized: true)
                : previous with { IsMaximized = true });
            return;
        }

        Save(new WindowPlacement(window.Bounds.Width, window.Bounds.Height, window.Position.X, window.Position.Y, IsMaximized: false));
    }
}
