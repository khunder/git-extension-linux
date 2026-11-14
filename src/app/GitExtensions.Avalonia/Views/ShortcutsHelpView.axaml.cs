using Avalonia.Controls;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The content of the "Keyboard shortcuts" dialog. Esc (handled by the window) closes it.
/// </summary>
public sealed partial class ShortcutsHelpView : UserControl
{
    public ShortcutsHelpView()
    {
        InitializeComponent();

        // Take the focus when the dialog opens, so the page keys scroll the list.
        AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
    }
}
