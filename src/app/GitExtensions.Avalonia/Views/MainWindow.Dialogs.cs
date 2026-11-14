using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

// The commands that open a dialog: each one is a view-model with an IsOpen flag that DialogHost turns into a modal window.
public sealed partial class MainWindow
{
    private MainWindowViewModel? _dialogsBoundTo;

    private void InitializeDialogs()
        => DataContextChanged += (_, _) => BindDialogs();

    private void BindDialogs()
    {
        if (DataContext is not MainWindowViewModel viewModel || ReferenceEquals(_dialogsBoundTo, viewModel))
        {
            return;
        }

        _dialogsBoundTo = viewModel;
        DialogHost.Bind(this, viewModel.Remotes, () => new RemotesView { DataContext = viewModel.Remotes }, "Remote repositories", 860, 580);
        BindCoreDialogs(viewModel);
        BindRevisionDialogs(viewModel);
        BindToolDialogs(viewModel);
        BindPullPushDialogs(viewModel);
    }
}
