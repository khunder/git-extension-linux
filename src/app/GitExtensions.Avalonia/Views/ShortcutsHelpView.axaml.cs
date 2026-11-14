using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The "Keyboard shortcuts" overlay. Esc is handled by the window (topmost overlay first).
/// </summary>
public sealed partial class ShortcutsHelpView : UserControl
{
    public ShortcutsHelpView()
    {
        InitializeComponent();
        Scrim.PointerPressed += (_, e) =>
        {
            if (e.Source == Scrim && DataContext is ShortcutsHelpViewModel viewModel)
            {
                viewModel.Close();
            }
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Take the focus when the overlay opens, so Esc closes it and the page keys scroll the list.
        if (change.Property == IsVisibleProperty && change.NewValue is true)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }
}
