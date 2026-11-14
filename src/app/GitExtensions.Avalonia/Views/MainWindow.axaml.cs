using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachRecentFlyout();

        ColumnDefinitions columns = MainColumns.ColumnDefinitions;
        columns[2].MinWidth = MinMiddleWidth;
        columns[4].MinWidth = MinRightWidth;
        this.FindControl<Button>("SidebarToggle")!.Click += (_, _) => SidebarCollapsed = !SidebarCollapsed;
        this.FindControl<GridSplitter>("SidebarSplitter")!.DoubleTapped += (_, _) => SidebarCollapsed = true;
        AddHandler(Thumb.DragCompletedEvent, (_, _) => SaveLayout());
        _layout = new LayoutSettings();
        ApplyLayout(_layout);
    }

    private const double MinMiddleWidth = 320;
    private const double MinRightWidth = 300;

    private LayoutStore? _layoutStore;
    private LayoutSettings _layout;
    private double _expandedSidebarWidth = LayoutSettings.DefaultSidebarWidth;

    /// <summary>
    ///  Whether the branches sidebar is hidden. Setting it also hides its splitter and updates the toggle button.
    /// </summary>
    public bool SidebarCollapsed
    {
        get => _layout.SidebarCollapsed;
        set
        {
            if (value && !_layout.SidebarCollapsed)
            {
                _expandedSidebarWidth = Math.Max(LayoutSettings.MinSidebarWidth, MainColumns.ColumnDefinitions[0].ActualWidth);
            }

            _layout = _layout with { SidebarCollapsed = value };
            ShowSidebar(!value, _expandedSidebarWidth);
            SaveLayout();
        }
    }

    /// <summary>
    ///  Restores the stored layout and keeps saving it (after a splitter drag, a sidebar toggle and on closing).
    /// </summary>
    public void UseLayoutStore(LayoutStore store)
    {
        _layoutStore = store;
        ApplyLayout(store.Load());
        Closing += (_, _) => SaveLayout();
    }

    /// <summary>
    ///  Applies <paramref name="settings"/> (clamped) to the splitter-controlled columns and rows.
    /// </summary>
    public void ApplyLayout(LayoutSettings settings)
    {
        _layout = settings.Clamp();
        ColumnDefinitions columns = MainColumns.ColumnDefinitions;
        columns[2].Width = new GridLength(_layout.MiddleWidth, GridUnitType.Star);
        columns[4].Width = new GridLength(_layout.RightWidth, GridUnitType.Star);
        SetRows(MiddleRows, _layout.HistoryHeight, null, _layout.DetailsHeight);
        SetRows(RightRows, _layout.ChangesHeight, _layout.DiffHeight, _layout.CommitHeight);

        // Keep the sidebar from squeezing the other two columns off a small window.
        double sidebar = _layout.SidebarWidth;
        if (Width > 0)
        {
            sidebar = Math.Min(sidebar, Math.Max(LayoutSettings.MinSidebarWidth, Width * 0.3));
        }

        _expandedSidebarWidth = sidebar;
        ShowSidebar(!_layout.SidebarCollapsed, sidebar);
    }

    /// <summary>
    ///  Reads the current sizes from the live layout (keeping the stored values for anything not laid out yet).
    /// </summary>
    public LayoutSettings CaptureLayout()
    {
        LayoutSettings captured = _layout;
        ColumnDefinitions columns = MainColumns.ColumnDefinitions;
        if (columns[2].ActualWidth > 0 && columns[4].ActualWidth > 0)
        {
            captured = captured with
            {
                SidebarWidth = _layout.SidebarCollapsed ? _expandedSidebarWidth : columns[0].ActualWidth,
                MiddleWidth = columns[2].ActualWidth,
                RightWidth = columns[4].ActualWidth,
            };
        }

        RowDefinitions middle = MiddleRows.RowDefinitions;
        if (middle[0].ActualHeight > 0 && middle[2].ActualHeight > 0)
        {
            captured = captured with { HistoryHeight = middle[0].ActualHeight, DetailsHeight = middle[2].ActualHeight };
        }

        RowDefinitions right = RightRows.RowDefinitions;
        if (right[0].ActualHeight > 0 && right[2].ActualHeight > 0 && right[4].ActualHeight > 0)
        {
            captured = captured with
            {
                ChangesHeight = right[0].ActualHeight,
                DiffHeight = right[2].ActualHeight,
                CommitHeight = right[4].ActualHeight,
            };
        }

        return captured.Clamp();
    }

    private static void SetRows(Grid grid, double first, double? second, double third)
    {
        RowDefinitions rows = grid.RowDefinitions;
        rows[0].Height = new GridLength(first, GridUnitType.Star);
        if (second is double middle)
        {
            rows[2].Height = new GridLength(middle, GridUnitType.Star);
            rows[4].Height = new GridLength(third, GridUnitType.Star);
        }
        else
        {
            rows[2].Height = new GridLength(third, GridUnitType.Star);
        }
    }

    private void ShowSidebar(bool visible, double width)
    {
        ColumnDefinition column = MainColumns.ColumnDefinitions[0];
        column.MinWidth = visible ? LayoutSettings.MinSidebarWidth : 0;
        column.MaxWidth = visible ? double.PositiveInfinity : 0;
        column.Width = new GridLength(visible ? width : 0);
        this.FindControl<Control>("SidebarPanel")!.IsVisible = visible;
        this.FindControl<Control>("SidebarSplitter")!.IsVisible = visible;
        this.FindControl<Button>("SidebarToggle")!.Content = visible ? "«" : "»";
    }

    private void SaveLayout()
    {
        _layout = CaptureLayout();
        _layoutStore?.Save(_layout);
    }

    // The flyout is not part of the window's tree, so give it its own view and view-model explicitly.
    private void AttachRecentFlyout()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Flyout flyout = new()
        {
            Placement = Avalonia.Controls.PlacementMode.BottomEdgeAlignedRight,
            Content = new RecentRepositoriesView { DataContext = viewModel.Recent, MaxHeight = 420 },
        };
        viewModel.Recent.Chosen += flyout.Hide;
        this.FindControl<Button>("RecentButton")!.Flyout = flyout;
    }
}
