using Avalonia.Controls;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class FormatPatchView : UserControl
{
    public FormatPatchView()
    {
        InitializeComponent();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible && DataContext is FormatPatchViewModel)
            {
                this.FindControl<TextBox>("FolderBox")?.Focus();
            }
        };
    }
}
