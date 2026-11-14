using Avalonia.Controls;

namespace GitExtensions.Desktop.Views;

public sealed partial class InitView : UserControl
{
    public InitView()
    {
        InitializeComponent();

        // Focus the FolderBox field when the dialog opens (after layout, so it can take the focus).
        AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(
            () => this.FindControl<TextBox>("FolderBox")?.Focus(),
            Avalonia.Threading.DispatcherPriority.Loaded);
    }
}
