using Avalonia.Controls;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The "View" button of the repository bar: a menu with the System / Light / Dark theme choice.
/// </summary>
public sealed class ThemeMenuButton : Button
{
    private readonly Dictionary<ThemeChoice, MenuItem> _items = [];

    // Style as a plain Button: the theme's control template is keyed on the exact type.
    protected override Type StyleKeyOverride => typeof(Button);

    public ThemeMenuButton()
    {
        Content = "◐";
        FontSize = 16;
        ToolTip.SetTip(this, "Theme: system, light or dark");
        Name = "ViewButton";
        Avalonia.Automation.AutomationProperties.SetName(this, "Theme");
        Avalonia.Automation.AutomationProperties.SetHelpText(this, "Choose the system, light or dark theme (Ctrl+Shift+D cycles)");

        MenuFlyout flyout = new();
        foreach (ThemeChoice choice in Enum.GetValues<ThemeChoice>())
        {
            MenuItem item = new()
            {
                Header = choice == ThemeChoice.System ? "System theme" : $"{choice} theme",
                Tag = choice,
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "Theme",
            };
            item.Click += (_, _) => ThemeService.Current.Set(choice);
            _items[choice] = item;
            flyout.Items.Add(item);
        }

        Flyout = flyout;
        Sync();
        ThemeService.Current.ChoiceChanged += Sync;
    }

    /// <summary>
    ///  The menu entries, for tests.
    /// </summary>
    public IReadOnlyDictionary<ThemeChoice, MenuItem> MenuItems => _items;

    private void Sync()
    {
        foreach ((ThemeChoice choice, MenuItem item) in _items)
        {
            item.IsChecked = choice == ThemeService.Current.Choice;
        }
    }
}
