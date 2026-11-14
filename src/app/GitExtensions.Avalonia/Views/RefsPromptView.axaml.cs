using Avalonia.Controls;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class RefsPromptView : UserControl
{
    public RefsPromptView()
    {
        InitializeComponent();

        // Focus the name box when the dialog opens.
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is RefsTreeViewModel refs && refs.ShowName)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("RefsPromptName")?.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
            }
        };
    }
}
