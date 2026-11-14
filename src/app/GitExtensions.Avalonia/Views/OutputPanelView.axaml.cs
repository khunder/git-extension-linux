using Avalonia.Controls;
using Avalonia.Threading;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class OutputPanelView : UserControl
{
    public OutputPanelView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is RemoteOperationsViewModel viewModel)
            {
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(RemoteOperationsViewModel.OutputText))
                    {
                        // Keep the newest output in view once the text has been laid out.
                        Dispatcher.UIThread.Post(OutputScroll.ScrollToEnd, DispatcherPriority.Background);
                    }
                };
            }
        };
    }
}
