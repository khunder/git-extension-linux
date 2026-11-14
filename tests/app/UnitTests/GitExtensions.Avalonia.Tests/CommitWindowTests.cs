using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class CommitWindowTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static string NewStorePath() => Path.Combine(Path.GetTempPath(), "ge-avalonia-commit-" + Guid.NewGuid().ToString("N") + ".json");

    private static void ConfigureIdentity(TestRepository repository)
    {
        repository.RunGit("config", "user.name", "Committer");
        repository.RunGit("config", "user.email", "committer@example.com");
    }

    private static async Task<(MainWindowViewModel Main, CommitWindowViewModel Window)> OpenAsync(string path, CommitWindowStore? store = null)
    {
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(path);
        await main.UseCurrentBranchAsync();
        CommitWindowViewModel window = main.CreateCommitWindowViewModel(store ?? new CommitWindowStore(NewStorePath()))!;
        await window.LoadAsync();
        return (main, window);
    }

    private static async Task WaitForAsync(Func<bool> condition, string because = "")
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(25);
            Dispatcher.UIThread.RunJobs();
        }

        condition().Should().BeTrue(because);
    }

    [AvaloniaTest]
    public async Task Lists_follow_stage_and_unstage()
    {
        using TestRepository repository = new();
        (MainWindowViewModel main, CommitWindowViewModel window) = await OpenAsync(repository.Path);

        window.Unstaged.Select(f => f.Name).Should().BeEquivalentTo("a.txt", "c.txt");
        window.Staged.Should().BeEmpty();
        window.StatusText.Should().Be("2 files unstaged, 0 staged");
        window.Title.Should().Contain("main");

        await window.StageFilesAsync([window.Unstaged.Single(f => f.Name == "a.txt")]);
        window.Staged.Select(f => f.Name).Should().Equal("a.txt");
        window.Unstaged.Select(f => f.Name).Should().Equal("c.txt");
        window.StatusText.Should().Be("1 file unstaged, 1 staged");
        main.ChangedFiles.Count(f => f.IsStaged).Should().Be(1, "the main window follows");

        await window.UnstageFilesAsync([window.Staged[0]]);
        window.Staged.Should().BeEmpty();
        window.Unstaged.Should().HaveCount(2);

        await window.StageAllCommand.ExecuteAsync(null);
        window.Staged.Should().HaveCount(2);
        await window.UnstageAllCommand.ExecuteAsync(null);
        window.Staged.Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Filter_and_untracked_toggle_narrow_the_lists()
    {
        using TestRepository repository = new();
        (_, CommitWindowViewModel window) = await OpenAsync(repository.Path);

        window.ShowUntracked = false;
        window.Unstaged.Select(f => f.Name).Should().Equal("a.txt");
        window.ShowUntracked = true;
        window.Filter = "^c\\.";
        window.Unstaged.Select(f => f.Name).Should().Equal("c.txt");
        window.Filter = "(";
        window.Unstaged.Should().BeEmpty("an invalid regex falls back to a literal search");
    }

    [AvaloniaTest]
    public async Task Commit_creates_a_commit_closes_and_refreshes_the_main_window()
    {
        using TestRepository repository = new();
        ConfigureIdentity(repository);
        repository.RunGit("add", "a.txt");
        (MainWindowViewModel main, CommitWindowViewModel window) = await OpenAsync(repository.Path);
        int closed = 0;
        window.CloseRequested += () => closed++;

        window.Commit.Message = "Change a.txt\n\nWith a body.";
        await window.CommitNowCommand.ExecuteAsync(null);

        repository.GitOutput("log", "-1", "--format=%B").Trim().Should().Be("Change a.txt\n\nWith a body.");
        repository.GitOutput("rev-list", "--count", "HEAD").Trim().Should().Be("3");
        window.ErrorText.Should().BeNull();
        window.Commit.Message.Should().BeEmpty();
        closed.Should().Be(1, "closing after a commit is the default");
        await WaitForAsync(() => main.Commits.Count == 3 && main.Commits[0].Subject == "Change a.txt");
        main.ChangedFiles.Should().ContainSingle(f => f.Name == "c.txt");
        window.RecentMessages.Should().Equal("Change a.txt\n\nWith a body.");
        window.HasUnsavedMessage.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Close_after_commit_setting_is_honoured_and_persisted()
    {
        using TestRepository repository = new();
        ConfigureIdentity(repository);
        repository.RunGit("add", "a.txt");
        string storePath = NewStorePath();
        (_, CommitWindowViewModel window) = await OpenAsync(repository.Path, new CommitWindowStore(storePath));
        int closed = 0;
        window.CloseRequested += () => closed++;
        window.CloseAfterCommit = false;

        window.Commit.Message = "Keep open";
        await window.CommitNowCommand.ExecuteAsync(null);

        closed.Should().Be(0);
        window.Unstaged.Select(f => f.Name).Should().Equal("c.txt");
        new CommitWindowStore(storePath).CloseAfterCommit.Should().BeFalse();
        new CommitWindowStore(NewStorePath()).CloseAfterCommit.Should().BeTrue("the default is on");
    }

    [AvaloniaTest]
    public async Task Amend_prefills_and_replaces_the_last_commit()
    {
        using TestRepository repository = new();
        ConfigureIdentity(repository);
        (_, CommitWindowViewModel window) = await OpenAsync(repository.Path);

        window.Commit.Amend = true;
        await WaitForAsync(() => window.Commit.Message == "Add b.txt");
        window.Commit.Message = "Reworded b";
        await window.CommitNowCommand.ExecuteAsync(null);

        repository.GitOutput("rev-list", "--count", "HEAD").Trim().Should().Be("2");
        repository.GitOutput("log", "-1", "--format=%s").Trim().Should().Be("Reworded b");
    }

    [AvaloniaTest]
    public async Task Sign_off_adds_the_trailer()
    {
        using TestRepository repository = new();
        ConfigureIdentity(repository);
        repository.RunGit("add", "a.txt");
        (_, CommitWindowViewModel window) = await OpenAsync(repository.Path);

        window.Commit.SignOff = true;
        window.Commit.Message = "Signed";
        await window.CommitNowCommand.ExecuteAsync(null);

        repository.GitOutput("log", "-1", "--format=%B").Should().Contain("Signed-off-by: Committer <committer@example.com>");
    }

    [AvaloniaTest]
    public async Task Validation_blocks_empty_messages_and_nothing_staged()
    {
        using TestRepository repository = new();
        ConfigureIdentity(repository);
        (_, CommitWindowViewModel window) = await OpenAsync(repository.Path);
        int closed = 0;
        window.CloseRequested += () => closed++;

        await window.CommitNowCommand.ExecuteAsync(null);
        window.ErrorText.Should().Be("Please enter commit message");

        window.Commit.Message = "Something";
        await window.CommitNowCommand.ExecuteAsync(null);
        window.ErrorText.Should().Be("There are no staged changes");
        window.HasError.Should().BeTrue();

        repository.GitOutput("rev-list", "--count", "HEAD").Trim().Should().Be("2");
        closed.Should().Be(0);
        window.Commit.Message.Should().Be("Something", "the message is kept");
    }

    [AvaloniaTest]
    public async Task Missing_identity_is_reported_inline_and_in_the_banner()
    {
        using TestRepository repository = new();
        repository.RunGit("config", "user.useConfigOnly", "true");
        repository.RunGit("add", "a.txt");
        string? oldGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        string? oldSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", "/dev/null");
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        try
        {
            (MainWindowViewModel main, CommitWindowViewModel window) = await OpenAsync(repository.Path);
            window.Commit.Message = "Will fail";

            await window.CommitNowCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(30));

            window.ErrorText.Should().NotBeNullOrEmpty();
            main.ErrorMessage.Should().Contain("Commit failed");
            window.Commit.Message.Should().Be("Will fail");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", oldGlobal);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", oldSystem);
        }
    }

    [AvaloniaTest]
    public async Task Commit_and_push_pushes_to_a_local_bare_origin()
    {
        using RemoteScenario scenario = new();
        RemoteScenario.Run(scenario.Alpha, "config", "user.name", "Committer");
        RemoteScenario.Run(scenario.Alpha, "config", "user.email", "committer@example.com");
        File.WriteAllText(Path.Combine(scenario.Alpha, "a.txt"), "two");
        RemoteScenario.Run(scenario.Alpha, "add", "a.txt");
        (_, CommitWindowViewModel window) = await OpenAsync(scenario.Alpha);

        window.Commit.Message = "Pushed change";
        await window.CommitAndPushCommand.ExecuteAsync(null);

        RemoteScenario.Run(scenario.Origin, "log", "-1", "--format=%s", "main").Trim().Should().Be("Pushed change");
        window.IsPushing.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Push_after_commit_option_pushes_on_a_plain_commit()
    {
        using RemoteScenario scenario = new();
        RemoteScenario.Run(scenario.Alpha, "config", "user.name", "Committer");
        RemoteScenario.Run(scenario.Alpha, "config", "user.email", "committer@example.com");
        File.WriteAllText(Path.Combine(scenario.Alpha, "a.txt"), "two");
        RemoteScenario.Run(scenario.Alpha, "add", "a.txt");
        (_, CommitWindowViewModel window) = await OpenAsync(scenario.Alpha);

        window.PushAfterCommit = true;
        window.Commit.Message = "Option push";
        await window.CommitNowCommand.ExecuteAsync(null);

        RemoteScenario.Run(scenario.Origin, "log", "-1", "--format=%s", "main").Trim().Should().Be("Option push");
    }

    [AvaloniaTest]
    public void Recent_messages_store_keeps_ten_unique_newest_first_and_persists()
    {
        string path = NewStorePath();
        CommitWindowStore store = new(path);
        for (int i = 1; i <= 12; i++)
        {
            store.AddRecent($"message {i}");
        }

        store.AddRecent("message 5\n");
        store.AddRecent("   ");

        store.RecentMessages.Should().HaveCount(CommitWindowStore.MaxRecentMessages);
        store.RecentMessages[0].Should().Be("message 5");
        store.RecentMessages.Count(m => m == "message 5").Should().Be(1);
        store.RecentMessages[1].Should().Be("message 12");

        new CommitWindowStore(path).RecentMessages.Should().Equal(store.RecentMessages);

        File.WriteAllText(path, "not json");
        new CommitWindowStore(path).RecentMessages.Should().BeEmpty("a corrupt file is ignored");
    }

    [AvaloniaTest]
    public async Task Reset_asks_for_confirmation_then_discards()
    {
        using TestRepository repository = new();
        (_, CommitWindowViewModel window) = await OpenAsync(repository.Path);

        window.BeginReset([window.Unstaged.Single(f => f.Name == "a.txt")]);
        window.IsConfirmingReset.Should().BeTrue();
        window.CancelResetCommand.Execute(null);
        File.ReadAllText(Path.Combine(repository.Path, "a.txt")).Should().Be("changed");

        window.BeginReset([window.Unstaged.Single(f => f.Name == "a.txt"), window.Unstaged.Single(f => f.Name == "c.txt")]);
        await window.ConfirmResetCommand.ExecuteAsync(null);

        File.ReadAllText(Path.Combine(repository.Path, "a.txt")).Should().Be("first");
        File.Exists(Path.Combine(repository.Path, "c.txt")).Should().BeFalse();
        window.AllChanges.Should().BeEmpty();

        File.WriteAllText(Path.Combine(repository.Path, "a.txt"), "again");
        repository.RunGit("add", "a.txt");
        await window.RefreshAsync();
        window.ResetAllCommand.Execute(null);
        await window.ConfirmResetCommand.ExecuteAsync(null);
        window.AllChanges.Should().BeEmpty();
        File.ReadAllText(Path.Combine(repository.Path, "a.txt")).Should().Be("first");
    }

    [AvaloniaTest]
    public async Task Window_closes_when_the_repository_is_closed()
    {
        using TestRepository repository = new();
        (MainWindowViewModel main, CommitWindowViewModel window) = await OpenAsync(repository.Path);
        int closed = 0;
        window.CloseRequested += () => closed++;

        main.HasOpenedRepository = false;

        closed.Should().Be(1);
    }

    [AvaloniaTest]
    public async Task Refresh_survives_a_main_window_reload_in_parallel()
    {
        using TestRepository repository = new();
        (MainWindowViewModel main, CommitWindowViewModel window) = await OpenAsync(repository.Path);

        Task reload = main.OpenAsync(repository.Path);
        await Task.WhenAll(window.RefreshAsync(), window.RefreshAsync(), reload);

        window.AllChanges.Should().HaveCount(2);
        window.IsBusy.Should().BeFalse();
    }

    // ---- headless window tests -------------------------------------------------------------------------------------

    private static async Task<(WindowService Service, Window Owner, MainWindowViewModel Main, CommitWindow Window)> ShowAsync(string path, ThemeVariant? variant = null)
    {
        CommitWindowStore.Current = new CommitWindowStore(NewStorePath());
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        Window owner = new() { Width = 800, Height = 600 };
        owner.Show();
        await main.OpenAsync(path);
        WindowService service = new(main, owner);
        main.WindowService = service;
        main.ShowCommitWindowCommand.Execute(null);
        CommitWindow window = service.CommitWindow!;
        window.Width = 1000;
        window.Height = 700;
        if (variant is not null)
        {
            window.RequestedThemeVariant = variant;
        }

        CommitWindowViewModel viewModel = (CommitWindowViewModel)window.DataContext!;
        await WaitForAsync(() => viewModel.Branch is not null && !viewModel.IsBusy);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        return (service, owner, main, window);
    }

    [AvaloniaTest]
    public async Task Service_opens_one_non_modal_window_per_repository_and_shows_the_lists()
    {
        using TestRepository repository = new();
        repository.RunGit("add", "a.txt");
        (WindowService service, Window owner, MainWindowViewModel main, CommitWindow window) = await ShowAsync(repository.Path);

        window.IsVisible.Should().BeTrue();
        window.Title.Should().Be($"Commit to {Path.GetFileName(repository.Path)} (main)");
        window.FindControl<ListBox>("UnstagedList")!.ItemCount.Should().Be(1);
        window.FindControl<ListBox>("StagedList")!.ItemCount.Should().Be(1);
        window.FindControl<TextBlock>("StatusLine")!.Text.Should().Be("1 file unstaged, 1 staged");

        main.ShowCommitWindowCommand.Execute(null);
        service.CommitWindow.Should().BeSameAs(window, "a second request activates the same window");

        window.Close();
        service.CommitWindow.Should().BeNull();
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Selecting_a_file_shows_its_diff_and_buttons_stage()
    {
        using TestRepository repository = new();
        (_, Window owner, MainWindowViewModel main, CommitWindow window) = await ShowAsync(repository.Path);
        CommitWindowViewModel viewModel = (CommitWindowViewModel)window.DataContext!;
        ListBox unstaged = window.FindControl<ListBox>("UnstagedList")!;

        unstaged.SelectedIndex = 0;
        await WaitForAsync(() => viewModel.Diff.Lines.Count > 0 && !viewModel.Diff.IsLoading);
        viewModel.Diff.File!.Name.Should().Be("a.txt");

        window.FindControl<Button>("StageSelectedButton")!.Command!.Execute(null);
        await WaitForAsync(() => viewModel.Staged.Count == 1);
        viewModel.Unstaged.Select(f => f.Name).Should().Equal("c.txt");
        await WaitForAsync(() => main.ChangedFiles.Any(f => f.IsStaged));

        window.Close();
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Hunk_staging_works_inside_the_window()
    {
        using MultiHunkRepository repo = new();
        (_, Window owner, _, CommitWindow window) = await ShowAsync(repo.Path);
        CommitWindowViewModel viewModel = (CommitWindowViewModel)window.DataContext!;

        window.FindControl<ListBox>("UnstagedList")!.SelectedIndex = 0;
        await WaitForAsync(() => !viewModel.Diff.IsLoading && viewModel.Diff.Lines.Count(l => l.Kind == DiffLineKind.Hunk) == 3);

        DiffLine hunk = viewModel.Diff.Lines.Single(l => l.Kind == DiffLineKind.Hunk && l.HunkIndex == 1);
        await viewModel.Diff.StageHunkCommand.ExecuteAsync(hunk);

        await WaitForAsync(() => viewModel.Staged.Count == 1 && !viewModel.Diff.IsLoading && viewModel.Diff.Lines.Count(l => l.Kind == DiffLineKind.Hunk) == 2);
        repo.Git("diff", "--cached", "-U0").Should().Contain("+changed 15").And.NotContain("changed 3\n");
        viewModel.Unstaged.Should().ContainSingle(f => f.Name == "m.txt");

        window.Close();
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Escape_closes_directly_or_asks_when_a_message_would_be_lost()
    {
        using TestRepository repository = new();
        (_, Window owner, _, CommitWindow window) = await ShowAsync(repository.Path);
        CommitWindowViewModel viewModel = (CommitWindowViewModel)window.DataContext!;
        window.FindControl<TextBox>("MessageBox")!.Text = "Draft message";
        window.FindControl<TextBox>("MessageBox")!.Focus();

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        viewModel.IsConfirmingClose.Should().BeTrue();
        window.FindControl<Border>("CloseConfirmation")!.IsVisible.Should().BeTrue();
        window.IsVisible.Should().BeTrue();

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        viewModel.IsConfirmingClose.Should().BeFalse("a second Escape cancels the question");

        window.FindControl<Button>("DiscardCloseButton")!.Command!.Execute(null);
        viewModel.CloseRequestedForTest();
        await WaitForAsync(() => !window.IsVisible);
        CommitWindowStore.Current.RecentMessages.Should().Contain("Draft message", "a discarded draft stays recoverable");
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Escape_closes_without_asking_when_the_message_is_empty()
    {
        using TestRepository repository = new();
        (WindowService service, Window owner, _, CommitWindow window) = await ShowAsync(repository.Path);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        await WaitForAsync(() => !window.IsVisible);
        service.CommitWindow.Should().BeNull();
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Ctrl_Enter_commits_and_the_window_closes()
    {
        using TestRepository repository = new();
        ConfigureIdentity(repository);
        repository.RunGit("add", "a.txt");
        (_, Window owner, MainWindowViewModel main, CommitWindow window) = await ShowAsync(repository.Path);
        TextBox box = window.FindControl<TextBox>("MessageBox")!;
        box.Text = "Committed from the window";
        box.Focus();

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);

        await WaitForAsync(() => !window.IsVisible);
        repository.GitOutput("log", "-1", "--format=%s").Trim().Should().Be("Committed from the window");
        await WaitForAsync(() => main.Commits.Count == 3);
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Window_closes_with_the_repository()
    {
        using TestRepository repository = new();
        (WindowService service, Window owner, MainWindowViewModel main, CommitWindow window) = await ShowAsync(repository.Path);
        window.FindControl<TextBox>("MessageBox")!.Text = "Unsaved but repository closes";

        main.HasOpenedRepository = false;

        await WaitForAsync(() => !window.IsVisible);
        service.CommitWindow.Should().BeNull();
        owner.Close();
    }

    [AvaloniaTest]
    [TestCase("Light")]
    [TestCase("Dark")]
    public async Task Commit_window_renders(string theme)
    {
        using MultiHunkRepository repo = new();
        repo.Write("new1.txt", "brand new\n");
        repo.Write("src.txt", "x\n");
        repo.Repository.RunGit("add", "src.txt");
        repo.Repository.RunGit("rm", "--quiet", "--cached", "b.txt");
        (_, Window owner, _, CommitWindow window) = await ShowAsync(repo.Path, theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        CommitWindowViewModel viewModel = (CommitWindowViewModel)window.DataContext!;
        window.FindControl<ListBox>("UnstagedList")!.SelectedItem = viewModel.Unstaged.First(f => f.Name == "m.txt");
        await WaitForAsync(() => !viewModel.Diff.IsLoading && viewModel.Diff.Lines.Count > 0);
        window.FindControl<TextBox>("MessageBox")!.Text = "Fix the thing\n\nLonger explanation of the change.";
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, $"commit-window-{theme.ToLowerInvariant()}.png");
        frame!.Save(file);
        TestContext.AddTestAttachment(file);

        viewModel.CloseAfterCommit = true;
        window.Close();
        owner.Close();
    }
}

internal static class CommitWindowTestExtensions
{
    /// <summary>The discard command asks the window to close through the view-model event; let queued UI work run.</summary>
    public static void CloseRequestedForTest(this CommitWindowViewModel viewModel) => Dispatcher.UIThread.RunJobs();
}
