using Avalonia.Data.Converters;
using Avalonia.Media;
using GitExtensions.Desktop.Diff;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  Theme-neutral line colors: translucent tints blend with whatever the theme background is, in light and dark.
/// </summary>
public static class DiffLineBrushes
{
    private static readonly IBrush Added = new SolidColorBrush(Color.FromArgb(0x38, 0x2E, 0xA0, 0x43)).ToImmutable();
    private static readonly IBrush Removed = new SolidColorBrush(Color.FromArgb(0x38, 0xE5, 0x3E, 0x3E)).ToImmutable();
    private static readonly IBrush Hunk = new SolidColorBrush(Color.FromArgb(0x30, 0x3B, 0x82, 0xF6)).ToImmutable();

    public static FuncValueConverter<DiffLineKind, IBrush?> Background { get; } = new(kind => kind switch
    {
        DiffLineKind.Added => Added,
        DiffLineKind.Removed => Removed,
        DiffLineKind.Hunk => Hunk,
        _ => null
    });

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
