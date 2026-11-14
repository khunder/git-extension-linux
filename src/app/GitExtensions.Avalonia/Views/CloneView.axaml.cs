using Avalonia.Controls;

namespace GitExtensions.Desktop.Views;

public sealed partial class CloneView : UserControl
{
    public CloneView()
    {
        InitializeComponent();

        // Focus the UrlBox field when the dialog opens (after layout, so it can take the focus).
        AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(
            () => this.FindControl<TextBox>("UrlBox")?.Focus(),
            Avalonia.Threading.DispatcherPriority.Loaded);
    }
}
