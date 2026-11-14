using Avalonia.Data.Converters;
using GitExtensions.Desktop.Diff;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  Diff line presentation helpers. Line background colors live in Styles/Theme.axaml (theme-aware).
/// </summary>
public static class DiffLineBrushes
{
    /// <summary>
    ///  Header and informational lines are dimmed through opacity so they follow the theme's foreground.
    /// </summary>
    public static FuncValueConverter<DiffLineKind, double> TextOpacity { get; } = new(kind => kind switch
    {
        DiffLineKind.Header or DiffLineKind.Info => 0.6,
        DiffLineKind.Hunk => 0.85,
        _ => 1.0
    });
}
