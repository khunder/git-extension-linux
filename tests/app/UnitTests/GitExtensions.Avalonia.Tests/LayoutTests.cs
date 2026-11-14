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

    [Test]
    public void Store_round_trips()
    {
        LayoutStore store = new(Path.Combine(_directory, "sub", "layout.json"));
        LayoutSettings settings = new(SidebarWidth: 260, MiddleWidth: 600, RightWidth: 400, HistoryHeight: 300,
            DetailsHeight: 200, ChangesHeight: 100, DiffHeight: 350, CommitHeight: 140, SidebarCollapsed: true);
        store.Save(settings);
        store.Load().Should().Be(settings);
    }

    [Test]
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

    [Test]
    public void Store_clamps_absurd_values_and_fills_missing_ones()
    {
        LayoutStore store = new(Path.Combine(_directory, "layout.json"));
        File.WriteAllText(store.FilePath, "{\"SidebarWidth\": 99999, \"MiddleWidth\": -5, \"DiffHeight\": 1e300}");
        LayoutSettings loaded = store.Load();
        loaded.SidebarWidth.Should().Be(LayoutSettings.MaxSidebarWidth);
        loaded.MiddleWidth.Should().Be(LayoutSettings.MinWeight);
        loaded.DiffHeight.Should().Be(LayoutSettings.MaxWeight);
        loaded.CommitHeight.Should().Be(new LayoutSettings().CommitHeight);

        new LayoutSettings(SidebarWidth: double.NaN, RightWidth: double.PositiveInfinity).Clamp()
            .Should().Be(new LayoutSettings());
    }

    [AvaloniaTest]
    public void Layout_is_restored_from_the_store_and_saved_back()
    {
        LayoutStore store = new(Path.Combine(_directory, "layout.json"));
        store.Save(new LayoutSettings(SidebarWidth: 300, MiddleWidth: 400, RightWidth: 600, SidebarCollapsed: false));

        MainWindow window = new() { DataContext = new MainWindowViewModel(GitEnvironment.Initialize(), new NoFolderPicker()) };
        window.UseLayoutStore(store);
        Columns(window).ColumnDefinitions[0].Width.Value.Should().Be(300);
        Columns(window).ColumnDefinitions[2].Width.Value.Should().Be(400);
        Columns(window).ColumnDefinitions[4].Width.Value.Should().Be(600);

        window.SidebarCollapsed = true;
        store.Load().SidebarCollapsed.Should().BeTrue();
        window.Close();
    }

    [AvaloniaTest]
    public void Corrupt_layout_cannot_make_the_window_unusable()
    {
        MainWindow window = new() { DataContext = new MainWindowViewModel(GitEnvironment.Initialize(), new NoFolderPicker()) };
        window.ApplyLayout(new LayoutSettings(SidebarWidth: 1e9, MiddleWidth: double.NaN, RightWidth: -1, DiffHeight: 0));
        Columns(window).ColumnDefinitions[0].Width.Value.Should().BeLessThanOrEqualTo(LayoutSettings.MaxSidebarWidth);
        Columns(window).ColumnDefinitions[2].Width.Value.Should().BeGreaterThan(0);
        Columns(window).ColumnDefinitions[4].Width.Value.Should().BeGreaterThan(0);
        window.FindControl<Grid>("RightRows")!.RowDefinitions[2].Height.Value.Should().BeGreaterThan(0);
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

        Control sidebar = window.FindControl<Control>("SidebarPanel")!;
        Button toggle = window.FindControl<Button>("SidebarToggle")!;
        sidebar.Bounds.Width.Should().BeGreaterThan(100);

        toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle();
        window.SidebarCollapsed.Should().BeTrue();
        sidebar.IsVisible.Should().BeFalse();
        Columns(window).ColumnDefinitions[0].ActualWidth.Should().Be(0);

        toggle.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle();
        window.SidebarCollapsed.Should().BeFalse();
        sidebar.Bounds.Width.Should().BeGreaterThan(100);
        window.Close();
    }

    [AvaloniaTest]
    public async Task Default_layout_gives_the_diff_and_history_enough_room_at_1200x720()
    {
        (MainWindow window, _) = await OpenAsync(1200, 720, ThemeVariant.Light);
        Control diff = window.FindControl<DiffView>("DiffPanel")!;
        ListBox history = window.FindControl<ListBox>("CommitList")!;
        diff.Bounds.Height.Should().BeGreaterThanOrEqualTo(250);
        history.Bounds.Height.Should().BeGreaterThanOrEqualTo(150);
        diff.Bounds.Width.Should().BeGreaterThanOrEqualTo(300);
        window.Close();
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

        ChangedFileViewModel file = viewModel.ChangedFiles.First();
        viewModel.SelectedChangedFile = file;
        await viewModel.Diff!.ShowAsync(file);
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
