using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views.ContextMenus;

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
        MenuKit.AttachBuilt(this, row => row is DashboardEntry entry && DataContext is DashboardViewModel dashboard ? DashboardContextMenu.Build(dashboard, entry) : null);
        AddHandler(SelectingItemsControl.SelectionChangedEvent, OnSelectionChanged);
        AddHandler(KeyDownEvent, OnKeyDown);
        AttachedToVisualTree += (_, _) =>
        {
            AttachClipboard();
            FocusFirstEntry();
        };
        DataContextChanged += (_, _) =>
        {
            AttachClipboard();
            if (DataContext is DashboardViewModel viewModel)
            {
                // The entries arrive after the page is shown: select the first one once they are there.
                viewModel.Categories.CollectionChanged += (_, _) => FocusFirstEntry();
            }
        };
    }

    private DashboardEntry? _selected;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
        {
            FocusFirstEntry();
        }
    }

    // The entries are grouped in one list per category; only one entry is selected across all of them.
    private IEnumerable<ListBox> RepositoryLists() => this.GetVisualDescendants().OfType<ListBox>().Where(list => list.Classes.Contains("repos"));

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not ListBox changed || !changed.Classes.Contains("repos") || changed.SelectedItem is not DashboardEntry entry)
        {
            return;
        }

        _selected = entry;
        foreach (ListBox other in RepositoryLists().Where(list => !ReferenceEquals(list, changed) && list.SelectedItem is not null))
        {
            other.SelectedItem = null;
        }
    }

    /// <summary>Selects the first repository and gives it the focus so Enter and the arrow keys work right away.</summary>
    private void FocusFirstEntry()
    {
        if (!IsVisible)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () =>
            {
                ListBox? first = RepositoryLists().FirstOrDefault(list => list.ItemCount > 0);
                if (first is null || RepositoryLists().Any(list => list.SelectedItem is not null) || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
                {
                    return;
                }

                first.SelectedIndex = 0;
                (first.ContainerFromIndex(0) as Control ?? first).Focus();
            },
            DispatcherPriority.Loaded);
    }

    // Enter opens the selected repository wherever the focus is on the page (but not while typing in a text box).
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return) || e.KeyModifiers != KeyModifiers.None || e.Source is TextBox or Button
            || DataContext is not DashboardViewModel viewModel)
        {
            return;
        }

        DashboardEntry? entry = RepositoryLists().Select(list => list.SelectedItem).OfType<DashboardEntry>().FirstOrDefault()
            ?? _selected ?? RepositoryLists().SelectMany(list => list.Items.OfType<DashboardEntry>()).FirstOrDefault();
        if (entry is not null && viewModel.OpenCommand.CanExecute(entry))
        {
            viewModel.OpenCommand.Execute(entry);
            e.Handled = true;
        }
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
