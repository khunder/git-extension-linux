using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

// Window-level keyboard handling, overlay focus management, accessibility names and the responsive history row.
public sealed partial class MainWindow
{
    // The history list is narrower than these widths in the "medium" (no date) and "narrow" (no date, no author) states.
    internal const double HistoryMediumWidth = 640;
    internal const double HistoryNarrowWidth = 440;

    // Below this the ref badges give way as well, so the subject keeps its room.
    internal const double HistoryTinyWidth = 360;

    private static readonly string[] OverlayNames = ["SettingsOverlay", "FileHistoryOverlay", "CredentialOverlay", "PaletteOverlay", "ShortcutsOverlay"];

    // The commands that stay available while an overlay is open (so the palette and help can be toggled off again).
    private static readonly string[] OverlayCommandIds = ["palette.show", "help.shortcuts", "view.theme"];

    private IInputElement? _focusBeforeOverlay;
    private MainWindowViewModel? _shortcutViewModel;

    private void InitializeShortcuts()
    {
        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);
        AddHandler(GotFocusEvent, OnAnyGotFocus, RoutingStrategies.Bubble);
        DataContextChanged += (_, _) => AttachShortcuts();

        CommitList.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                UpdateHistoryLayout(CommitList.Bounds.Width);
            }
        };
        SizeChanged += (_, e) => ClampSidebarToWindow(e.NewSize.Width);
        Describe();
    }

    // The sidebar may not squeeze the history and changes columns off a small window (the window can be resized after the
    // layout was applied); a wide window keeps whatever width the user chose.
    private void ClampSidebarToWindow(double windowWidth)
    {
        ColumnDefinition sidebar = MainColumns.ColumnDefinitions[0];
        double limit = Math.Max(LayoutSettings.MinSidebarWidth, windowWidth * 0.25);
        if (!SidebarCollapsed && sidebar.Width.IsAbsolute && sidebar.Width.Value > limit)
        {
            sidebar.Width = new GridLength(limit);
        }
    }

    /// <summary>
    ///  The compact state of the history rows: "wide", "medium" (no date) or "narrow" (no date, no author; below 360px also no ref badges).
    /// </summary>
    public string HistoryLayoutState => CommitList.Classes.Contains("narrow") ? "narrow" : CommitList.Classes.Contains("medium") ? "medium" : "wide";

    private void UpdateHistoryLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        CommitList.Classes.Set("tiny", width < HistoryTinyWidth);
        CommitList.Classes.Set("narrow", width < HistoryNarrowWidth);
        CommitList.Classes.Set("medium", width >= HistoryNarrowWidth && width < HistoryMediumWidth);
    }

    private void AttachShortcuts()
    {
        if (_shortcutViewModel is not null)
        {
            _shortcutViewModel.UiRequested -= OnUiRequested;
        }

        _shortcutViewModel = DataContext as MainWindowViewModel;
        if (_shortcutViewModel is null)
        {
            return;
        }

        _shortcutViewModel.UiRequested += OnUiRequested;
        foreach (INotifyPropertyChanged overlay in new INotifyPropertyChanged[] { _shortcutViewModel.Settings, _shortcutViewModel.FileHistory, _shortcutViewModel.Credentials, _shortcutViewModel.Palette, _shortcutViewModel.ShortcutsHelp })
        {
            overlay.PropertyChanged += OnOverlayPropertyChanged;
        }
    }

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            CloseTopmostOverlay(viewModel, e);
            return;
        }

        IInputElement? focused = FocusManager?.GetFocusedElement();
        bool typing = focused is TextBox;
        bool credentials = viewModel.Credentials.IsOpen;
        bool overlay = viewModel.IsOverlayOpen;
        foreach (AppCommand command in viewModel.Commands)
        {
            foreach (CommandGesture gesture in command.Gestures)
            {
                if (!gesture.Matches(e))
                {
                    continue;
                }

                // Letters and "?" belong to the text box that has the focus.
                if (typing && gesture.IsTypedCharacter)
                {
                    return;
                }

                if (credentials || (overlay && !OverlayCommandIds.Contains(command.Id)) || (viewModel.Palette.IsOpen && command.Id != "palette.show"))
                {
                    return;
                }

                if (command.FocusScope is { } scope && !IsFocusWithin(focused, scope))
                {
                    return;
                }

                bool canExecute = command.CanExecute();
                if (canExecute)
                {
                    viewModel.Run(command);
                }

                // A disabled plain key is left alone; a disabled chord is still swallowed so it does nothing surprising.
                e.Handled = canExecute || !gesture.IsTypedCharacter;
                return;
            }
        }
    }

    private static bool IsFocusWithin(IInputElement? focused, string scopeName)
        => focused is Visual visual && visual.GetSelfAndVisualAncestors().OfType<Control>().Any(control => control.Name == scopeName);

    // Esc closes the topmost overlay (and only that one). It is decided here, before the focused control sees the key, so
    // an overlay opened on top of another one (the help over the settings) wins whatever has the focus.
    private static void CloseTopmostOverlay(MainWindowViewModel viewModel, KeyEventArgs e)
    {
        if (viewModel.Credentials.IsOpen)
        {
            // Keeps its own cancel semantics: cancelling answers git with "no".
            viewModel.Credentials.CancelCommand.Execute(null);
        }
        else if (viewModel.Palette.IsOpen)
        {
            viewModel.Palette.Close();
        }
        else if (viewModel.ShortcutsHelp.IsOpen)
        {
            viewModel.ShortcutsHelp.Close();
        }
        else if (viewModel.FileHistory.IsOpen)
        {
            viewModel.FileHistory.CloseCommand.Execute(null);
        }
        else if (viewModel.Settings.IsOpen)
        {
            viewModel.Settings.CancelCommand.Execute(null);
        }
        else
        {
            return;
        }

        e.Handled = true;
    }

    // Remember the last control outside the overlays that had the focus, to give it back when the overlays close.
    private void OnAnyGotFocus(object? sender, GotFocusEventArgs e)
    {
        if (e.Source is Visual source && !IsInsideOverlay(source))
        {
            _focusBeforeOverlay = e.Source as IInputElement;
        }
    }

    private bool IsInsideOverlay(Visual visual)
        => visual.GetSelfAndVisualAncestors().OfType<Control>().Any(control => control.Name is { } name && OverlayNames.Contains(name));

    private void OnOverlayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "IsOpen" || DataContext is not MainWindowViewModel viewModel || viewModel.IsOverlayOpen)
        {
            return;
        }

        // The last overlay closed.
        if (_focusBeforeOverlay is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } previous && previous.GetVisualRoot() is not null)
        {
            previous.Focus();
        }
    }

    private void OnUiRequested(UiTarget target)
    {
        switch (target)
        {
            case UiTarget.PathBox:
                PathBox.Focus();
                PathBox.SelectAll();
                break;
            case UiTarget.SidebarFilter:
                if (SidebarCollapsed)
                {
                    SidebarCollapsed = false;
                }

                SidebarPanel.FindControl<TextBox>("FilterBox")?.Focus();
                break;
            case UiTarget.CommitMessage:
                CommitPanel.FindControl<TextBox>("MessageBox")?.Focus();
                break;
            case UiTarget.ToggleSidebar:
                SidebarCollapsed = !SidebarCollapsed;
                break;
            case UiTarget.ShowRecent:
                if (RecentButton.Flyout is { } recent)
                {
                    recent.ShowAt(RecentButton);
                }

                break;
            case UiTarget.ShowStash:
                if (this.GetVisualDescendants().OfType<StashMenuButton>().FirstOrDefault() is { } stash)
                {
                    stash.Flyout?.ShowAt(stash);
                    (DataContext as MainWindowViewModel)?.Stash.RequestStashCommand.Execute(null);
                }

                break;
        }
    }

    // Names for icon-only and ambiguous controls, so screen readers announce more than "button".
    private void Describe()
    {
        static void Name(Control? control, string name, string? help = null)
        {
            if (control is null)
            {
                return;
            }

            AutomationProperties.SetName(control, name);
            if (help is not null)
            {
                AutomationProperties.SetHelpText(control, help);
            }
        }

        Name(PathBox, "Repository path", "Type the path of a Git repository and press Enter to open it");
        Name(RecentButton, "Recent repositories");
        Name(SettingsButton, "Settings", "Open the settings (Ctrl+,)");
        Name(this.FindControl<Button>("SidebarToggle"), "Toggle branches sidebar", "Hide or show the branches sidebar (Ctrl+B)");
        Name(CommitList, "Commit history", "J and K or the arrow keys select the next or previous commit");
        Name(this.FindControl<ListBox>("ChangeList"), "Changed files", "Files changed in the working tree");
        Name(SidebarPanel.FindControl<TextBox>("FilterBox"), "Filter branches and tags", "Ctrl+F focuses this box");
        Name(SidebarPanel.FindControl<TreeView>("RefsTree"), "Branches, remotes and tags");
        Name(CommitPanel.FindControl<TextBox>("MessageBox"), "Commit message", "Ctrl+Enter commits");
        Name(this.FindControl<Control>("SettingsOverlay"), "Settings");
        Name(this.FindControl<Control>("FileHistoryOverlay"), "File history and blame");
        Name(this.FindControl<Control>("CredentialOverlay"), "Credentials requested by Git");
        Name(this.FindControl<Control>("PaletteOverlay"), "Command palette");
        Name(this.FindControl<Control>("ShortcutsOverlay"), "Keyboard shortcuts");
        Name(this.FindControl<Control>("StartView"), "Recent repositories");
    }
}
