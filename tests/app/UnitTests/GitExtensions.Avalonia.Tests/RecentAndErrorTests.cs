using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using GitCommands.UserRepositoryHistory;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;
using GitExtensions.Extensibility;
using GitUI;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class RecentAndErrorTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static MainWindowViewModel CreateViewModel() => new(GitEnvironment.Initialize(), new NoFolderPicker());

    private static void Render(Window window)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static void Screenshot(Window window, string name)
    {
        Render(window);
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, name);
        window.CaptureRenderedFrame()!.Save(file);
        TestContext.AddTestAttachment(file);
        TestContext.Out.WriteLine(file);
    }

    [AvaloniaTest]
    public void Config_is_isolated_from_the_real_user_profile()
    {
        IsolatedConfig.Directory.Should().NotBeNull();
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).Should().StartWith(IsolatedConfig.Directory);
    }

    [AvaloniaTest]
    public async Task Opening_a_repository_records_it_as_most_recent_first()
    {
        using TestRepository first = new();
        using TestRepository second = new();
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.OpenAsync(first.Path);
        await viewModel.OpenAsync(second.Path);
        await viewModel.OpenAsync(first.Path);
        await viewModel.Recent.LoadAsync();

        string[] paths = viewModel.Recent.Items.Select(i => Path.TrimEndingDirectorySeparator(i.Path)).ToArray();
        paths.Take(2).Should().Equal(first.Path, second.Path);

        IList<Repository> stored = await RepositoryHistoryManager.Locals.LoadRecentHistoryAsync();
        Path.TrimEndingDirectorySeparator(stored[0].Path).Should().Be(first.Path);
    }

    [AvaloniaTest]
    public async Task Failed_open_is_not_recorded()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        string folder = Directory.CreateTempSubdirectory("ge-avalonia-notrepo-").FullName;
        try
        {
            await viewModel.OpenAsync(folder);
            await viewModel.Recent.LoadAsync();
            viewModel.Recent.Items.Should().NotContain(i => i.Path.Contains("ge-avalonia-notrepo-"));
            viewModel.ShowStartView.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [AvaloniaTest]
    public async Task Recent_item_command_opens_the_repository()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.Recent.OpenCommand.ExecuteAsync(new RecentRepositoryItem(repository.Path));

        viewModel.Branch.Should().Be("main");
        viewModel.ShowStartView.Should().BeFalse();
    }

    [AvaloniaTest]
    public void ShowError_appears_in_the_banner_and_can_be_dismissed()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.HasError.Should().BeFalse();

        MessageDialogs.Instance.ShowError(null, "Something broke", "Oops");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        viewModel.HasError.Should().BeTrue();
        viewModel.ErrorMessage.Should().Be("Oops: Something broke");

        viewModel.DismissErrorCommand.Execute(null);
        viewModel.HasError.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Exception_reporter_from_a_background_thread_appears_in_the_banner()
    {
        MainWindowViewModel viewModel = CreateViewModel();

        await Task.Run(() => TaskManager.ExceptionReporter!(new InvalidOperationException("background failure")));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        viewModel.ErrorMessage.Should().Be("background failure");
    }

    [AvaloniaTest]
    public void WindowStateStore_round_trips()
    {
        string file = Path.Combine(IsolatedConfig.Directory!, "store", "window.json");
        WindowStateStore store = new(file);
        store.Load().Should().BeNull();

        WindowPlacement placement = new(900.5, 600, 120, -30, IsMaximized: true);
        store.Save(placement);

        new WindowStateStore(file).Load().Should().Be(placement);
    }

    [AvaloniaTest]
    public void WindowStateStore_ignores_a_corrupt_file()
    {
        string file = Path.Combine(IsolatedConfig.Directory!, "corrupt.json");
        File.WriteAllText(file, "{ nope");
        new WindowStateStore(file).Load().Should().BeNull();
    }

    [AvaloniaTest]
    public void WindowStateStore_defaults_to_the_app_data_folder()
        => new WindowStateStore().FilePath.Should().StartWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    [AvaloniaTest]
    public async Task Dashboard_renders_the_recent_repositories()
    {
        string root = Directory.CreateTempSubdirectory("ge-avalonia-shots-").FullName;
        try
        {
            string[] names = ["gitextensions", "avalonia-playground", "dotfiles", "website"];
            List<Repository> history = [];
            foreach (string name in names)
            {
                string dir = Path.Combine(root, name);
                Directory.CreateDirectory(dir);
                history.Add(new Repository(dir + Path.DirectorySeparatorChar));
            }

            // Replace whatever earlier tests recorded so the screenshot is deterministic.
            await RepositoryHistoryManager.Locals.SaveRecentHistoryAsync(history);
            await RepositoryHistoryManager.Locals.SaveFavouriteHistoryAsync([]);

            MainWindowViewModel viewModel = CreateViewModel();
            MainWindow window = new() { DataContext = viewModel };
            window.Show();
            await viewModel.Recent.LoadAsync();
            await viewModel.Dashboard.LoadAsync();
            Render(window);

            window.FindControl<DashboardView>("DashboardPage")!.IsVisible.Should().BeTrue();
            window.Find<RevisionGridView>("RevisionGrid").IsEffectivelyVisible.Should().BeFalse();
            viewModel.Recent.Items.Count.Should().Be(4);
            viewModel.Dashboard.Categories.Single().Entries.Count.Should().Be(4);
            Screenshot(window, "start-view.png");
            window.Close();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaTest]
    public async Task Dashboard_renders_the_empty_state_and_error_banner()
    {
        await RepositoryHistoryManager.Locals.SaveRecentHistoryAsync([]);
        await RepositoryHistoryManager.Locals.SaveFavouriteHistoryAsync([]);
        MainWindowViewModel viewModel = CreateViewModel();
        await viewModel.Recent.LoadAsync();
        await viewModel.Dashboard.LoadAsync();
        viewModel.Recent.IsEmpty.Should().BeTrue();
        viewModel.Dashboard.IsEmpty.Should().BeTrue();
        MainWindow window = new() { DataContext = viewModel };
        window.Show();

        MessageDialogs.Instance.ShowError(null, "fatal: unable to access the remote repository.", "Git error");
        Render(window);

        window.FindControl<Border>("ErrorBanner")!.IsVisible.Should().BeTrue();
        window.FindControl<TextBlock>("ErrorText")!.Text.Should().Contain("unable to access");
        Screenshot(window, "error-banner.png");
        window.Close();
    }
}
