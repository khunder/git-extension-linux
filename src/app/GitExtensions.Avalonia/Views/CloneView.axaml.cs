using Avalonia.Controls;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class CloneView : UserControl
{
    public CloneView()
    {
        InitializeComponent();

        // Focus the URL field when the dialog opens.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible && DataContext is CloneViewModel)
            {
                this.FindControl<TextBox>("UrlBox")?.Focus();
            }
        };
    }
}
