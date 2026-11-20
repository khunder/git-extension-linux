using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  Zooms the monospaced text of the editor-like views (diff, blame, file content, patches, output): Ctrl and plus or minus
///  or the mouse wheel change the size, Ctrl+0 resets it. The size is remembered between sessions.
/// </summary>
public static class EditorZoom
{
    public const double DefaultSize = 12;
    public const double MinSize = 6;
    public const double MaxSize = 48;
    private const string ResourceKey = "EditorFontSize";

    private static bool _installed;

    public static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitExtensions", "avalonia-editor-zoom.txt");

    /// <summary>The current size in device independent pixels.</summary>
    public static double Size { get; private set; } = DefaultSize;

    /// <summary>
    ///  Loads the remembered size and starts listening for the zoom gestures in every window.
    /// </summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        Load();
        Apply();
        InputElement.KeyDownEvent.AddClassHandler<Window>(OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        InputElement.PointerWheelChangedEvent.AddClassHandler<Window>(OnWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>Changes the size by <paramref name="delta"/> points (clamped) and applies it to all views.</summary>
    public static void Change(double delta) => Set(Size + delta);

    public static void Reset() => Set(DefaultSize);

    public static void Set(double size)
    {
        Size = Math.Clamp(Math.Round(size), MinSize, MaxSize);
        Apply();
        Save();
    }

    /// <summary>Handles the zoom keys; returns whether <paramref name="key"/> with <paramref name="modifiers"/> was one.</summary>
    public static bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (!modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Alt))
        {
            return false;
        }

        switch (key)
        {
            case Key.OemPlus or Key.Add:
                Change(+1);
                return true;
            case Key.OemMinus or Key.Subtract:
                Change(-1);
                return true;
            case Key.D0 or Key.NumPad0:
                Reset();
                return true;
            default:
                return false;
        }
    }

    private static void OnKeyDown(Window window, KeyEventArgs e)
    {
        if (HandleKey(e.Key, e.KeyModifiers))
        {
            e.Handled = true;
        }
    }

    private static void OnWheel(Window window, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Delta.Y != 0)
        {
            Change(e.Delta.Y > 0 ? +1 : -1);
            e.Handled = true;
        }
    }

    private static void Apply()
    {
        if (Application.Current?.Resources is { } resources)
        {
            resources[ResourceKey] = Size;
            resources["HistoryFontSize"] = Size + 2;
        }
    }

    private static void Load()
    {
        try
        {
            if (File.Exists(FilePath) && double.TryParse(File.ReadAllText(FilePath).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double size))
            {
                Size = Math.Clamp(size, MinSize, MaxSize);
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
            File.WriteAllText(FilePath, Size.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
