using Avalonia.Controls;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class CommitActionPromptView : UserControl
{
    public CommitActionPromptView()
    {
        InitializeComponent();

        // Focus the name box when the dialog opens.
        AttachedToVisualTree += (_, _) =>
        {
            if (DataContext is CommitActionsViewModel actions && actions.NeedsInput)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("PromptInput")?.Focus(), Avalonia.Threading.DispatcherPriority.Loaded);
            }
        };
    }
}
