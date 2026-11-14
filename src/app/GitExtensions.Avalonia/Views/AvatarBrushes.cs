using Avalonia.Media;
using Avalonia.Media.Immutable;
using GitExtensions.Desktop.History;

namespace GitExtensions.Desktop.Views;

/// <summary>Fixed mid-tone fills for the avatar chips; the white initials stay readable in both themes.</summary>
internal static class AvatarBrushes
{
    private static readonly IBrush[] Brushes =
    [
        Brush(0x3F, 0x7F, 0xC9),
        Brush(0x2E, 0x8B, 0x57),
        Brush(0xC0, 0x62, 0x2B),
        Brush(0x8E, 0x55, 0xB8),
        Brush(0xB8, 0x4A, 0x6E),
        Brush(0x1F, 0x8E, 0x9A),
        Brush(0x7A, 0x7A, 0x32),
        Brush(0x5B, 0x6C, 0x8F),
    ];

    public static IBrush For(int index) => Brushes[Math.Abs(index) % AvatarStyle.ColorCount];

    private static IBrush Brush(byte r, byte g, byte b) => new ImmutableSolidColorBrush(Color.FromRgb(r, g, b));
}
