using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class StashTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class Harness
    {
        public required StashViewModel ViewModel { get; init; }

        public List<string>? ErrorsSource { get; set; }

        public int Refreshes { get; set; }
    }

    private static async Task<Harness> CreateAsync(TestRepository repository)
    {
        repository.ConfigureIdentity();
        GitModule module = GitEnvironment.Initialize().OpenRepository(repository.Path)!;
        List<string> errors = [];
        Harness[] holder = new Harness[1];
        StashViewModel viewModel = new(() => module, () =>
        {
            holder[0].Refreshes++;
            return Task.CompletedTask;
        }, errors.Add);
        Harness harness = new() { ViewModel = viewModel };
        holder[0] = harness;
        harness.ErrorsSource = errors;
        await viewModel.LoadAsync();
        return harness;
    }

    private static string StashCount(TestRepository repository) => repository.GitOutput("stash", "list").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ToString();

    [AvaloniaTest]
    public async Task Stash_changes_creates_a_stash_and_cleans_the_tracked_changes()
    {
        using TestRepository repository = new();
        Harness harness = await CreateAsync(repository);
        harness.ViewModel.Items.Should().BeEmpty();

        harness.ViewModel.StashMessage = "my work";
        (await harness.ViewModel.ConfirmStashAsync()).Should().BeTrue();

        File.ReadAllText(Path.Combine(repository.Path, "a.txt")).Should().Be("first");
        File.Exists(Path.Combine(repository.Path, "c.txt")).Should().BeTrue("untracked files stay without --include-untracked");
        harness.ViewModel.Items.Should().ContainSingle();
        harness.ViewModel.Items[0].Message.Should().Be("my work");
        harness.ViewModel.Items[0].Branch.Should().Be("main");
        harness.Refreshes.Should().Be(1);
        harness.ViewModel.ButtonText.Should().Be("Stash (1) ▾");
    }

    [AvaloniaTest]
    public async Task Include_untracked_stashes_new_files_too()
    {
        using TestRepository repository = new();
        Harness harness = await CreateAsync(repository);

        harness.ViewModel.IncludeUntracked = true;
        (await harness.ViewModel.ConfirmStashAsync()).Should().BeTrue();

        File.Exists(Path.Combine(repository.Path, "c.txt")).Should().BeFalse();
        harness.ViewModel.Items.Should().ContainSingle();
        harness.ViewModel.Items[0].Message.Should().EndWith("Add b.txt", "no message was given, git describes the base commit");

        harness.ViewModel.SelectedStash = harness.ViewModel.Items[0];
        await WaitAsync(() => harness.ViewModel.Files.Count == 2);
        harness.ViewModel.Files.Select(f => f.Name).Should().BeEquivalentTo(["a.txt", "c.txt"]);

        harness.ViewModel.SelectedFile = harness.ViewModel.Files.Single(f => f.Name == "c.txt");
        await WaitAsync(() => harness.ViewModel.DiffLines.Count > 0);
        harness.ViewModel.DiffLines.Select(l => l.Text).Should().Contain("+new");
    }

    [AvaloniaTest]
    public async Task Nothing_to_stash_is_reported()
    {
        using TestRepository repository = new();
        repository.RunGit("checkout", "--", "a.txt");
        File.Delete(Path.Combine(repository.Path, "c.txt"));
        Harness harness = await CreateAsync(repository);

        (await harness.ViewModel.ConfirmStashAsync()).Should().BeFalse();

        harness.ErrorsSource!.Should().ContainSingle().Which.Should().Contain("no local changes");
        harness.ViewModel.Items.Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Apply_keeps_the_stash_and_pop_removes_it()
    {
        using TestRepository repository = new();
        repository.AddStash("keep me", content: "stashed content");
        Harness harness = await CreateAsync(repository);
        harness.ViewModel.Items.Should().ContainSingle();

        (await harness.ViewModel.ApplyAsync(harness.ViewModel.Items[0])).Should().BeTrue();
        File.ReadAllText(Path.Combine(repository.Path, "a.txt")).Should().Be("stashed content");
        StashCount(repository).Should().Be("1");
        harness.ViewModel.Items.Should().ContainSingle();

        repository.RunGit("checkout", "--", "a.txt");
        (await harness.ViewModel.PopAsync(harness.ViewModel.Items[0])).Should().BeTrue();
        File.ReadAllText(Path.Combine(repository.Path, "a.txt")).Should().Be("stashed content");
        StashCount(repository).Should().Be("0");
        harness.ViewModel.Items.Should().BeEmpty();
        harness.ErrorsSource!.Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Drop_requires_confirmation()
    {
        using TestRepository repository = new();
        repository.AddSeveralStashes();
        Harness harness = await CreateAsync(repository);
        harness.ViewModel.Items.Should().HaveCount(3);

        // Requesting a drop (the button) only opens the prompt.
        harness.ViewModel.SelectedStash = harness.ViewModel.Items[1];
        harness.ViewModel.RequestDropCommand.Execute(null);
        harness.ViewModel.IsDropPending.Should().BeTrue();
        harness.ViewModel.DropPrompt.Should().Contain("stash@{1}").And.Contain("second");
        StashCount(repository).Should().Be("3");

        // So does the API without the confirmed flag.
        (await harness.ViewModel.DropAsync(harness.ViewModel.Items[0])).Should().BeFalse();
        StashCount(repository).Should().Be("3");

        // Cancel keeps everything.
        harness.ViewModel.CancelDropCommand.Execute(null);
        harness.ViewModel.IsDropPending.Should().BeFalse();
        StashCount(repository).Should().Be("3");

        // Confirm drops exactly the requested one.
        harness.ViewModel.RequestDropCommand.Execute(null);
        await harness.ViewModel.ConfirmDropCommand.ExecuteAsync(null);
        harness.ViewModel.IsDropPending.Should().BeFalse();
        StashCount(repository).Should().Be("2");
        harness.ViewModel.Items.Select(i => i.Message).Should().Equal("third on b", "first");
    }

    [AvaloniaTest]
    public async Task Conflict_on_apply_is_reported_and_the_stash_is_kept()
    {
        using TestRepository repository = new();
        repository.AddStash("conflicting", content: "stash version");
        File.WriteAllText(Path.Combine(repository.Path, "a.txt"), "other version");
        repository.RunGit("commit", "-a", "-m", "Other change");
        Harness harness = await CreateAsync(repository);

        bool applied = await harness.ViewModel.ApplyAsync(harness.ViewModel.Items[0]).WaitAsync(TimeSpan.FromSeconds(30));

        applied.Should().BeFalse();
        harness.ErrorsSource!.Should().ContainSingle().Which.Should().Contain("stash@{0} was kept");
        StashCount(repository).Should().Be("1");
        harness.ViewModel.Items.Should().ContainSingle();

        // Pop with the same conflict must not drop the stash either.
        (await harness.ViewModel.PopAsync(harness.ViewModel.Items[0])).Should().BeFalse();
        StashCount(repository).Should().Be("1");
    }

    [AvaloniaTest]
    public async Task List_is_newest_first_with_message_branch_and_date()
    {
        using TestRepository repository = new();
        repository.RunGit("checkout", "-b", "topic");
        repository.AddSeveralStashes();
        Harness harness = await CreateAsync(repository);

        harness.ViewModel.Items.Select(i => (i.Index, i.Name, i.Message, i.Branch)).Should().Equal(
            (0, "stash@{0}", "third on b", "topic"),
            (1, "stash@{1}", "second", "topic"),
            (2, "stash@{2}", "first", "topic"));
        harness.ViewModel.Items.Should().OnlyContain(i => i.DateText.Length == 16);
        StashItemViewModel.TryParse("stash@{4}\u001fWIP on main: abc1234 subject\u001f2024-05-06T07:08:09+02:00")!
            .Should().Match<StashItemViewModel>(i => i.Index == 4 && i.Message == "abc1234 subject" && i.Branch == "main");
        StashItemViewModel.TryParse("garbage").Should().BeNull();
    }

    [AvaloniaTest]
    public async Task Selecting_a_stash_shows_its_files_and_diff()
    {
        using TestRepository repository = new();
        repository.AddSeveralStashes();
        Harness harness = await CreateAsync(repository);

        harness.ViewModel.SelectedStash = harness.ViewModel.Items[0];
        await WaitAsync(() => harness.ViewModel.Files.Count == 1);
        harness.ViewModel.Files[0].Name.Should().Be("b.txt");
        harness.ViewModel.Files[0].Status.Should().Be("M");

        harness.ViewModel.SelectedFile = harness.ViewModel.Files[0];
        await WaitAsync(() => harness.ViewModel.DiffLines.Count > 0);
        harness.ViewModel.DiffLines.Select(l => l.Text).Should().Contain("+bbb").And.Contain("-second");
    }

    [AvaloniaTest]
    public async Task Main_window_stashes_and_refreshes_the_changes()
    {
        using TestRepository repository = new();
        repository.ConfigureIdentity();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await viewModel.OpenAsync(repository.Path);
        viewModel.ChangedFiles.Should().HaveCount(2);

        viewModel.Stash.IncludeUntracked = true;
        (await viewModel.Stash.ConfirmStashAsync()).Should().BeTrue();

        viewModel.ChangedFiles.Should().BeEmpty();
        viewModel.Stash.Items.Should().ContainSingle();

        (await viewModel.Stash.PopAsync(null!)).Should().BeFalse("nothing is selected");
        viewModel.Stash.SelectedStash = viewModel.Stash.Items[0];
        (await viewModel.Stash.PopAsync(null)).Should().BeTrue();
        viewModel.ChangedFiles.Should().HaveCount(2);
        viewModel.Stash.Items.Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Stash_flyout_renders_in_light_and_dark()
    {
        using TestRepository repository = new();
        repository.AddSeveralStashes();
        File.WriteAllText(Path.Combine(repository.Path, "a.txt"), "dirty again");
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel, Width = 1000, Height = 640 };
        window.Show();
        await viewModel.OpenAsync(repository.Path);
        await WaitAsync(() => viewModel.Stash.Items.Count == 3);

        StashMenuButton button = (StashMenuButton)window.GetVisualDescendants().First(c => c is StashMenuButton);
        button.Content.Should().Be("Stash (3) ▾");
        viewModel.Stash.SelectedStash = viewModel.Stash.Items[0];
        await WaitAsync(() => viewModel.Stash.Files.Count == 1);
        viewModel.Stash.SelectedFile = viewModel.Stash.Files[0];
        await WaitAsync(() => viewModel.Stash.DiffLines.Count > 0);
        viewModel.Stash.RequestDropCommand.Execute(viewModel.Stash.Items[1]);

        button.Flyout!.ShowAt(button);
        foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            window.RequestedThemeVariant = variant;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            StashView view = button.View;
            view.FindControl<ListBox>("StashList")!.ItemCount.Should().Be(3);
            view.FindControl<Panel>("DropPromptPanel")!.IsVisible.Should().BeTrue();

            WriteableBitmap? frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, $"stash-{variant.Key.ToString()!.ToLowerInvariant()}.png");
            frame!.Save(file);
            TestContext.AddTestAttachment(file);
        }

        window.Close();
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }

        condition().Should().BeTrue("the condition should become true in time");
    }
}
