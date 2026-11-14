using Avalonia.Controls;
using Avalonia.Threading;

namespace GitExtensions.Desktop.Views;

public sealed partial class GoToCommitView : UserControl
{
    public GoToCommitView()
    {
        InitializeComponent();

        // After layout, so the box can take the focus.
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => ExpressionBox.Focus(), DispatcherPriority.Loaded);
    }
}
