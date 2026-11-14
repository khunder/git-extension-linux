using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The in-window command palette overlay. Up/Down/Enter are handled here; Esc is handled by the window (topmost overlay first).
/// </summary>
public sealed partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();
        FilterBox.AddHandler(KeyDownEvent, OnFilterKeyDown, RoutingStrategies.Tunnel);
        CommandList.Tapped += OnListTapped;
        Scrim.PointerPressed += OnScrimPressed;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                // After layout, so the box can take the focus.
                Dispatcher.UIThread.Post(() => FilterBox.Focus(), DispatcherPriority.Loaded);
            }
        };
        CommandList.SelectionChanged += (_, _) =>
        {
            if (CommandList.SelectedItem is { } item)
            {
                CommandList.ScrollIntoView(item);
            }
        };
    }

    private void OnFilterKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                viewModel.Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                viewModel.Move(-1);
                e.Handled = true;
                break;
            case Key.Enter or Key.Return:
                viewModel.ExecuteSelected();
                e.Handled = true;
                break;
        }
    }

    private void OnListTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is CommandPaletteViewModel viewModel && (e.Source as StyledElement)?.DataContext is PaletteItem item)
        {
            viewModel.Execute(item);
        }
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only a click outside the card.
        if (e.Source == Scrim && DataContext is CommandPaletteViewModel viewModel)
        {
            viewModel.Close();
        }
    }
}
