using Avalonia.Controls;
using Avalonia.Input;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class RefsTreeView : UserControl
{
    public RefsTreeView()
    {
        InitializeComponent();
        RefsTree.DoubleTapped += OnDoubleTapped;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is RefsTreeViewModel viewModel && viewModel.SelectedNode is { CanCheckout: true } node)
        {
            viewModel.CheckoutCommand.Execute(node);
        }
    }
}
