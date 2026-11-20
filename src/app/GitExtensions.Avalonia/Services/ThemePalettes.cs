using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  The base colors of a named theme. Everything else (the Fluent control brushes, the panels of the app) is derived from
///  these in <see cref="ThemePalettes.Build"/>; diff, error and badge colors come from the inherited Light or Dark theme.
/// </summary>
/// <param name="Window">The window and the main areas.</param>
/// <param name="Panel">The sidebar, toolbar and status bar.</param>
/// <param name="Elevated">Menus, dialogs, tooltips and cards.</param>
/// <param name="Input">Text boxes.</param>
/// <param name="Control">Buttons and combo boxes.</param>
/// <param name="ControlHover">Buttons under the pointer.</param>
/// <param name="ControlBorder">Borders of buttons and text boxes.</param>
/// <param name="Hover">List and tree rows under the pointer.</param>
/// <param name="Selected">Selected list and tree rows.</param>
/// <param name="Text">Normal text.</param>
/// <param name="TextMuted">Secondary text, placeholders and disabled text.</param>
/// <param name="Divider">Separator lines between areas.</param>
/// <param name="Accent">The accent color (accent buttons, focus, the selected tab).</param>
/// <param name="AccentText">Links and accent-colored text.</param>
public sealed record ThemePalette(
    Color Window,
    Color Panel,
    Color Elevated,
    Color Input,
    Color Control,
    Color ControlHover,
    Color ControlBorder,
    Color Hover,
    Color Selected,
    Color Text,
    Color TextMuted,
    Color Divider,
    Color Accent,
    Color AccentText);

/// <summary>
///  The named themes besides System, Light and Dark. Adding a theme: one entry here and one value in <see cref="ThemeChoice"/>.
/// </summary>
public static class ThemePalettes
{
    /// <summary>The theme variant and palette of every named theme, by choice.</summary>
    public static IReadOnlyDictionary<ThemeChoice, (ThemeVariant Variant, ThemePalette Palette)> All { get; } =
        new Dictionary<ThemeChoice, (ThemeVariant, ThemePalette)>
        {
            // Graphite: the neutral dark grey of GitHub Desktop's dark theme.
            [ThemeChoice.Graphite] = (new ThemeVariant("Graphite", ThemeVariant.Dark), new ThemePalette(
                Window: C("#24292E"), Panel: C("#1F2428"), Elevated: C("#2F363D"), Input: C("#1B1F23"),
                Control: C("#2F363D"), ControlHover: C("#3A424A"), ControlBorder: C("#444D56"),
                Hover: C("#2B3036"), Selected: C("#0366D6"), Text: C("#D1D5DA"), TextMuted: C("#8B949E"),
                Divider: C("#141414"), Accent: C("#0366D6"), AccentText: C("#79B8FF"))),

            // Mist: a soft light grey, like GitHub Desktop's light theme with grey panels.
            [ThemeChoice.Mist] = (new ThemeVariant("Mist", ThemeVariant.Light), new ThemePalette(
                Window: C("#F6F8FA"), Panel: C("#ECEFF2"), Elevated: C("#FFFFFF"), Input: C("#FFFFFF"),
                Control: C("#F3F4F6"), ControlHover: C("#E6E9ED"), ControlBorder: C("#D0D7DE"),
                Hover: C("#E3E7EB"), Selected: C("#CCE5FF"), Text: C("#24292F"), TextMuted: C("#6E7781"),
                Divider: C("#D0D7DE"), Accent: C("#0969DA"), AccentText: C("#0969DA"))),

            // Dusk: a dimmed blue-grey dark theme, easier on the eyes than black.
            [ThemeChoice.Dusk] = (new ThemeVariant("Dusk", ThemeVariant.Dark), new ThemePalette(
                Window: C("#22272E"), Panel: C("#1C2128"), Elevated: C("#2D333B"), Input: C("#1C2128"),
                Control: C("#373E47"), ControlHover: C("#444C56"), ControlBorder: C("#444C56"),
                Hover: C("#2D333B"), Selected: C("#264D78"), Text: C("#ADBAC7"), TextMuted: C("#768390"),
                Divider: C("#373E47"), Accent: C("#316DCA"), AccentText: C("#539BF5"))),

            // Sand: a warm, paper-like light theme.
            [ThemeChoice.Sand] = (new ThemeVariant("Sand", ThemeVariant.Light), new ThemePalette(
                Window: C("#FBF7EF"), Panel: C("#F2ECE0"), Elevated: C("#FFFDF8"), Input: C("#FFFDF8"),
                Control: C("#EFE8DA"), ControlHover: C("#E6DCC9"), ControlBorder: C("#D6CCB8"),
                Hover: C("#EEE5D3"), Selected: C("#F3DCA8"), Text: C("#3B3428"), TextMuted: C("#8A7E69"),
                Divider: C("#E0D6C2"), Accent: C("#B5651D"), AccentText: C("#9A5416"))),
        };

    /// <summary>Adds the theme dictionaries of the named themes to the application resources.</summary>
    public static void Register(IResourceDictionary resources)
    {
        foreach ((ThemeVariant variant, ThemePalette palette) in All.Values)
        {
            resources.ThemeDictionaries[variant] = Build(palette);
        }
    }

    /// <summary>Whether <paramref name="variant"/> is dark (Dark itself or a theme based on it).</summary>
    public static bool IsDark(ThemeVariant? variant)
    {
        for (ThemeVariant? current = variant; current is not null; current = current.InheritVariant)
        {
            if (current == ThemeVariant.Dark)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The resources of one theme: the Fluent system colors and brushes plus the app's own panel brushes.</summary>
    public static ResourceDictionary Build(ThemePalette p)
    {
        ResourceDictionary d = [];
        Color textMedium = Mix(p.Text, p.TextMuted, 0.5);
        Color controlPressed = Mix(p.Control, p.Window, 0.5);
        Color selectedHover = Mix(p.Selected, p.Text, 0.12);
        Color baseLow = Mix(p.Window, p.Text, 0.2);

        void Colour(string key, Color color) => d[key] = color;
        void Brush(string key, Color color) => d[key] = new SolidColorBrush(color);

        // Fluent system colors.
        Colour("SystemAccentColor", p.Accent);
        Colour("SystemAccentColorLight1", Mix(p.Accent, Colors.White, 0.15));
        Colour("SystemAccentColorLight2", Mix(p.Accent, Colors.White, 0.35));
        Colour("SystemAccentColorLight3", Mix(p.Accent, Colors.White, 0.55));
        Colour("SystemAccentColorDark1", Mix(p.Accent, Colors.Black, 0.15));
        Colour("SystemAccentColorDark2", Mix(p.Accent, Colors.Black, 0.3));
        Colour("SystemAltHighColor", p.Window);
        Colour("SystemAltMediumHighColor", WithAlpha(p.Window, 0xCC));
        Colour("SystemAltMediumColor", WithAlpha(p.Window, 0x99));
        Colour("SystemAltMediumLowColor", WithAlpha(p.Window, 0x66));
        Colour("SystemAltLowColor", WithAlpha(p.Window, 0x33));
        Colour("SystemRegionColor", p.Window);
        Colour("SystemChromeLowColor", p.Panel);
        Colour("SystemChromeMediumLowColor", p.Panel);
        Colour("SystemChromeMediumColor", p.Elevated);
        Colour("SystemChromeHighColor", p.ControlBorder);
        Colour("SystemChromeDisabledLowColor", p.TextMuted);
        Colour("SystemChromeDisabledHighColor", p.Elevated);
        Colour("SystemChromeAltLowColor", textMedium);
        Colour("SystemListLowColor", p.Hover);
        Colour("SystemListMediumColor", Mix(p.Hover, p.Text, 0.08));
        Colour("SystemBaseHighColor", p.Text);
        Colour("SystemBaseMediumHighColor", textMedium);
        Colour("SystemBaseMediumColor", p.TextMuted);
        Colour("SystemBaseMediumLowColor", Mix(p.TextMuted, p.Window, 0.3));
        Colour("SystemBaseLowColor", baseLow);

        // Fluent brushes (they copy the colors when the theme loads, so they are set too).
        Brush("SystemControlBackgroundAltHighBrush", p.Window);
        Brush("SystemControlBackgroundAltMediumHighBrush", WithAlpha(p.Window, 0xCC));
        Brush("SystemControlBackgroundAltMediumBrush", WithAlpha(p.Window, 0x99));
        Brush("SystemControlBackgroundAltMediumLowBrush", WithAlpha(p.Window, 0x66));
        Brush("SystemRegionBrush", p.Window);
        Brush("SystemControlBackgroundChromeLowBrush", p.Panel);
        Brush("SystemControlBackgroundChromeMediumLowBrush", p.Panel);
        Brush("SystemControlBackgroundChromeMediumBrush", p.Elevated);
        Brush("SystemControlBackgroundChromeHighBrush", p.ControlBorder);
        Brush("SystemControlBackgroundBaseLowBrush", baseLow);
        Brush("SystemControlBackgroundListLowBrush", p.Hover);
        Brush("SystemControlBackgroundListMediumBrush", Mix(p.Hover, p.Text, 0.08));
        Brush("SystemControlForegroundBaseHighBrush", p.Text);
        Brush("SystemControlForegroundBaseMediumHighBrush", textMedium);
        Brush("SystemControlForegroundBaseMediumBrush", p.TextMuted);
        Brush("SystemControlForegroundBaseMediumLowBrush", Mix(p.TextMuted, p.Window, 0.3));
        Brush("SystemControlForegroundBaseLowBrush", baseLow);
        Brush("SystemControlDisabledBaseLowBrush", baseLow);
        Brush("SystemControlDisabledBaseMediumLowBrush", p.TextMuted);
        Brush("SystemControlDisabledChromeDisabledLowBrush", p.TextMuted);
        Brush("SystemControlDisabledChromeDisabledHighBrush", p.Elevated);
        Brush("SystemControlForegroundChromeDisabledLowBrush", p.TextMuted);
        Brush("SystemControlHighlightListAccentLowBrush", WithAlpha(p.Accent, 0x55));
        Brush("SystemControlHyperlinkTextBrush", p.AccentText);

        // The app's own areas.
        Brush("AccentTextForeground", p.AccentText);
        Brush("LinkForeground", p.AccentText);
        Brush("DialogBackground", p.Elevated);
        Brush("CardBackground", p.Elevated);
        Brush("MenuBarBackground", p.Panel);
        Brush("ToolbarBackground", p.Panel);
        Brush("SidebarBackground", p.Panel);
        Brush("StatusBarBackground", p.Panel);
        Brush("CommitMessageBackground", p.Input);
        Brush("SubtleBorderBrush", p.Divider);

        // Menus, flyouts and tooltips.
        Brush("MenuFlyoutPresenterBackground", p.Elevated);
        Brush("MenuFlyoutPresenterBorderBrush", p.ControlBorder);
        Brush("MenuFlyoutItemBackgroundPointerOver", p.Selected);
        Brush("MenuFlyoutItemBackgroundPressed", p.Selected);
        Brush("MenuFlyoutItemBackgroundSelected", p.Selected);
        Brush("MenuFlyoutSeparatorBackground", p.ControlBorder);
        Brush("MenuBarItemBackgroundPointerOver", p.ControlHover);
        Brush("MenuBarItemBackgroundPressed", p.ControlHover);
        Brush("MenuBarItemBackgroundSelected", p.ControlHover);
        Brush("ToolTipBackground", p.Elevated);
        Brush("ToolTipBorderBrush", p.ControlBorder);
        Brush("ToolTipForeground", p.Text);

        // Inputs and buttons.
        Brush("TextControlBackground", p.Input);
        Brush("TextControlBackgroundPointerOver", p.Input);
        Brush("TextControlBackgroundFocused", p.Input);
        Brush("TextControlBorderBrush", p.ControlBorder);
        Brush("TextControlBorderBrushPointerOver", Mix(p.ControlBorder, p.Text, 0.2));
        Brush("TextControlPlaceholderForeground", p.TextMuted);
        Brush("TextControlSelectionHighlightColor", WithAlpha(p.Accent, 0x88));
        Brush("ComboBoxDropDownBackground", p.Elevated);
        Brush("ComboBoxDropDownBorderBrush", p.ControlBorder);
        Brush("ComboBoxItemBackgroundPointerOver", p.Hover);
        Brush("ComboBoxItemBackgroundSelected", p.Selected);
        Brush("ComboBoxItemBackgroundSelectedPointerOver", selectedHover);
        Brush("ComboBoxBackground", p.Control);
        Brush("ComboBoxBackgroundPointerOver", p.ControlHover);
        Brush("ComboBoxBackgroundPressed", controlPressed);
        Brush("ComboBoxBorderBrush", p.ControlBorder);
        Brush("ComboBoxBorderBrushPointerOver", Mix(p.ControlBorder, p.Text, 0.2));
        foreach (string control in new[] { "Button", "ToggleButton", "SplitButton" })
        {
            Brush($"{control}Background", p.Control);
            Brush($"{control}BackgroundPointerOver", p.ControlHover);
            Brush($"{control}BackgroundPressed", controlPressed);
            Brush($"{control}BorderBrush", p.ControlBorder);
        }

        Brush("ButtonBackgroundDisabled", Mix(p.Control, p.Window, 0.5));
        Brush("ButtonBorderBrushPointerOver", Mix(p.ControlBorder, p.Text, 0.2));
        Brush("ButtonBorderBrushPressed", p.ControlBorder);
        Brush("ButtonBorderBrushDisabled", Mix(p.ControlBorder, p.Window, 0.5));
        Brush("ButtonForegroundDisabled", p.TextMuted);
        Brush("CheckBoxCheckBackgroundStrokeUnchecked", p.TextMuted);
        Brush("CheckBoxCheckBackgroundStrokeUncheckedPointerOver", textMedium);
        Brush("CheckBoxCheckBackgroundFillUnchecked", p.Input);
        Brush("CheckBoxCheckBackgroundFillUncheckedPointerOver", p.Input);

        // Lists, trees and tabs.
        foreach (string control in new[] { "ListBoxItem", "TreeViewItem" })
        {
            Brush($"{control}BackgroundPointerOver", p.Hover);
            Brush($"{control}BackgroundSelected", p.Selected);
            Brush($"{control}BackgroundSelectedPointerOver", selectedHover);
            Brush($"{control}BackgroundPressed", p.Selected);
            Brush($"{control}BackgroundSelectedPressed", p.Selected);
        }

        Brush("TabItemHeaderForegroundUnselected", p.TextMuted);
        Brush("TabItemHeaderForegroundUnselectedPointerOver", p.Text);
        Brush("TabItemHeaderForegroundSelected", p.Text);
        Brush("TabItemHeaderSelectedPipeFill", p.Accent);

        // Scroll bars.
        Brush("ScrollBarThumbFill", Mix(p.Window, p.Text, 0.3));
        Brush("ScrollBarThumbFillPointerOver", Mix(p.Window, p.Text, 0.45));
        Brush("ScrollBarThumbFillPressed", Mix(p.Window, p.Text, 0.6));
        return d;
    }

    private static Color C(string hex) => Color.Parse(hex);

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    // Linear mix: 0 gives a, 1 gives b.
    private static Color Mix(Color a, Color b, double amount)
    {
        static byte Lerp(byte x, byte y, double t) => (byte)Math.Round(x + ((y - x) * t));
        return Color.FromArgb(Lerp(a.A, b.A, amount), Lerp(a.R, b.R, amount), Lerp(a.G, b.G, amount), Lerp(a.B, b.B, amount));
    }
}
