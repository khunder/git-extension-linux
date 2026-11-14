using Avalonia.Controls;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachRecentFlyout();
    }

    // The flyout is not part of the window's tree, so give it its own view and view-model explicitly.
    private void AttachRecentFlyout()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Flyout flyout = new()
        {
            Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight,
            Content = new RecentRepositoriesView { DataContext = viewModel.Recent, MaxHeight = 420 },
        };
        viewModel.Recent.Chosen += flyout.Hide;
        this.FindControl<Button>("RecentButton")!.Flyout = flyout;
    }
}
