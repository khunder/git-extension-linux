using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The Commit window: a separate, non-modal window like the original Git Extensions commit dialog.
/// </summary>
public sealed partial class CommitWindow : Window
{
    private bool _forceClose;
    private CommitWindowViewModel? _attached;

    public CommitWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Closing += OnClosing;
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    ///  Closes without asking about an unsaved message.
    /// </summary>
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private CommitWindowViewModel? ViewModel => DataContext as CommitWindowViewModel;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_attached is not null)
        {
            _attached.CloseRequested -= OnCloseRequested;
        }

        _attached = ViewModel;
        if (_attached is not null)
        {
            _attached.CloseRequested += OnCloseRequested;
        }
    }

    private void OnCloseRequested() => Dispatcher.UIThread.Post(ForceClose);

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose || e.CloseReason is WindowCloseReason.OwnerWindowClosing or WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
        {
            return;
        }

        if (ViewModel is { } viewModel && !viewModel.CanCloseWithoutAsking())
        {
            e.Cancel = true;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.CommitNowCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.RequestEscape();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            viewModel.RefreshCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.S or Key.U or Key.R)
        {
            e.Handled = RunFileShortcut(viewModel, e.Key);
        }
    }

    // S stages, U unstages and R resets the selected file(s), whichever control has the focus. Not while typing, and not when
    // the diff view has the focus with selected lines: its own handler then stages, unstages or resets those lines.
    private bool RunFileShortcut(CommitWindowViewModel viewModel, Key key)
    {
        IInputElement? focused = FocusManager?.GetFocusedElement();
        if (focused is TextBox || IsWithin(focused, ConflictPanel) || (viewModel.Diff.HasSelection && IsWithin(focused, DiffPanel)))
        {
            return false;
        }

        // The side of the file that is selected: the staged list when it has the focus or holds the selected file.
        bool staged = IsWithin(focused, StagedList) || (!IsWithin(focused, UnstagedList) && viewModel.SelectedStaged is not null && viewModel.SelectedUnstaged is null);
        IReadOnlyList<ChangedFileViewModel> files = staged ? viewModel.StagedSelection : viewModel.UnstagedSelection;
        if (files.Count == 0)
        {
            return false;
        }

        switch (key)
        {
            case Key.S when !staged:
                viewModel.StageSelectedCommand.Execute(null);
                return true;
            case Key.U when staged:
                viewModel.UnstageSelectedCommand.Execute(null);
                return true;
            case Key.R:
                viewModel.BeginReset(files);
                return true;
            default:
                return false;
        }
    }

    private static bool IsWithin(IInputElement? focused, Control container)
        => focused is Avalonia.Visual visual && (visual == container || visual.GetVisualAncestors().Contains(container));

    private void OnUnstagedSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && ViewModel is { } viewModel)
        {
            viewModel.UnstagedSelection = [.. list.SelectedItems?.OfType<ChangedFileViewModel>() ?? []];
        }
    }

    private void OnStagedSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && ViewModel is { } viewModel)
        {
            viewModel.StagedSelection = [.. list.SelectedItems?.OfType<ChangedFileViewModel>() ?? []];
        }
    }

    private void OnUnstagedDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is { } viewModel && e.Source is Control { DataContext: ChangedFileViewModel })
        {
            viewModel.StageSelectedCommand.Execute(null);
        }
    }

    private void OnStagedDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is { } viewModel && e.Source is Control { DataContext: ChangedFileViewModel })
        {
            viewModel.UnstageSelectedCommand.Execute(null);
        }
    }

    private void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ChangedFileViewModel file } && ViewModel is { } viewModel)
        {
            // Handles its own failures.
            _ = OpenFileAsync(viewModel, file);
        }
    }

    private void OnCopyPath(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ChangedFileViewModel file } && ViewModel is { } viewModel)
        {
            _ = CopyPathAsync(viewModel, file);
        }
    }

    private async Task OpenFileAsync(CommitWindowViewModel viewModel, ChangedFileViewModel file)
    {
        try
        {
            string path = System.IO.Path.Combine(viewModel.WorkingDir, file.Name);
            if (await StorageProvider.TryGetFileFromPathAsync(new Uri(path)) is { } item && Launcher is { } launcher)
            {
                await launcher.LaunchFileAsync(item);
            }
            else
            {
                viewModel.ErrorText = $"Could not open {file.Name}.";
            }
        }
        catch (Exception ex)
        {
            viewModel.ErrorText = $"Could not open {file.Name}: {ex.Message}";
        }
    }

    private async Task CopyPathAsync(CommitWindowViewModel viewModel, ChangedFileViewModel file)
    {
        try
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(System.IO.Path.Combine(viewModel.WorkingDir, file.Name));
            }
        }
        catch (Exception ex)
        {
            viewModel.ErrorText = $"Could not copy the path: {ex.Message}";
        }
    }

    private void OnTemplatesClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control anchor && ViewModel is { } viewModel)
        {
            ShowMenu(anchor, viewModel.Templates.Select(t => (t.Name, (Action)(() => viewModel.ApplyTemplate(t)))));
        }
    }

    private void OnRecentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control anchor && ViewModel is { } viewModel)
        {
            ShowMenu(anchor, viewModel.RecentMessages.Select(m => (Summarize(m), (Action)(() => viewModel.UseRecentMessage(m)))));
        }
    }

    private static string Summarize(string message)
    {
        string first = message.Split('\n')[0].Trim();
        return first.Length > 80 ? first[..79] + "…" : first;
    }

    private static void ShowMenu(Control anchor, IEnumerable<(string Header, Action Run)> entries)
    {
        ContextMenu menu = new();
        foreach ((string header, Action run) in entries)
        {
            MenuItem item = new() { Header = new TextBlock { Text = header } };
            item.Click += (_, _) => run();
            menu.Items.Add(item);
        }

        menu.Open(anchor);
    }
}
