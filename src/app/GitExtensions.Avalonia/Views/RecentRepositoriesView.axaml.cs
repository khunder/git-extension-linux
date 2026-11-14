using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class RecentRepositoriesView : UserControl
{
    public RecentRepositoriesView()
        => InitializeComponent();

    // A click on an entry opens it; Enter is bound in XAML.
    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is RecentRepositoriesViewModel viewModel
            && (e.Source as StyledElement)?.DataContext is RecentRepositoryItem item)
        {
            viewModel.OpenCommand.Execute(item);
        }
    }
}
