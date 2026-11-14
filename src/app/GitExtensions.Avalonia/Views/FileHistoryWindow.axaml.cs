using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The non-modal File History window (see <see cref="FileHistoryWindowViewModel"/>).
/// </summary>
public sealed partial class FileHistoryWindow : Window
{
    private readonly WindowStateStore? _stateStore;
    private bool _syncingScroll;

    public FileHistoryWindow()
        : this(new FileHistoryWindowViewModel(() => null, string.Empty, string.Empty), null)
    {
    }

    public FileHistoryWindow(FileHistoryWindowViewModel viewModel, WindowStateStore? stateStore)
    {
        _stateStore = stateStore;
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Clipboard ??= new TopLevelClipboardService(this);
        stateStore?.Apply(this);
        DialogHost.Bind(this, viewModel.CommitDetails, () => new CommitNotesView { DataContext = viewModel.CommitDetails }, "Commit notes", 520, 300);

        AuthorGrip.DragDelta += (_, e) => Resize(c => c.AuthorWidth, (c, w) => c.AuthorWidth = w, e);
        DateGrip.DragDelta += (_, e) => Resize(c => c.DateWidth, (c, w) => c.DateWidth = w, e);
        HashGrip.DragDelta += (_, e) => Resize(c => c.HashWidth, (c, w) => c.HashWidth = w, e);

        // The blame has two lists (annotations, text) that scroll as one; clicking a block selects its commit.
        ItemsControl text = BlameText;
        ItemsControl annotations = BlameAnnotations;
        text.AddHandler(ScrollViewer.ScrollChangedEvent, (_, e) => SyncScroll(text, annotations, e));
        annotations.AddHandler(ScrollViewer.ScrollChangedEvent, (_, e) => SyncScroll(annotations, text, e));
        text.AddHandler(PointerPressedEvent, OnBlamePressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        annotations.AddHandler(PointerPressedEvent, OnBlamePressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);

        Closing += (_, _) => _stateStore?.Capture(this);
        Closed += (_, _) => _ = viewModel.CloseAsync();
    }

    public FileHistoryWindowViewModel ViewModel { get; }

    /// <summary>
    ///  Focuses the filter text box (Ctrl+F).
    /// </summary>
    public void FocusFilter()
    {
        TextBox box = FilterBox;
        box.Focus();
        box.SelectAll();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            ViewModel.RefreshCommand.Execute(null);
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            FocusFilter();
        }
        else if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            ViewModel.CopyHashCommand.Execute(null);
        }
    }

    private void Resize(Func<FileHistoryColumns, double> get, Action<FileHistoryColumns, double> set, VectorEventArgs e)
    {
        // The grip sits at the left edge of its column: dragging left makes the column wider.
        set(ViewModel.Columns, Math.Max(FileHistoryColumns.MinWidth, get(ViewModel.Columns) - e.Vector.X));
    }

    private void SyncScroll(ItemsControl source, ItemsControl target, ScrollChangedEventArgs e)
    {
        if (_syncingScroll || e.Source is not ScrollViewer sourceViewer || sourceViewer.GetVisualParent() is null)
        {
            return;
        }

        ScrollViewer? targetViewer = target.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (targetViewer is null || !ReferenceEquals(source.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault(), sourceViewer))
        {
            return;
        }

        _syncingScroll = true;
        try
        {
            if (Math.Abs(targetViewer.Offset.Y - sourceViewer.Offset.Y) > 0.01)
            {
                targetViewer.Offset = new Vector(targetViewer.Offset.X, sourceViewer.Offset.Y);
            }
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    private void OnBlamePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is StyledElement { DataContext: BlameLineViewModel line })
        {
            ViewModel.Blame.SelectedLine = line;
        }
    }
}
