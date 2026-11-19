using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class LayoutTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private string _directory = null!;

    [SetUp]
    public void CreateDirectory() => _directory = Directory.CreateTempSubdirectory("ge-layout-").FullName;

    [TearDown]
    public void DeleteDirectory() => Directory.Delete(_directory, recursive: true);

    [AvaloniaTest]
    public void Store_round_trips()
    {
        LayoutStore store = new(Path.Combine(_directory, "sub", "layout.json"));
        LayoutSettings settings = new(SidebarWidth: 260, GridHeight: 300, TabsHeight: 200, SidebarCollapsed: true);
        store.Save(settings);
        store.Load().Should().Be(settings);
    }

    [AvaloniaTest]
    public void Store_returns_defaults_for_missing_or_corrupt_files()
    {
        LayoutStore store = new(Path.Combine(_directory, "layout.json"));
        store.Load().Should().Be(new LayoutSettings());

        File.WriteAllText(store.FilePath, "{ not json");
        store.Load().Should().Be(new LayoutSettings());

        File.WriteAllText(store.FilePath, "null");
        store.Load().Should().Be(new LayoutSettings());

        File.WriteAllText(store.FilePath, "{\"SidebarWidth\": \"wide\"}");
        store.Load().Should().Be(new LayoutSettings());
    }

    [AvaloniaTest]
    public void Store_ignores_the_fields_of_the_old_layout_file()
    {
        LayoutStore store = new(Path.Combine(_directory, "layout.json"));
        File.WriteAllText(store.FilePath, "{\"SidebarWidth\": 260, \"MiddleWidth\": 600, \"RightWidth\": 400, \"ChangesHeight\": 90, \"SidebarCollapsed\": true}");
        LayoutSettings loaded = store.Load();
        loaded.SidebarWidth.Should().Be(260);
        loaded.SidebarCollapsed.Should().BeTrue();
        loaded.GridHeight.Should().Be(new LayoutSettings().GridHeight);
    }

    [AvaloniaTest]
    public void Store_clamps_absurd_values_and_fills_missing_ones()
    {
        LayoutStore store = new(Path.Combine(_directory, "layout.json"));
        File.WriteAllText(store.FilePath, "{\"SidebarWidth\": 99999, \"GridHeight\": -5, \"TabsHeight\": 1e300}");
        LayoutSettings loaded = store.Load();
        loaded.SidebarWidth.Should().Be(LayoutSettings.MaxSidebarWidth);
        loaded.GridHeight.Should().Be(LayoutSettings.MinWeight);
        loaded.TabsHeight.Should().Be(LayoutSettings.MaxWeight);

        new LayoutSettings(SidebarWidth: double.NaN, GridHeight: double.PositiveInfinity, TabsHeight: double.NaN).Clamp()
            .Should().Be(new LayoutSettings());
    }

    [AvaloniaTest]
    public void Layout_is_restored_from_the_store_and_saved_back()
    {
        LayoutStore store = new(Path.Combine(_directory, "layout.json"));
        store.Save(new LayoutSettings(SidebarWidth: 300, GridHeight: 400, TabsHeight: 600, SidebarCollapsed: false));

        MainWindow window = new() { DataContext = new MainWindowViewModel(GitEnvironment.Initialize(), new NoFolderPicker()) };
        window.UseLayoutStore(store);
        Columns(window).ColumnDefinitions[0].Width.Value.Should().Be(300);
        window.Find<Grid>("BrowseRows").RowDefinitions[0].Height.Value.Should().Be(400);
        window.Find<Grid>("BrowseRows").RowDefinitions[2].Height.Value.Should().Be(600);

        window.SidebarCollapsed = true;
        store.Load().SidebarCollapsed.Should().BeTrue();
        window.Close();
    }

    [AvaloniaTest]
    public void Corrupt_layout_cannot_make_the_window_unusable()
    {
        MainWindow window = new() { DataContext = new MainWindowViewModel(GitEnvironment.Initialize(), new NoFolderPicker()) };
        window.ApplyLayout(new LayoutSettings(SidebarWidth: 1e9, GridHeight: double.NaN, TabsHeight: -1));
        Columns(window).ColumnDefinitions[0].Width.Value.Should().BeLessThanOrEqualTo(LayoutSettings.MaxSidebarWidth);
        window.Find<Grid>("BrowseRows").RowDefinitions[0].Height.Value.Should().BeGreaterThan(0);
        window.Find<Grid>("BrowseRows").RowDefinitions[2].Height.Value.Should().BeGreaterThan(0);
        window.Close();
    }

    [AvaloniaTest]
    public async Task A_tiny_tabs_weight_cannot_push_the_tabs_onto_the_history()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel, Width = 1200, Height = 720 };
        window.Show();
        await viewModel.OpenAsync(repository.Path);

        // A layout stored after the splitter was dragged almost to the bottom.
        window.ApplyLayout(new LayoutSettings(GridHeight: 1040, TabsHeight: 60));
        ShellTestSupport.Settle();

        Control grid = window.Find<Control>("RevisionGrid");
        Control tabs = window.Find<Control>("RevisionTabs");
        tabs.Bounds.Height.Should().BeGreaterThanOrEqualTo(120, "the tabs keep room for their headers and content");
        tabs.Bounds.Top.Should().BeGreaterThanOrEqualTo(grid.Bounds.Bottom, "the tabs do not overlap the history");
        window.Close();
    }

    [AvaloniaTest]
    public async Task Sidebar_collapse_toggle_hides_and_restores_the_sidebar()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.OpenAsync(repository.Path);
        Settle();

        Control sidebar = window.Find<RefsTreeView>("SidebarPanel");
        // The toolbar button and Ctrl+B use this command; it asks the window to toggle the sidebar.
        System.Windows.Input.ICommand toggle = viewModel.ToggleLeftPanelCommand;
        sidebar.Bounds.Width.Should().BeGreaterThan(100);

        toggle.Execute(null);
        Settle();
        window.SidebarCollapsed.Should().BeTrue();
        sidebar.IsEffectivelyVisible.Should().BeFalse("the whole sidebar (with its Changes | History tabs) is hidden");
        Columns(window).ColumnDefinitions[0].ActualWidth.Should().Be(0);

        toggle.Execute(null);
        Settle();
        window.SidebarCollapsed.Should().BeFalse();
        sidebar.Bounds.Width.Should().BeGreaterThan(100);
        window.Close();
    }

    [AvaloniaTest]
    public async Task Default_layout_gives_the_grid_tabs_and_left_panel_enough_room()
    {
        foreach ((int width, int height) in new[] { (1200, 720), (900, 600) })
        {
            (MainWindow window, TestRepository repository) = await OpenAsync(width, height, ThemeVariant.Light);
            string size = $"{width}x{height}";
            window.Find<RevisionGridView>("RevisionGrid").Bounds.Height.Should().BeGreaterThanOrEqualTo(150, size);
            window.Find<RevisionTabsView>("RevisionTabs").Bounds.Height.Should().BeGreaterThanOrEqualTo(150, size);
            window.Find<RefsTreeView>("SidebarPanel").Bounds.Width.Should().BeGreaterThanOrEqualTo(150, size);
            window.Find<RevisionGridView>("RevisionGrid").Bounds.Width.Should().BeGreaterThanOrEqualTo(400, size);
            window.Find<RevisionTabsView>("RevisionTabs").Bounds.Width.Should().Be(window.Find<RevisionGridView>("RevisionGrid").Bounds.Width, size);
            window.Close();
            repository.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task Dragging_the_splitters_is_saved_as_the_layout()
    {
        LayoutStore store = new(Path.Combine(_directory, "layout.json"));
        (MainWindow window, TestRepository repository) = await OpenAsync(1200, 720, ThemeVariant.Light);
        window.UseLayoutStore(store);
        Settle();

        window.CaptureLayout().SidebarWidth.Should().BeApproximately(window.Find<RefsTreeView>("SidebarPanel").Bounds.Width, 1);
        LayoutSettings captured = window.CaptureLayout();
        captured.GridHeight.Should().BeApproximately(window.Find<RevisionGridView>("RevisionGrid").Bounds.Height, 1);
        captured.TabsHeight.Should().BeApproximately(window.Find<RevisionTabsView>("RevisionTabs").Bounds.Height, 1);
        window.Close();
        store.Load().SidebarWidth.Should().BeApproximately(captured.SidebarWidth, 1);
        repository.Dispose();
    }

    [AvaloniaTest]
    public async Task Layout_screenshots()
    {
        foreach ((int width, int height) in new[] { (1200, 720), (900, 600) })
        {
            foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                (MainWindow window, TestRepository repository) = await OpenAsync(width, height, variant);
                WriteableBitmap? frame = window.CaptureRenderedFrame();
                frame.Should().NotBeNull();
                string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
                Directory.CreateDirectory(directory);
                string file = Path.Combine(directory, $"layout-{width}x{height}-{variant.Key.ToString()!.ToLowerInvariant()}.png");
                frame!.Save(file);
                TestContext.AddTestAttachment(file);
                window.Close();
                repository.Dispose();
            }
        }
    }

    private static Grid Columns(MainWindow window) => window.FindControl<Grid>("MainColumns")!;

    private static void Settle()
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static async Task<(MainWindow Window, TestRepository Repository)> OpenAsync(int width, int height, ThemeVariant variant)
    {
        TestRepository repository = new();
        repository.AddRichHistory();
        File.WriteAllText(System.IO.Path.Combine(repository.Path, "a.txt"), "changed\nsecond line\nthird\n");
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel, Width = width, Height = height, RequestedThemeVariant = variant };
        window.Show();
        await viewModel.OpenAsync(repository.Path);

        viewModel.SelectedCommit = viewModel.Commits[0];
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!viewModel.CommitDetails.HasCommit && DateTime.UtcNow < deadline)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }

        Settle();
        return (window, repository);
    }
}
