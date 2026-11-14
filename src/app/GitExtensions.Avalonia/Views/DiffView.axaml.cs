using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public partial class DiffView : UserControl
{
    public DiffView() => InitializeComponent();

    private void OnLinePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: DiffLine line } && DataContext is FileDiffViewModel viewModel
            && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            viewModel.SelectLine(line, e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
    }

    private void OnHunkAction(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DiffLine hunk, Tag: string action } && DataContext is FileDiffViewModel viewModel)
        {
            // The commands handle their own failures and report them through the view-model's message.
            switch (action)
            {
                case "stage":
                    viewModel.StageHunkCommand.Execute(hunk);
                    break;
                case "unstage":
                    viewModel.UnstageHunkCommand.Execute(hunk);
                    break;
                case "discard":
                    viewModel.DiscardHunkCommand.Execute(hunk);
                    break;
            }

            e.Handled = true;
        }
    }
}
