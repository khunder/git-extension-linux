using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

// The Pull and Push dialogs.
public sealed partial class MainWindow
{
    private void BindPullPushDialogs(MainWindowViewModel viewModel)
    {
        DialogHost.Bind(this, viewModel.Pull, () => new PullView { DataContext = viewModel.Pull }, "Pull", 720, 860);
        DialogHost.Bind(this, viewModel.Push, () => new PushView { DataContext = viewModel.Push }, "Push", 680, 760);
    }
}
