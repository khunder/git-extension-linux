using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.Views;

/// <summary>The Console tab: the git commands the application ran.</summary>
public sealed partial class CommandLogView : UserControl
{
    private CommandLogService? _service;

    public CommandLogView()
    {
        InitializeComponent();

        // Show the newest entries when the view appears (the dialog opens on an existing log).
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_service is not null)
        {
            _service.Items.CollectionChanged -= OnItemsChanged;
        }

        _service = DataContext as CommandLogService;
        if (_service is not null)
        {
            _service.Items.CollectionChanged += OnItemsChanged;
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Follow the newest entry, like a terminal.
        Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void OnClearClick(object? sender, RoutedEventArgs e) => _service?.Clear();

    private void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (_service is not null)
        {
            _ = new TopLevelClipboardService(TopLevel.GetTopLevel(this)).SetTextAsync(_service.GetText());
        }
    }
}
