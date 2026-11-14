using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class MergeRebaseTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static async Task<MainWindowViewModel> OpenAsync(TestRepository repository)
    {
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await viewModel.OpenAsync(repository.Path);
        await viewModel.Operation.RefreshAsync();
        return viewModel;
    }

    private static async Task SelectConflictAsync(MainWindowViewModel viewModel, string name = "conflict.txt")
    {
        ChangedFileViewModel file = viewModel.ChangedFiles.First(c => c.Name == name && c.IsConflicted);
        viewModel.SelectedChangedFile = file;
        await viewModel.Resolver.ShowAsync(file, viewModel.ChangedFiles);
    }

    private static bool InProgress(TestRepository repository) => RepositoryOperationState.Detect(Path.Combine(repository.Path, ".git")) != OperationKind.None;

    [AvaloniaTest]
    public async Task Clean_merge_with_no_ff_creates_a_merge_commit()
    {
        using TestRepository repository = new();
        repository.AddCleanDivergedBranches();
        MainWindowViewModel viewModel = await OpenAsync(repository);

        (await viewModel.Operation.MergeAsync("topic", MergeMode.NoFastForward)).Should().BeTrue();

        repository.Parents().Should().HaveCount(2);
        File.Exists(Path.Combine(repository.Path, "t.txt")).Should().BeTrue();
        viewModel.Operation.IsActive.Should().BeFalse();
        viewModel.Commits[0].Revision.ParentIds.Should().HaveCount(2);
    }

    [AvaloniaTest]
    public async Task Fast_forward_only_refuses_when_branches_diverged()
    {
        using TestRepository repository = new();
        repository.AddCleanDivergedBranches();
        string head = repository.Head();
        MainWindowViewModel viewModel = await OpenAsync(repository);

        (await viewModel.Operation.MergeAsync("topic", MergeMode.FastForwardOnly)).Should().BeFalse();

        viewModel.HasError.Should().BeTrue();
        repository.Head().Should().Be(head);
        viewModel.Operation.IsActive.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Fast_forward_only_succeeds_when_possible()
    {
        using TestRepository repository = new();
        repository.AddCleanDivergedBranches();
        repository.RunGit("checkout", "--quiet", "-b", "behind", "main~1");
        MainWindowViewModel viewModel = await OpenAsync(repository);

        (await viewModel.Operation.MergeAsync("main", MergeMode.FastForwardOnly)).Should().BeTrue();

        repository.Head().Should().Be(repository.GitOutput("rev-parse", "main").Trim());
    }

    [AvaloniaTest]
    public async Task Merge_into_a_dirty_tree_reports_the_git_error()
    {
        using TestRepository repository = new();
        repository.AddCleanDivergedBranches();
        repository.RunGit("checkout", "--quiet", "-b", "dirty", "topic");
        repository.CommitFile("m.txt", "other", "Topic m");
        repository.RunGit("checkout", "--quiet", "main");
        File.WriteAllText(Path.Combine(repository.Path, "m.txt"), "local edit");
        MainWindowViewModel viewModel = await OpenAsync(repository);

        (await viewModel.Operation.MergeAsync("dirty")).Should().BeFalse();

        viewModel.HasError.Should().BeTrue();
        viewModel.ErrorMessage.Should().Contain("git merge failed");
        viewModel.Operation.IsActive.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Conflicting_merge_enters_the_in_progress_state_with_the_unmerged_files()
    {
        using TestRepository repository = new();
        repository.AddConflictingBranches();
        MainWindowViewModel viewModel = await OpenAsync(repository);

        (await viewModel.Operation.MergeAsync("topic")).Should().BeFalse();

        viewModel.HasError.Should().BeFalse("a conflict is a state, not an error");
        viewModel.Operation.Kind.Should().Be(OperationKind.Merge);
        viewModel.Operation.UnmergedCount.Should().Be(1);
        viewModel.Operation.CanContinue.Should().BeFalse();
        viewModel.Operation.CanAbort.Should().BeTrue();
        viewModel.Operation.CanSkip.Should().BeFalse();
        ChangedFileViewModel conflict = viewModel.ChangedFiles.Single(c => c.IsConflicted);
        conflict.Name.Should().Be("conflict.txt");
        conflict.Status.Should().Be("U");
        viewModel.ChangedFiles.Where(c => c.Name == "t.txt").Should().OnlyContain(c => !c.IsConflicted);
    }

    [AvaloniaTest]
    public async Task Resolver_shows_markers_and_use_ours_then_continue_creates_the_merge_commit()
    {
        using TestRepository repository = new();
        repository.AddConflictingBranches();
        repository.RunGit("config", "core.editor", "false");
        string mainTip = repository.Head();
        string topicTip = repository.GitOutput("rev-parse", "topic").Trim();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        await viewModel.Operation.MergeAsync("topic");
        await SelectConflictAsync(viewModel);

        ConflictResolverViewModel resolver = viewModel.Resolver;
        resolver.IsActive.Should().BeTrue();
        resolver.Kind.Should().Be(ConflictKind.BothModified);
        resolver.HasMarkers.Should().BeTrue();
        resolver.Lines.Where(l => l.IsMarker).Select(l => l.Text[..7]).Should().Equal("<<<<<<<", "=======", ">>>>>>>");
        resolver.Lines.Single(l => l.IsOurs).Text.Should().Be("main version");
        resolver.Lines.Single(l => l.IsTheirs).Text.Should().Be("topic version");

        (await resolver.UseOursAsync()).Should().BeTrue();

        repository.WorkFile("conflict.txt").Should().Be("one\nmain version\nthree\n");
        viewModel.Operation.UnmergedCount.Should().Be(0);
        viewModel.Operation.CanContinue.Should().BeTrue();
        resolver.IsActive.Should().BeFalse();

        (await viewModel.Operation.ContinueAsync()).Should().BeTrue();

        InProgress(repository).Should().BeFalse();
        repository.Parents().Should().Equal(mainTip, topicTip);
        repository.GitOutput("status", "--porcelain").Should().BeEmpty();
        viewModel.Operation.IsActive.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Use_theirs_takes_the_incoming_version()
    {
        using TestRepository repository = new();
        repository.AddConflictingBranches();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        await viewModel.Operation.MergeAsync("topic");
        await SelectConflictAsync(viewModel);

        (await viewModel.Resolver.UseTheirsAsync()).Should().BeTrue();

        repository.WorkFile("conflict.txt").Should().Be("one\ntopic version\nthree\n");
        viewModel.Operation.CanContinue.Should().BeTrue();
    }

    [AvaloniaTest]
    public async Task Mark_resolved_stages_a_manually_edited_file()
    {
        using TestRepository repository = new();
        repository.AddConflictingBranches();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        await viewModel.Operation.MergeAsync("topic");
        await SelectConflictAsync(viewModel);
        File.WriteAllText(Path.Combine(repository.Path, "conflict.txt"), "one\nboth versions\nthree\n");
        await viewModel.Resolver.ReloadCommand.ExecuteAsync(null);
        viewModel.Resolver.HasMarkers.Should().BeFalse();

        (await viewModel.Resolver.MarkResolvedAsync()).Should().BeTrue();
        (await viewModel.Operation.ContinueAsync()).Should().BeTrue();

        repository.GitOutput("show", "HEAD:conflict.txt").Should().Be("one\nboth versions\nthree\n");
        repository.Parents().Should().HaveCount(2);
    }

    [AvaloniaTest]
    public async Task Abort_needs_confirmation_and_restores_the_pre_merge_state()
    {
        using TestRepository repository = new();
        repository.AddConflictingBranches();
        string head = repository.Head();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        await viewModel.Operation.MergeAsync("topic");
        await SelectConflictAsync(viewModel);
        await viewModel.Resolver.UseTheirsAsync();

        (await viewModel.Operation.AbortAsync()).Should().BeFalse();
        viewModel.Operation.IsConfirmingAbort.Should().BeTrue();
        InProgress(repository).Should().BeTrue("nothing is aborted without the confirmation");

        (await viewModel.Operation.AbortAsync(confirmed: true)).Should().BeTrue();

        InProgress(repository).Should().BeFalse();
        repository.Head().Should().Be(head);
        repository.GitOutput("status", "--porcelain").Should().BeEmpty();
        repository.WorkFile("conflict.txt").Should().Be("one\nmain version\nthree\n");
        viewModel.Operation.IsActive.Should().BeFalse();
        viewModel.Operation.IsConfirmingAbort.Should().BeFalse();
        viewModel.ChangedFiles.Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Rebase_conflict_can_be_resolved_and_continued()
    {
        using TestRepository repository = new();
        repository.AddConflictingBranches();
        repository.RunGit("checkout", "--quiet", "topic");
        repository.RunGit("config", "core.editor", "false");
        string mainTip = repository.GitOutput("rev-parse", "main").Trim();
        MainWindowViewModel viewModel = await OpenAsync(repository);

        (await viewModel.Operation.RebaseAsync("main")).Should().BeFalse();

        viewModel.Operation.Kind.Should().Be(OperationKind.Rebase);
        viewModel.Operation.CanSkip.Should().BeTrue();
        viewModel.Operation.CanContinue.Should().BeFalse();
        viewModel.HasError.Should().BeFalse();

        await SelectConflictAsync(viewModel);
        (await viewModel.Resolver.UseTheirsAsync()).Should().BeTrue();
        (await viewModel.Operation.ContinueAsync()).Should().BeTrue();

        InProgress(repository).Should().BeFalse();
        repository.GitOutput("log", "--format=%s", "-3").Replace("\n", "|").Should().Be("Topic adds t.txt|Topic changes conflict.txt|Main adds m.txt|");
        repository.GitOutput("rev-parse", "HEAD~2").Trim().Should().Be(mainTip);
        repository.WorkFile("conflict.txt").Should().Be("one\ntopic version\nthree\n");
        repository.GitOutput("branch", "--show-current").Trim().Should().Be("topic");
    }

    [AvaloniaTest]
    public async Task Rebase_conflict_can_skip_the_conflicting_commit()
    {
        using TestRepository repository = new();
        repository.AddConflictingBranches();
        repository.RunGit("checkout", "--quiet", "topic");
        MainWindowViewModel viewModel = await OpenAsync(repository);
        await viewModel.Operation.RebaseAsync("main");

        (await viewModel.Operation.SkipAsync()).Should().BeTrue();

        InProgress(repository).Should().BeFalse();
        repository.GitOutput("log", "--format=%s", "-3").Replace("\n", "|").Should().Be("Topic adds t.txt|Main adds m.txt|Main changes conflict.txt|");
        repository.WorkFile("conflict.txt").Should().Be("one\nmain version\nthree\n");
    }

    [AvaloniaTest]
    public async Task Rebase_conflict_can_be_aborted()
    {
        using TestRepository repository = new();
        repository.AddConflictingBranches();
        repository.RunGit("checkout", "--quiet", "topic");
        string head = repository.Head();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        await viewModel.Operation.RebaseAsync("main");
        InProgress(repository).Should().BeTrue();

        (await viewModel.Operation.AbortAsync(confirmed: true)).Should().BeTrue();

        InProgress(repository).Should().BeFalse();
        repository.Head().Should().Be(head);
        repository.GitOutput("branch", "--show-current").Trim().Should().Be("topic");
        repository.GitOutput("status", "--porcelain").Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Rebase_of_a_dirty_tree_reports_the_git_error()
    {
        using TestRepository repository = new();
        repository.AddCleanDivergedBranches();
        repository.RunGit("checkout", "--quiet", "topic");
        File.WriteAllText(Path.Combine(repository.Path, "t.txt"), "dirty");
        MainWindowViewModel viewModel = await OpenAsync(repository);

        (await viewModel.Operation.RebaseAsync("main")).Should().BeFalse();

        viewModel.HasError.Should().BeTrue();
        viewModel.Operation.IsActive.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Cherry_pick_conflict_leaves_the_state_and_can_be_aborted_from_the_bar()
    {
        using TestRepository repository = new();
        repository.AddConflictingBranches();
        string head = repository.Head();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        CommitItemViewModel pick = new(new GitRevision(ObjectId.Parse(repository.GitOutput("rev-parse", "topic~1").Trim())), string.Empty);

        (await viewModel.Actions.CherryPickAsync(pick)).Should().BeFalse();
        await viewModel.Operation.RefreshAsync();

        viewModel.HasError.Should().BeFalse();
        viewModel.Operation.Kind.Should().Be(OperationKind.CherryPick);
        viewModel.Operation.UnmergedCount.Should().Be(1);

        (await viewModel.Operation.AbortAsync(confirmed: true)).Should().BeTrue();

        InProgress(repository).Should().BeFalse();
        repository.Head().Should().Be(head);
        repository.GitOutput("status", "--porcelain").Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Cherry_pick_conflict_can_be_resolved_and_continued()
    {
        using TestRepository repository = new();
        repository.AddConflictingBranches();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        CommitItemViewModel pick = new(new GitRevision(ObjectId.Parse(repository.GitOutput("rev-parse", "topic~1").Trim())), string.Empty);
        await viewModel.Actions.CherryPickAsync(pick);
        await viewModel.Operation.RefreshAsync();
        await SelectConflictAsync(viewModel);
        await viewModel.Resolver.UseTheirsAsync();

        (await viewModel.Operation.ContinueAsync()).Should().BeTrue();

        InProgress(repository).Should().BeFalse();
        repository.GitOutput("log", "-1", "--format=%s").Trim().Should().Be("Topic changes conflict.txt");
    }

    [AvaloniaTest]
    public async Task Revert_conflict_leaves_the_state_and_can_be_aborted()
    {
        using TestRepository repository = new();
        repository.AddConflictingBranches();
        repository.RunGit("checkout", "--quiet", "topic");
        repository.CommitFile("conflict.txt", "one\ntopic later\nthree\n", "Topic edits again");
        string head = repository.Head();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        CommitItemViewModel target = viewModel.Commits.Single(c => c.Subject == "Topic changes conflict.txt");

        (await viewModel.Actions.RevertAsync(target)).Should().BeFalse();
        await viewModel.Operation.RefreshAsync();

        viewModel.HasError.Should().BeFalse();
        viewModel.Operation.Kind.Should().Be(OperationKind.Revert);
        viewModel.Operation.Title.Should().Be("Revert in progress");

        (await viewModel.Operation.AbortAsync(confirmed: true)).Should().BeTrue();

        InProgress(repository).Should().BeFalse();
        repository.Head().Should().Be(head);
    }

    [AvaloniaTest]
    public async Task Delete_versus_modify_can_keep_the_file()
    {
        using TestRepository repository = new();
        repository.AddDeleteVersusModifyBranches();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        await viewModel.Operation.MergeAsync("topic");
        await SelectConflictAsync(viewModel);

        viewModel.Resolver.Kind.Should().Be(ConflictKind.DeletedByThem);
        viewModel.Resolver.IsDeleteConflict.Should().BeTrue();
        viewModel.Resolver.CanChooseSides.Should().BeFalse();

        (await viewModel.Resolver.KeepFileAsync()).Should().BeTrue();
        (await viewModel.Operation.ContinueAsync()).Should().BeTrue();

        repository.WorkFile("conflict.txt").Should().Be("one\nmain version\nthree\n");
        repository.Parents().Should().HaveCount(2);
    }

    [AvaloniaTest]
    public async Task Delete_versus_modify_can_delete_the_file()
    {
        using TestRepository repository = new();
        repository.AddDeleteVersusModifyBranches();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        await viewModel.Operation.MergeAsync("topic");
        await SelectConflictAsync(viewModel);

        (await viewModel.Resolver.DeleteFileAsync()).Should().BeTrue();
        (await viewModel.Operation.ContinueAsync()).Should().BeTrue();

        File.Exists(Path.Combine(repository.Path, "conflict.txt")).Should().BeFalse();
        repository.Parents().Should().HaveCount(2);
    }

    [AvaloniaTest]
    public async Task Delete_versus_modify_deleted_by_us_keeps_their_version()
    {
        using TestRepository repository = new();
        repository.AddDeleteVersusModifyBranches();
        repository.RunGit("checkout", "--quiet", "topic");
        MainWindowViewModel viewModel = await OpenAsync(repository);
        await viewModel.Operation.MergeAsync("main");

        // Git lists this conflict twice (staged "D" and unmerged "U"); selecting the staged entry opens the resolver too.
        ChangedFileViewModel staged = viewModel.ChangedFiles.First(c => c.Name == "conflict.txt" && !c.IsConflicted);
        viewModel.SelectedChangedFile = staged;
        await viewModel.Resolver.ShowAsync(staged, viewModel.ChangedFiles);
        viewModel.Resolver.IsActive.Should().BeTrue();

        viewModel.Resolver.Kind.Should().Be(ConflictKind.DeletedByUs);
        (await viewModel.Resolver.KeepFileAsync()).Should().BeTrue();
        (await viewModel.Operation.ContinueAsync()).Should().BeTrue();

        repository.WorkFile("conflict.txt").Should().Be("one\nmain version\nthree\n");
    }

    [AvaloniaTest]
    public void Parse_classifies_marker_and_side_lines()
    {
        ConflictLine[] lines = ConflictResolverViewModel.Parse("a\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> topic\nz\n").ToArray();

        lines.Select(l => l.Kind).Should().Equal(
            ConflictLineKind.Normal,
            ConflictLineKind.Marker,
            ConflictLineKind.Ours,
            ConflictLineKind.Marker,
            ConflictLineKind.Theirs,
            ConflictLineKind.Marker,
            ConflictLineKind.Normal);
    }

    [AvaloniaTest]
    public async Task Branch_context_menu_offers_merge_and_rebase_and_runs_them()
    {
        using TestRepository repository = new();
        repository.AddCleanDivergedBranches();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        TreeView tree = window.GetVisualDescendants().OfType<TreeView>().First();
        StackPanel row = tree.GetVisualDescendants().OfType<StackPanel>()
            .First(p => p.ContextMenu is not null && p.DataContext is RefNodeViewModel { Name: "topic" });
        ContextMenu menu = row.ContextMenu!;
        menu.Open(row);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        MenuItem[] items = menu.Items.OfType<MenuItem>().ToArray();
        MenuItem merge = items.Single(i => (string?)i.Header == "Merge into current branch");
        MenuItem rebase = items.Single(i => (string?)i.Header == "Rebase current branch onto this");
        merge.IsEnabled.Should().BeTrue();
        rebase.IsEnabled.Should().BeTrue();
        rebase.Command.Should().NotBeNull();
        merge.Items.OfType<MenuItem>().Should().HaveCount(3).And.OnlyContain(i => i.Command != null);

        MenuItem noFf = merge.Items.OfType<MenuItem>().Single(i => ((string)i.Header!).Contains("--no-ff", StringComparison.Ordinal));
        noFf.Command!.Execute(noFf.CommandParameter);
        for (int i = 0; i < 200 && repository.Parents().Length < 2; i++)
        {
            await Task.Delay(50);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        repository.Parents().Should().HaveCount(2);
        menu.Close();
        window.Close();
    }

    [AvaloniaTest]
    public async Task Current_branch_cannot_be_merged_into_itself()
    {
        using TestRepository repository = new();
        repository.AddCleanDivergedBranches();
        MainWindowViewModel viewModel = await OpenAsync(repository);

        RefNodeViewModel main = Flatten(viewModel.Refs.Roots).Single(n => n.Kind == RefNodeKind.LocalBranch && n.Name == "main");
        RefNodeViewModel topic = Flatten(viewModel.Refs.Roots).Single(n => n.Kind == RefNodeKind.LocalBranch && n.Name == "topic");
        main.CanMergeOrRebase.Should().BeFalse();
        topic.CanMergeOrRebase.Should().BeTrue();
    }

    private static IEnumerable<RefNodeViewModel> Flatten(IEnumerable<RefNodeViewModel> nodes)
        => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    [AvaloniaTest]
    public async Task Screenshots_of_the_operation_bar_and_resolver_in_both_themes()
    {
        using TestRepository repository = new();
        repository.AddConflictingBranches();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        await viewModel.Operation.MergeAsync("topic");
        MainWindow window = new() { DataContext = viewModel, Width = 1200, Height = 720 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // The resolver (like the changes list) is shown by the Commit window; a minimal host stands in for it here.
        Window host = ShellTestSupport.CreateChangesHost(viewModel, 1200, 720);
        host.Find<ListBox>("ChangeList").SelectedItem = viewModel.ChangedFiles.First(c => c.IsConflicted);
        await viewModel.Resolver.ShowAsync(viewModel.SelectedChangedFile);
        ShellTestSupport.Settle();

        window.FindControl<OperationBarView>("OperationBar")!.IsVisible.Should().BeTrue();
        host.Find<ConflictResolverView>("ResolverView").IsVisible.Should().BeTrue();

        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        foreach ((ThemeVariant variant, string name) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
        {
            window.RequestedThemeVariant = variant;
            host.RequestedThemeVariant = variant;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            string file = Path.Combine(directory, $"merge-conflict-{name}.png");
            window.CaptureRenderedFrame()!.Save(file);
            TestContext.AddTestAttachment(file);
            string resolverFile = Path.Combine(directory, $"merge-conflict-resolver-{name}.png");
            host.CaptureRenderedFrame()!.Save(resolverFile);
            TestContext.AddTestAttachment(resolverFile);
        }

        // With the abort confirmation open.
        viewModel.Operation.RequestAbortCommand.Execute(null);
        window.RequestedThemeVariant = ThemeVariant.Dark;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.CaptureRenderedFrame()!.Save(Path.Combine(directory, "merge-conflict-abort-dark.png"));
        host.Close();
        window.Close();
    }
}
