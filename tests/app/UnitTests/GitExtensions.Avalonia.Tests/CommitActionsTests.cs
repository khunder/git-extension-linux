using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class CommitActionsTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; private set; }

        public Task SetTextAsync(string text)
        {
            Text = text;
            return Task.CompletedTask;
        }
    }

    private static async Task<(TestRepository Repository, MainWindowViewModel ViewModel)> OpenMergedAsync()
    {
        TestRepository repository = new();
        repository.AddMergedHistory();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await viewModel.OpenAsync(repository.Path);
        return (repository, viewModel);
    }

    private static CommitItemViewModel Commit(MainWindowViewModel viewModel, string subject)
        => viewModel.Commits.Single(c => c.Subject == subject);

    private static string Subjects(TestRepository repository, params string[] extra)
        => repository.GitOutput(["log", "--format=%s", .. extra]).Replace("\n", "|");

    [AvaloniaTest]
    public async Task History_has_graph_rows_with_a_second_lane_for_the_merged_branch()
    {
        (TestRepository repository, MainWindowViewModel viewModel) = await OpenMergedAsync();
        using (repository)
        {
            viewModel.Commits.Should().HaveCount(7);
            viewModel.Commits.Should().OnlyContain(c => c.Graph != null && c.Actions != null);
            viewModel.GraphLaneCount.Should().Be(2);
            Commit(viewModel, "Feature more").Graph!.Lane.Should().Be(1);
            Commit(viewModel, "Main work").Graph!.Lane.Should().Be(0);
        }
    }

    [AvaloniaTest]
    public async Task Create_branch_and_tag_via_prompt()
    {
        (TestRepository repository, MainWindowViewModel viewModel) = await OpenMergedAsync();
        using (repository)
        {
            CommitItemViewModel target = Commit(viewModel, "Main work");
            CommitActionsViewModel actions = viewModel.Actions;

            actions.RequestCreateBranchCommand.Execute(target);
            actions.IsPromptVisible.Should().BeTrue();
            actions.NeedsInput.Should().BeTrue();
            actions.InputText = "topic";
            await actions.ConfirmPromptAsync();

            actions.IsPromptVisible.Should().BeFalse();
            repository.GitOutput("rev-parse", "topic").Trim().Should().Be(target.ObjectId.ToString());

            actions.RequestCreateTagCommand.Execute(target);
            actions.InputText = "v9";
            await actions.ConfirmPromptAsync();
            repository.GitOutput("rev-parse", "v9^{commit}").Trim().Should().Be(target.ObjectId.ToString());

            // The refresh reloaded the history: the new branch is listed with the commit.
            Commit(viewModel, "Main work").Refs.Should().Contain("topic").And.Contain("v9");
        }
    }

    [AvaloniaTest]
    public async Task Creating_a_duplicate_branch_reports_the_error_in_the_banner()
    {
        (TestRepository repository, MainWindowViewModel viewModel) = await OpenMergedAsync();
        using (repository)
        {
            bool ok = await viewModel.Actions.CreateBranchAsync(Commit(viewModel, "Main work"), "feature");

            ok.Should().BeFalse();
            viewModel.ErrorMessage.Should().Contain("already exists");
            viewModel.HasError.Should().BeTrue();
        }
    }

    [AvaloniaTest]
    public async Task Checkout_detaches_head_at_the_commit()
    {
        (TestRepository repository, MainWindowViewModel viewModel) = await OpenMergedAsync();
        using (repository)
        {
            CommitItemViewModel target = Commit(viewModel, "Main work");

            (await viewModel.Actions.CheckoutAsync(target)).Should().BeTrue();

            repository.GitOutput("rev-parse", "HEAD").Trim().Should().Be(target.ObjectId.ToString());
            repository.GitOutput("symbolic-ref", "-q", "HEAD").Should().BeEmpty("HEAD is detached");
        }
    }

    [AvaloniaTest]
    public async Task Soft_and_mixed_reset_keep_the_changes_of_the_dropped_commit()
    {
        (TestRepository repository, MainWindowViewModel viewModel) = await OpenMergedAsync();
        using (repository)
        {
            // Drop only "After merge" (s.txt).
            CommitItemViewModel target = Commit(viewModel, "Merge feature");

            (await viewModel.Actions.ResetAsync(target, CommitResetMode.Soft)).Should().BeTrue();
            repository.GitOutput("rev-parse", "HEAD").Trim().Should().Be(target.ObjectId.ToString());
            repository.GitOutput("status", "--porcelain", "s.txt").Should().StartWith("A  s.txt");

            (await viewModel.Actions.ResetAsync(target, CommitResetMode.Mixed)).Should().BeTrue();
            repository.GitOutput("status", "--porcelain", "s.txt").Should().StartWith("?? s.txt");
            File.Exists(Path.Combine(repository.Path, "s.txt")).Should().BeTrue();
        }
    }

    [AvaloniaTest]
    public async Task Hard_reset_is_blocked_without_confirmation_and_runs_after_it()
    {
        (TestRepository repository, MainWindowViewModel viewModel) = await OpenMergedAsync();
        using (repository)
        {
            string head = repository.GitOutput("rev-parse", "HEAD").Trim();
            CommitItemViewModel target = Commit(viewModel, "Merge feature");

            (await viewModel.Actions.ResetAsync(target, CommitResetMode.Hard)).Should().BeFalse();

            repository.GitOutput("rev-parse", "HEAD").Trim().Should().Be(head, "nothing may run without confirmation");
            File.ReadAllText(Path.Combine(repository.Path, "a.txt")).Should().Be("changed");
            viewModel.Actions.PromptKind.Should().Be(CommitPromptKind.ConfirmHardReset);

            // Cancelling keeps everything.
            viewModel.Actions.CancelPromptCommand.Execute(null);
            viewModel.Actions.IsPromptVisible.Should().BeFalse();
            repository.GitOutput("rev-parse", "HEAD").Trim().Should().Be(head);

            // The menu path: request, then confirm.
            viewModel.Actions.RequestHardResetCommand.Execute(target);
            await viewModel.Actions.ConfirmPromptAsync();

            repository.GitOutput("rev-parse", "HEAD").Trim().Should().Be(target.ObjectId.ToString());
            File.Exists(Path.Combine(repository.Path, "s.txt")).Should().BeFalse();
            File.ReadAllText(Path.Combine(repository.Path, "a.txt")).Should().Be("first", "tracked changes are discarded by --hard");
        }
    }

    [AvaloniaTest]
    public async Task Revert_adds_a_commit_undoing_the_change()
    {
        (TestRepository repository, MainWindowViewModel viewModel) = await OpenMergedAsync();
        using (repository)
        {
            (await viewModel.Actions.RevertAsync(Commit(viewModel, "Main work"))).Should().BeTrue();

            Subjects(repository).Should().StartWith("Revert \"Main work\"|After merge|");
            File.Exists(Path.Combine(repository.Path, "m.txt")).Should().BeFalse();
            viewModel.Commits[0].Subject.Should().StartWith("Revert");
        }
    }

    [AvaloniaTest]
    public async Task Revert_of_a_merge_commit_uses_the_first_parent()
    {
        (TestRepository repository, MainWindowViewModel viewModel) = await OpenMergedAsync();
        using (repository)
        {
            (await viewModel.Actions.RevertAsync(Commit(viewModel, "Merge feature"))).Should().BeTrue();

            File.Exists(Path.Combine(repository.Path, "f.txt")).Should().BeFalse();
            File.Exists(Path.Combine(repository.Path, "m.txt")).Should().BeTrue();
        }
    }

    [AvaloniaTest]
    public async Task Cherry_pick_applies_a_commit_from_another_branch()
    {
        (TestRepository repository, MainWindowViewModel viewModel) = await OpenMergedAsync();
        using (repository)
        {
            repository.RunGit("checkout", "--quiet", "-b", "side", "v1.0");
            repository.CommitFile("x.txt", "x", "Side change");
            string side = repository.GitOutput("rev-parse", "HEAD").Trim();
            repository.RunGit("checkout", "--quiet", "main");
            CommitItemViewModel pick = new(new GitRevision(ObjectId.Parse(side)), string.Empty);

            (await viewModel.Actions.CherryPickAsync(pick)).Should().BeTrue();

            Subjects(repository, "-1").Should().Be("Side change|");
            File.Exists(Path.Combine(repository.Path, "x.txt")).Should().BeTrue();
        }
    }

    [AvaloniaTest]
    public async Task Cherry_pick_conflict_is_left_in_progress_for_the_operation_bar()
    {
        (TestRepository repository, MainWindowViewModel viewModel) = await OpenMergedAsync();
        using (repository)
        {
            repository.RunGit("checkout", "--quiet", "-b", "side", "v1.0");
            repository.CommitFile("m.txt", "other", "Conflicting");
            string side = repository.GitOutput("rev-parse", "HEAD").Trim();
            repository.RunGit("checkout", "--quiet", "main");
            string head = repository.GitOutput("rev-parse", "HEAD").Trim();
            CommitItemViewModel pick = new(new GitRevision(ObjectId.Parse(side)), string.Empty);

            (await viewModel.Actions.CherryPickAsync(pick)).Should().BeFalse();
            await viewModel.Operation.RefreshAsync();

            viewModel.HasError.Should().BeFalse();
            viewModel.Operation.Kind.Should().Be(OperationKind.CherryPick);
            repository.GitOutput("rev-parse", "HEAD").Trim().Should().Be(head);
            repository.GitOutput("status", "--porcelain").Should().Contain("m.txt");
            File.Exists(Path.Combine(repository.Path, ".git", "CHERRY_PICK_HEAD")).Should().BeTrue();

            (await viewModel.Operation.AbortAsync(confirmed: true)).Should().BeTrue();
            repository.GitOutput("status", "--porcelain").Should().NotContain("m.txt");
            File.Exists(Path.Combine(repository.Path, ".git", "CHERRY_PICK_HEAD")).Should().BeFalse();
        }
    }

    private static string MenuText(MenuItem item) => (item.Header as TextBlock)?.Text ?? string.Empty;

    [AvaloniaTest]
    public async Task Context_menu_items_are_bound_and_copy_uses_the_clipboard()
    {
        (TestRepository repository, MainWindowViewModel viewModel) = await OpenMergedAsync();
        using (repository)
        {
            FakeClipboard clipboard = new();
            viewModel.Actions.Clipboard = clipboard;
            MainWindow window = new() { DataContext = viewModel };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            ListBox list = window.Find<ListBox>("CommitList");
            list.GetVisualDescendants().OfType<ListBoxItem>().First().RaiseEvent(new ContextRequestedEventArgs());
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            ContextMenu menu = list.ContextMenu!;
            MenuItem[] items = menu.Items.OfType<MenuItem>().ToArray();

            items.Select(MenuText).Should().Contain(["Copy to clipboard", "Create new branch here…", "Create new tag here…", "Revert this commit…", "Cherry pick this commit…", "Reset current branch to here…"]);
            items.Where(i => i.IsEnabled && i.Items.Count == 0).Should().OnlyContain(i => i.Icon != null);

            MenuItem copy = items.Single(i => MenuText(i) == "Copy to clipboard");
            MenuItem copyHash = copy.Items.OfType<MenuItem>().Single(i => MenuText(i).StartsWith("Commit hash", StringComparison.Ordinal));
            copyHash.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            await Task.Yield();
            clipboard.Text.Should().Be(viewModel.Commits[0].ObjectId.ToString());
            menu.Close();
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task MainWindow_renders_the_history_graph()
    {
        (TestRepository repository, MainWindowViewModel viewModel) = await OpenMergedAsync();
        using (repository)
        {
            MainWindow window = new() { DataContext = viewModel };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            window.GetVisualDescendants().OfType<CommitGraphCell>().Should().NotBeEmpty();

            WriteableBitmap? frame = window.CaptureRenderedFrame();
            string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "main-window-graph.png");
            frame!.Save(file);
            TestContext.AddTestAttachment(file);

            // Same window with the hard-reset prompt open.
            viewModel.Actions.RequestHardResetCommand.Execute(Commit(viewModel, "Main work"));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            frame = window.CaptureRenderedFrame();
            frame!.Save(Path.Combine(directory, "main-window-graph-prompt.png"));
            window.Close();
        }
    }
}
