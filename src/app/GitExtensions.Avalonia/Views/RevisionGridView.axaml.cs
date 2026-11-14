using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The revision grid: filter bar, header row with resizable columns, the "Uncommitted changes" row and the virtualized
///  list of commits. Its DataContext is the <see cref="MainWindowViewModel"/>.
/// </summary>
public sealed partial class RevisionGridView : UserControl
{
    // The grid is narrower than these widths in the "medium" (no date) and "narrow" (no date, no author, no avatar) states.
    internal const double HistoryMediumWidth = 640;
    internal const double HistoryNarrowWidth = 440;

    // Below this the ref badges and the hash give way as well, so the subject keeps its room.
    internal const double HistoryTinyWidth = 360;

    private string? _resizing;
    private double _resizeStart;

    public RevisionGridView()
    {
        InitializeComponent();

        AutomationProperties.SetName(CommitList, "Commit history");
        AutomationProperties.SetHelpText(CommitList, "J and K or the arrow keys select the next or previous commit");

        CommitList.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                UpdateLayoutState(CommitList.Bounds.Width);
            }
        };
        AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
        UncommittedRow.Focusable = true;
        CommitList.DoubleTapped += (_, _) => RowActivated?.Invoke(this, EventArgs.Empty);
        UncommittedRow.PointerPressed += OnUncommittedPressed;

        foreach (Border grip in HeaderRow.GetLogicalDescendants().OfType<Border>().Where(b => b.Classes.Contains("grip")))
        {
            AttachGrip(grip);
        }
    }

    /// <summary>Raised when a row is activated (double click or Enter).</summary>
    public event EventHandler? RowActivated;

    /// <summary>The virtualized list of commits.</summary>
    public ListBox List => CommitList;

    /// <summary>The "Filter:" text box of the filter bar.</summary>
    public TextBox FilterTextBox => FilterBar.Box;

    public RevisionGridColumns? Columns => (DataContext as MainWindowViewModel)?.Columns;

    /// <summary>
    ///  The compact state of the rows: "wide", "medium" (no date) or "narrow" (no date, no author; below 360px also no ref badges and no hash).
    /// </summary>
    public string HistoryLayoutState => CommitList.Classes.Contains("narrow") ? "narrow" : CommitList.Classes.Contains("medium") ? "medium" : "wide";

    private void UpdateLayoutState(double width)
    {
        if (width <= 0)
        {
            return;
        }

        CommitList.Classes.Set("tiny", width < HistoryTinyWidth);
        CommitList.Classes.Set("narrow", width < HistoryNarrowWidth);
        CommitList.Classes.Set("medium", width >= HistoryNarrowWidth && width < HistoryMediumWidth);
        if (Columns is { } columns)
        {
            columns.ShowDate = width >= HistoryMediumWidth;
            columns.ShowAuthor = width >= HistoryNarrowWidth;
            columns.ShowHash = width >= HistoryTinyWidth;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UpdateLayoutState(CommitList.Bounds.Width);
    }

    private void OnUncommittedPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && e.GetCurrentPoint(UncommittedRow).Properties.IsLeftButtonPressed)
        {
            viewModel.IsWorkingTreeSelected = true;
            UncommittedRow.Focus();
            e.Handled = true;
        }
    }

    // Moving between the "Uncommitted changes" row and the list, and Enter.
    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        // Typing in the filter box (or the combo) is not row navigation.
        if (DataContext is not MainWindowViewModel viewModel || e.Source is TextBox or ComboBox or ComboBoxItem)
        {
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            RowActivated?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (viewModel.IsWorkingTreeSelected)
        {
            if (e.Key is Key.Down or Key.PageDown or Key.Home or Key.End && viewModel.Commits.Count > 0)
            {
                Select(e.Key == Key.End ? viewModel.Commits.Count - 1 : 0);
                e.Handled = true;
            }
            else if (e.Key is Key.Up or Key.PageUp)
            {
                e.Handled = true;
            }

            return;
        }

        int count = viewModel.Commits.Count;
        int current = CommitList.SelectedIndex;
        if (count == 0 || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up or Key.Home when current == 0 && viewModel.ShowUncommittedRow:
                viewModel.IsWorkingTreeSelected = true;
                UncommittedRow.Focus();
                break;
            case Key.PageDown:
                Select(Math.Min(count - 1, Math.Max(current, 0) + PageSize()));
                break;
            case Key.PageUp:
                Select(Math.Max(0, (current < 0 ? 0 : current) - PageSize()));
                break;
            case Key.Home:
                Select(0);
                break;
            case Key.End:
                Select(count - 1);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    // The number of rows one PageUp/PageDown moves: what fits in the viewport, minus one for context.
    private int PageSize()
    {
        double rowHeight = CommitList.ContainerFromIndex(0)?.Bounds.Height ?? 26;
        return Math.Max(1, (int)(CommitList.Bounds.Height / Math.Max(1, rowHeight)) - 1);
    }

    private void Select(int index)
    {
        CommitList.SelectedIndex = index;
        CommitList.ScrollIntoView(index);
        (CommitList.ContainerFromIndex(index) as Control)?.Focus();
    }

    private void AttachGrip(Border grip)
    {
        grip.PointerPressed -= OnGripPressed;
        grip.PointerMoved -= OnGripMoved;
        grip.PointerReleased -= OnGripReleased;
        grip.PointerPressed += OnGripPressed;
        grip.PointerMoved += OnGripMoved;
        grip.PointerReleased += OnGripReleased;
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { Tag: string column } grip && e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
        {
            _resizing = column;
            _resizeStart = e.GetPosition(HeaderRow).X;
            e.Pointer.Capture(grip);
            e.Handled = true;
        }
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_resizing is null || Columns is not { } columns)
        {
            return;
        }

        double x = e.GetPosition(HeaderRow).X;
        columns.Resize(_resizing, x - _resizeStart);
        _resizeStart = x;
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_resizing is null)
        {
            return;
        }

        _resizing = null;
        e.Pointer.Capture(null);
        if (Columns is { } columns)
        {
            GridColumnStore.Current.Save(columns.ToSettings());
        }
    }
}
