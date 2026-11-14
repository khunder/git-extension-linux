using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The dashboard (start page). Enter is bound in XAML; a double click on an entry opens it.
/// </summary>
public sealed partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        AddHandler(DoubleTappedEvent, OnDoubleTapped);
        AttachedToVisualTree += (_, _) => AttachClipboard();
        DataContextChanged += (_, _) => AttachClipboard();
    }

    // The clipboard belongs to the window, so the view-model gets it once both exist.
    private void AttachClipboard()
    {
        if (DataContext is DashboardViewModel viewModel && TopLevel.GetTopLevel(this) is { } topLevel)
        {
            viewModel.Clipboard ??= new TopLevelClipboardService(topLevel);
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is DashboardViewModel viewModel
            && (e.Source as StyledElement)?.DataContext is DashboardEntry entry)
        {
            viewModel.OpenCommand.Execute(entry);
        }
    }
}
