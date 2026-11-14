using Avalonia.Controls;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class InitView : UserControl
{
    public InitView()
    {
        InitializeComponent();

        // Focus the folder field when the dialog opens.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible && DataContext is InitViewModel)
            {
                this.FindControl<TextBox>("FolderBox")?.Focus();
            }
        };
    }
}
