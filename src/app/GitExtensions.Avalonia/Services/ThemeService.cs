using Avalonia;
using Avalonia.Styling;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  Applies and persists the theme choice. Setting <see cref="Choice"/> re-skins the whole application immediately.
/// </summary>
public sealed class ThemeService
{
    private readonly ThemeStore _store;

    public ThemeService(ThemeStore? store = null)
    {
        _store = store ?? new ThemeStore();
        Choice = _store.Load();
    }

    /// <summary>
    ///  The service used by the UI. Replaceable so tests can point it at a temporary store.
    /// </summary>
    public static ThemeService Current { get; set; } = new();

    public ThemeChoice Choice { get; private set; }

    public event Action? ChoiceChanged;

    public static ThemeVariant ToVariant(ThemeChoice choice) => choice switch
    {
        ThemeChoice.Light => ThemeVariant.Light,
        ThemeChoice.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };

    /// <summary>
    ///  Applies the stored choice to the running application. Call before the main window is shown.
    /// </summary>
    public void ApplyCurrent()
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = ToVariant(Choice);
        }
    }

    public void Set(ThemeChoice choice)
    {
        Choice = choice;
        ApplyCurrent();
        _store.Save(choice);
        ChoiceChanged?.Invoke();
    }
}
