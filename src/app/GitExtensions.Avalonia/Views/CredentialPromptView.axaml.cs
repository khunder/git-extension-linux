using Avalonia.Controls;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class CredentialPromptView : UserControl
{
    public CredentialPromptView()
    {
        InitializeComponent();

        // Focus the answer field when the prompt opens.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible && DataContext is CredentialPromptViewModel { ShowTextInput: true })
            {
                this.FindControl<TextBox>("InputBox")?.Focus();
            }
        };
    }
}
