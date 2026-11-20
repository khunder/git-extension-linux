using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The main menu bar: Start, Dashboard, Repository, Navigate, View, Commands, Plugins, Tools and Help.
/// </summary>
public sealed partial class MainMenuView : UserControl
{
    private MainWindowViewModel? _viewModel;

    public MainMenuView()
    {
        InitializeComponent();
        Avalonia.Automation.AutomationProperties.SetName(MainMenu, "Main menu");
        DataContextChanged += (_, _) => Attach(DataContext as MainWindowViewModel);
        DetachedFromVisualTree += (_, _) => ThemeService.Current.ChoiceChanged -= SyncTheme;
        AttachedToVisualTree += (_, _) =>
        {
            ThemeService.Current.ChoiceChanged -= SyncTheme;
            ThemeService.Current.ChoiceChanged += SyncTheme;
            SyncTheme();
        };
        SyncTheme();
    }

    /// <summary>
    ///  The menu bar itself, for tests.
    /// </summary>
    public Menu Bar => MainMenu;

    private void Attach(MainWindowViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.Recent.Items.CollectionChanged -= OnRecentChanged;
            _viewModel.AboutRequested -= OnAbout;
        }

        _viewModel = viewModel;
        if (viewModel is not null)
        {
            viewModel.Recent.Items.CollectionChanged += OnRecentChanged;
            viewModel.AboutRequested += OnAbout;
        }

        RebuildRecent();
    }

    private void OnAbout()
    {
        AboutWindow about = new();
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            _ = about.ShowDialog(owner);
        }
        else
        {
            about.Show();
        }
    }

    private void OnRecentChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildRecent();

    private void RebuildRecent()
    {
        OpenRecentMenu.Items.Clear();
        if (_viewModel is null || _viewModel.Recent.Items.Count == 0)
        {
            OpenRecentMenu.Items.Add(new MenuItem { Header = "(no recent repositories)", IsEnabled = false });
            return;
        }

        foreach (RecentRepositoryItem item in _viewModel.Recent.Items.Take(20))
        {
            MenuItem entry = new() { Header = item.Path.Replace("_", "__", StringComparison.Ordinal), Tag = item };
            entry.Click += (_, _) => _viewModel.Recent.OpenCommand.Execute(item);
            OpenRecentMenu.Items.Add(entry);
        }
    }

    private void OnExit(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Window)?.Close();

    private void OnThemeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string name } && Enum.TryParse(name, out ThemeChoice choice))
        {
            ThemeService.Current.Set(choice);
        }
    }

    private void SyncTheme()
    {
        string current = ThemeService.Current.Choice.ToString();
        foreach (MenuItem item in ThemeMenu.Items.OfType<MenuItem>())
        {
            item.IsChecked = item.Tag is string tag && tag == current;
        }
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => EditorZoom.Change(+1);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => EditorZoom.Change(-1);

    private void OnZoomReset(object? sender, RoutedEventArgs e) => EditorZoom.Reset();
}
