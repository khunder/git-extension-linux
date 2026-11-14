using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace GitExtensions.Desktop.Views.ContextMenus;

/// <summary>
///  Small builder helpers shared by the code-built context menus: items with the original's icons, shortcut hints, section
///  captions, check items and the "not available yet" stubs. Everything uses the theme's own resources (no colours here).
/// </summary>
public static class MenuKit
{
    public const string NotAvailableTip = "Not available yet";

    /// <summary>A menu entry. The header is a plain text block so names with underscores are shown literally.</summary>
    public static MenuItem Item(string header, string? icon = null, Action? click = null, string? gesture = null, bool enabled = true, string? disabledTip = null, string? tip = null)
    {
        MenuItem item = new()
        {
            Header = new TextBlock { Text = header },
            Icon = new AppIcon { IconName = icon },
            IsEnabled = enabled,
        };
        AutomationProperties.SetName(item, header);
        if (gesture is not null)
        {
            item.InputGesture = KeyGesture.Parse(gesture);
        }

        if (click is not null)
        {
            item.Click += (_, _) => click();
        }

        string? hint = enabled ? tip : disabledTip ?? tip;
        if (hint is not null)
        {
            ToolTip.SetTip(item, hint);
            ToolTip.SetShowOnDisabled(item, true);
        }

        return item;
    }

    /// <summary>An entry of a feature that does not exist yet: shown (like the original) but disabled, with an explaining tooltip.</summary>
    public static MenuItem Stub(string header, string? icon = null, string? gesture = null)
        => Item(header, icon, gesture: gesture, enabled: false, disabledTip: NotAvailableTip);

    /// <summary>A check item; <paramref name="toggled"/> gets the new state.</summary>
    public static MenuItem Check(string header, bool isChecked, Action<bool> toggled, string? gesture = null, string? icon = null)
    {
        MenuItem item = Item(header, icon, gesture: gesture);
        item.ToggleType = MenuItemToggleType.CheckBox;
        item.IsChecked = isChecked;

        // The menu is rebuilt each time it opens, so the state is tracked here instead of relying on the item's own toggling.
        bool state = isChecked;
        item.Click += (_, _) =>
        {
            state = !state;
            item.IsChecked = state;
            toggled(state);
        };
        return item;
    }

    /// <summary>A radio item (the checked one of a group); <paramref name="chosen"/> runs when it is picked.</summary>
    public static MenuItem Radio(string header, string group, bool isChecked, Action chosen, string? gesture = null, string? icon = null)
    {
        MenuItem item = Item(header, icon, gesture: gesture);
        item.ToggleType = MenuItemToggleType.Radio;
        item.GroupName = group;
        item.IsChecked = isChecked;
        item.Click += (_, _) => chosen();
        return item;
    }

    /// <summary>An entry that opens a submenu (disabled, with a tooltip, when there is nothing to show).</summary>
    public static MenuItem Sub(string header, string? icon, IEnumerable<Control> children, bool enabled = true, string? disabledTip = null)
    {
        MenuItem item = Item(header, icon, enabled: enabled, disabledTip: disabledTip);
        foreach (Control child in Tidy(children))
        {
            item.Items.Add(child);
        }

        return item;
    }

    /// <summary>The bold caption of a group of items (the original's "caption" menu item): not clickable, not selectable.</summary>
    public static MenuItem Section(string text)
    {
        MenuItem item = new()
        {
            Header = new TextBlock { Text = text, FontWeight = FontWeight.SemiBold },
            Icon = new AppIcon(),
            IsHitTestVisible = false,
            Focusable = false,
            Classes = { "section" },
        };
        AutomationProperties.SetName(item, text);
        return item;
    }

    public static Separator Sep() => new();

    /// <summary>A context menu with the dense entries of the desktop-style menus (long menus must fit a small window).</summary>
    public static ContextMenu NewMenu()
    {
        ContextMenu menu = new();
        Style dense = new(x => x.OfType<MenuItem>());
        dense.Setters.Add(new Setter(Layoutable.MinHeightProperty, 0.0));
        dense.Setters.Add(new Setter(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty, new Thickness(8, 3)));
        dense.Setters.Add(new Setter(Avalonia.Controls.Primitives.TemplatedControl.FontSizeProperty, 13.0));
        menu.Styles.Add(dense);
        Style separator = new(x => x.OfType<Separator>());
        separator.Setters.Add(new Setter(Layoutable.MarginProperty, new Thickness(0, 2)));
        menu.Styles.Add(separator);
        return menu;
    }

    /// <summary>True for a caption created by <see cref="Section"/>.</summary>
    public static bool IsSection(Control control) => control is MenuItem item && item.Classes.Contains("section");

    /// <summary>The visible text of an entry built by this kit.</summary>
    public static string TextOf(Control control)
        => control is MenuItem { Header: TextBlock text } ? text.Text ?? string.Empty : control is Separator ? "-" : string.Empty;

    /// <summary>Drops leading, trailing and doubled separators so groups that came out empty leave no stray lines.</summary>
    public static List<Control> Tidy(IEnumerable<Control> items)
    {
        List<Control> result = [];
        foreach (Control item in items)
        {
            if (item is Separator && (result.Count == 0 || result[^1] is Separator))
            {
                continue;
            }

            result.Add(item);
        }

        while (result.Count > 0 && result[^1] is Separator)
        {
            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    /// <summary>
    ///  The data context of the row the event came from: the source's own data context when it differs from the host's (rows
    ///  of a list, tree or items control carry their item), otherwise null (empty space).
    /// </summary>
    public static object? RowOf(object? source, Control host)
        => source is StyledElement { DataContext: { } context } && !ReferenceEquals(context, host.DataContext) ? context : null;

    /// <summary>
    ///  Gives <paramref name="host"/> a context menu whose content is built each time it is requested, for the row under the
    ///  pointer. <paramref name="build"/> returns null to show no menu (e.g. when the pointer is on empty space).
    /// </summary>
    public static ContextMenu AttachBuilt(Control host, Func<object?, IEnumerable<Control>?> build)
    {
        ContextMenu menu = NewMenu();
        host.ContextMenu = menu;

        // The menu is filled before it opens; with nothing to show it stays closed (empty space, a folder row, ...).
        menu.Opening += (_, e) => e.Cancel = menu.Items.Count == 0;
        host.AddHandler(
            Control.ContextRequestedEvent,
            (_, e) =>
            {
                IEnumerable<Control>? items = build(RowOf(e.Source, host));
                menu.Items.Clear();
                foreach (Control control in items is null ? [] : Tidy(items))
                {
                    menu.Items.Add(control);
                }
            },
            Avalonia.Interactivity.RoutingStrategies.Tunnel);
        return menu;
    }
}
