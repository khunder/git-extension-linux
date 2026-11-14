using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

public sealed record ShortcutEntry(string Title, string Gesture);

public sealed record ShortcutGroup(string Name, IReadOnlyList<ShortcutEntry> Entries);

/// <summary>
///  The "Keyboard shortcuts" overlay: every command of the registry that has a gesture, grouped.
/// </summary>
public sealed partial class ShortcutsHelpViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<AppCommand>> _commands;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private IReadOnlyList<ShortcutGroup> _groups = [];

    public ShortcutsHelpViewModel(Func<IReadOnlyList<AppCommand>> commands) => _commands = commands;

    [RelayCommand]
    public void Open()
    {
        Groups = _commands()
            .Where(command => command.Gestures.Count > 0)
            .GroupBy(command => command.Group)
            .Select(group => new ShortcutGroup(group.Key, group.Select(command => new ShortcutEntry(command.Title, command.GestureText)).ToList()))
            .ToList();
        IsOpen = true;
    }

    [RelayCommand]
    public void Close() => IsOpen = false;

    [RelayCommand]
    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }
}
