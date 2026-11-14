using Avalonia.Controls;
using Avalonia.Layout;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

// View > "Tabs beside the revision grid": arranges the revision grid and the tabs in two columns instead of two rows.
public sealed partial class MainWindow
{
    private ViewOptionsViewModel? _viewOptions;

    private void InitializeViewOptions() => DataContextChanged += (_, _) =>
    {
        if (_viewOptions is not null)
        {
            _viewOptions.PropertyChanged -= OnViewOptionChanged;
        }

        _viewOptions = (DataContext as MainWindowViewModel)?.ViewOptions;
        if (_viewOptions is not null)
        {
            _viewOptions.PropertyChanged += OnViewOptionChanged;
        }

        ApplySplitLayout(_viewOptions?.SideBySide ?? false);
    };

    private void OnViewOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewOptionsViewModel.SideBySide) && _viewOptions is not null)
        {
            ApplySplitLayout(_viewOptions.SideBySide);
        }
    }

    private void ApplySplitLayout(bool sideBySide)
    {
        if (sideBySide)
        {
            BrowseRows.ColumnDefinitions = new ColumnDefinitions("*,Auto,*");
            Grid.SetRow(RevisionGrid, 0);
            Grid.SetRowSpan(RevisionGrid, 3);
            Grid.SetColumn(RevisionGrid, 0);
            Grid.SetRow(TabsSplitter, 0);
            Grid.SetRowSpan(TabsSplitter, 3);
            Grid.SetColumn(TabsSplitter, 1);
            TabsSplitter.Height = double.NaN;
            TabsSplitter.Width = 5;
            TabsSplitter.HorizontalAlignment = HorizontalAlignment.Center;
            TabsSplitter.VerticalAlignment = VerticalAlignment.Stretch;
            TabsSplitter.ResizeDirection = GridResizeDirection.Columns;
            Grid.SetRow(RevisionTabs, 0);
            Grid.SetRowSpan(RevisionTabs, 3);
            Grid.SetColumn(RevisionTabs, 2);
            RevisionTabs.MinWidth = 200;
        }
        else
        {
            BrowseRows.ColumnDefinitions = [];
            Grid.SetRow(RevisionGrid, 0);
            Grid.SetRowSpan(RevisionGrid, 1);
            Grid.SetColumn(RevisionGrid, 0);
            Grid.SetRow(TabsSplitter, 1);
            Grid.SetRowSpan(TabsSplitter, 1);
            Grid.SetColumn(TabsSplitter, 0);
            TabsSplitter.Width = double.NaN;
            TabsSplitter.Height = 5;
            TabsSplitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            TabsSplitter.VerticalAlignment = VerticalAlignment.Center;
            TabsSplitter.ResizeDirection = GridResizeDirection.Rows;
            Grid.SetRow(RevisionTabs, 2);
            Grid.SetRowSpan(RevisionTabs, 1);
            Grid.SetColumn(RevisionTabs, 0);
            RevisionTabs.MinWidth = 0;
        }
    }
}
