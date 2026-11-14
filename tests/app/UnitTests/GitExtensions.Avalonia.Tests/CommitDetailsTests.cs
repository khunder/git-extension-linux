using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class CommitDetailsTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        condition().Should().BeTrue("the condition should become true in time");
    }

    [AvaloniaTest]
    public async Task Load_shows_metadata_message_and_status_letters()
    {
        using TestRepository repository = new();
        repository.AddRichHistory();
        GitModule module = GitEnvironment.Initialize().OpenRepository(repository.Path)!;
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(repository.Path);
        CommitItemViewModel commit = main.Commits[0];
        CommitDetailsViewModel details = new();

        details.IsEmpty.Should().BeTrue();
        await details.LoadAsync(module, commit.ObjectId, commit.Refs);

        details.IsLoading.Should().BeFalse();
        details.HasCommit.Should().BeTrue();
        details.Hash.Should().Be(commit.ObjectId.ToString()).And.HaveLength(40);
        details.Author.Should().Be("Test Author <test@example.com>");
        details.Parents.Should().Be(main.Commits[1].ShortHash);
        details.Refs.Should().Contain("main");
        details.Message.Should().StartWith("Rework files").And.Contain("Second body line.").And.EndWith("Final paragraph.");
        details.Files.Select(f => (f.Name, f.Status)).Should().BeEquivalentTo(
            [("a.txt", "M"), ("b.txt", "D"), ("e.txt", "R")]);
    }

    [AvaloniaTest]
    public async Task Load_handles_the_root_commit()
    {
        using TestRepository repository = new();
        GitModule module = GitEnvironment.Initialize().OpenRepository(repository.Path)!;
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(repository.Path);
        CommitItemViewModel root = main.Commits[^1];
        CommitDetailsViewModel details = new();

        await details.LoadAsync(module, root.ObjectId, root.Refs);

        details.Parents.Should().Be("(root commit)");
        details.Files.Select(f => (f.Name, f.Status)).Should().BeEquivalentTo([("a.txt", "A")]);
        details.Error.Should().BeNull();
    }

    [AvaloniaTest]
    public async Task Clear_returns_to_the_empty_state()
    {
        using TestRepository repository = new();
        GitModule module = GitEnvironment.Initialize().OpenRepository(repository.Path)!;
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(repository.Path);
        CommitDetailsViewModel details = new();
        await details.LoadAsync(module, main.Commits[0].ObjectId, string.Empty);

        await details.ClearAsync();

        details.IsEmpty.Should().BeTrue();
        details.HasCommit.Should().BeFalse();
        details.Files.Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task A_newer_load_supersedes_the_previous_one()
    {
        using TestRepository repository = new();
        GitModule module = GitEnvironment.Initialize().OpenRepository(repository.Path)!;
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(repository.Path);
        CommitDetailsViewModel details = new();

        Task first = details.LoadAsync(module, main.Commits[0].ObjectId, string.Empty);
        await details.LoadAsync(module, main.Commits[1].ObjectId, string.Empty);
        await first;

        details.Message.Should().Be("Add a.txt");
        details.Files.Select(f => f.Name).Should().Equal("a.txt");
    }

    [AvaloniaTest]
    public async Task Selecting_a_commit_in_the_main_window_loads_its_details()
    {
        using TestRepository repository = new();
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(repository.Path);

        main.CommitDetails.IsEmpty.Should().BeTrue();
        main.SelectedCommit = main.Commits[0];
        await WaitUntilAsync(() => main.CommitDetails.HasCommit);
        main.CommitDetails.Message.Should().Be("Add b.txt");

        main.SelectedCommit = null;
        await WaitUntilAsync(() => main.CommitDetails.IsEmpty);
    }

    [AvaloniaTest]
    public async Task MainWindow_renders_the_details_of_the_selected_commit()
    {
        using TestRepository repository = new();
        repository.AddRichHistory();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.OpenAsync(repository.Path);

        window.FindControl<ListBox>("CommitList")!.SelectedIndex = 0;
        await WaitUntilAsync(() => viewModel.CommitDetails.HasCommit);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        CommitDetailsView view = window.FindControl<CommitDetailsView>("DetailsView")!;
        view.FindControl<Panel>("DetailsPanel")!.IsVisible.Should().BeTrue();
        view.FindControl<TextBlock>("EmptyText")!.IsVisible.Should().BeFalse();
        view.FindControl<ListBox>("FileList")!.ItemCount.Should().Be(3);
        view.FindControl<SelectableTextBlock>("MessageText")!.Text.Should().Contain("Final paragraph.");

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "main-window-details.png");
        frame!.Save(file);
        TestContext.AddTestAttachment(file);
        window.Close();
    }
}
