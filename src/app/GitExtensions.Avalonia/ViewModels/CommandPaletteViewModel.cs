using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  One row of the command palette.
/// </summary>
public sealed record PaletteItem(AppCommand Command)
{
    public string Title => Command.Title;

    public string Gesture => Command.PrimaryGestureText;

    public string Group => Command.Group;
}

/// <summary>
///  The command palette: a filter box over the enabled commands. Up/Down move the selection, Enter runs it, Esc closes.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<AppCommand>> _commands;
    private readonly Action<AppCommand> _run;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private int _selectedIndex = -1;

    public CommandPaletteViewModel(Func<IReadOnlyList<AppCommand>> commands, Action<AppCommand>? run = null)
    {
        _commands = commands;
        _run = run ?? (static command => command.Execute());
    }

    public ObservableCollection<PaletteItem> Items { get; } = [];

    public PaletteItem? SelectedItem => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;

    /// <summary>Opens with an empty filter, listing the commands that can run right now.</summary>
    [RelayCommand]
    public void Open()
    {
        Filter = string.Empty;
        Refresh();
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

    /// <summary>Moves the selection by <paramref name="delta"/>, wrapping around.</summary>
    public void Move(int delta)
    {
        if (Items.Count == 0)
        {
            return;
        }

        SelectedIndex = ((SelectedIndex < 0 ? 0 : SelectedIndex) + delta + Items.Count) % Items.Count;
    }

    /// <summary>Closes the palette (restoring the previous focus) and runs the selected command.</summary>
    [RelayCommand]
    public void ExecuteSelected() => Execute(SelectedItem);

    public void Execute(PaletteItem? item)
    {
        if (item is null)
        {
            return;
        }

        Close();
        if (item.Command.CanExecute())
        {
            _run(item.Command);
        }
    }

    partial void OnFilterChanged(string value) => Refresh();

    private void Refresh()
    {
        IEnumerable<PaletteItem> candidates = _commands().Where(command => command.ShowInPalette && command.CanExecute()).Select(command => new PaletteItem(command));
        IReadOnlyList<PaletteItem> ranked = CommandMatcher.Rank(candidates, Filter, item => item.Title);
        Items.Clear();
        foreach (PaletteItem item in ranked)
        {
            Items.Add(item);
        }

        SelectedIndex = Items.Count > 0 ? 0 : -1;
    }
}
