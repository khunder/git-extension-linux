using Avalonia;
using Avalonia.Controls;

namespace GitExtensions.Desktop.Views;

public sealed partial class FileHistoryView : UserControl
{
    public FileHistoryView()
    {
        InitializeComponent();
        Focusable = true;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Take the focus when the overlay opens, so Esc closes it.
        if (change.Property == IsVisibleProperty && change.NewValue is true)
        {
            Focus();
        }
    }
}
