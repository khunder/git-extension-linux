using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views.ContextMenus;

namespace GitExtensions.Desktop.Views;

public sealed partial class RefsTreeView : UserControl
{
    public RefsTreeView()
    {
        InitializeComponent();
        RefsTree.DoubleTapped += OnDoubleTapped;
        MenuKit.AttachBuilt(
            RefsTree,
            row => row is RefNodeViewModel node && DataContext is RefsTreeViewModel refs
                ? RefsContextMenu.Build(refs, this.FindAncestorOfType<Window>()?.DataContext as MainWindowViewModel, node, new TopLevelClipboardService(TopLevel.GetTopLevel(this)))
                : null);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is RefsTreeViewModel viewModel && viewModel.SelectedNode is { CanCheckout: true } node)
        {
            viewModel.CheckoutCommand.Execute(node);
        }
    }
}
