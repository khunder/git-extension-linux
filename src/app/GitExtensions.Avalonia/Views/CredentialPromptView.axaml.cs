using Avalonia.Controls;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class CredentialPromptView : UserControl
{
    public CredentialPromptView()
    {
        InitializeComponent();

        // Focus the answer field (or the OK button of a confirmation) when the dialog opens.
        AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                if (DataContext is CredentialPromptViewModel { ShowTextInput: true })
                {
                    this.FindControl<TextBox>("InputBox")?.Focus();
                }
                else
                {
                    this.FindControl<Button>("OkButton")?.Focus();
                }
            },
            Avalonia.Threading.DispatcherPriority.Loaded);
    }
}
