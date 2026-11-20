using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GitCommands;
using GitCommands.Logging;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.Revision;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class RevisionTabsTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string because = "the condition should become true in time")
    {
        for (int i = 0; i < 400 && !condition(); i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        condition().Should().BeTrue(because);
    }

    // Background loads that outlive a test would run into the next one.
    private static Task QuiesceAsync(MainWindowViewModel main)
        => WaitUntilAsync(() => !main.CommitDetails.IsLoading && !main.CommitDetails.IsLoadingExtras && !main.RevisionTabs.IsBusy);

    private static void Settle()
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static GitModule Module(string path) => GitEnvironment.Initialize().OpenRepository(path)!;

    private static async Task<MainWindowViewModel> OpenAsync(string path)
    {
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(path);
        _ = main.RevisionTabs;
        return main;
    }

    private static CommitItemViewModel Commit(MainWindowViewModel main, string subject) => main.Commits.Single(c => c.Subject == subject);

    // ---- Commit tab -------------------------------------------------------------------------------------------------------

    [AvaloniaTest]
    public async Task Commit_tab_lists_containing_branches_tags_and_the_nearest_tag()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);

        main.SelectedCommit = Commit(main, "Add c and src");
        CommitDetailsViewModel details = main.CommitDetails;
        await WaitUntilAsync(() => details.HasCommit && details.DescribeSuffix.Length > 0);

        details.BranchesText.Should().StartWith("Contained in branches:").And.Contain("main").And.Contain("feature");
        details.TagsText.Should().Be("Contained in no tag");
        details.DescribeTag.Should().Be("v1.0");
        details.DescribeSuffix.Should().Be(" + 2 commits");
        details.DescribeTagCommit.Should().NotBeNull();
        details.DescribeTagCommit!.ToString().Should().Be(repository.Sha("v1.0"));

        main.SelectedCommit = Commit(main, "Feature work");
        await WaitUntilAsync(() => details.Hash == main.SelectedCommit.ObjectId.ToString() && details.BranchesText.StartsWith("Contained in branches", StringComparison.Ordinal));
        details.BranchesText.Should().Be("Contained in branches: feature, main");

        await QuiesceAsync(main);
    }

    [AvaloniaTest]
    public async Task Commit_tab_shows_tags_containing_the_commit()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);

        main.SelectedCommit = main.Commits.First(c => c.ObjectId.ToString() == repository.Sha("v1.0"));
        await WaitUntilAsync(() => main.CommitDetails.TagsText.StartsWith("Contained in tags", StringComparison.Ordinal) && main.CommitDetails.DescribeSuffix.Length > 0);

        main.CommitDetails.TagsText.Should().Be("Contained in tags: v1.0");
        main.CommitDetails.DescribeSuffix.Should().Be(" + 0 commits");

        await QuiesceAsync(main);
    }

    [AvaloniaTest]
    public async Task Commit_tab_has_dates_parent_and_child_links_and_related_links()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);

        main.SelectedCommit = Commit(main, "Merge feature");
        CommitDetailsViewModel details = main.CommitDetails;
        await WaitUntilAsync(() => details.HasCommit && details.Hash == main.SelectedCommit.ObjectId.ToString());

        details.ParentLinks.Select(l => l.Id.ToString()).Should().Equal(repository.Sha("HEAD~1^1"), repository.Sha("HEAD~1^2"));
        details.ParentsLabel.Should().Be("Parents:");
        details.ChildLinks.Select(l => l.Id.ToString()).Should().Equal(repository.Sha("HEAD"));
        details.ChildrenLabel.Should().Be("Child:");
        details.AuthorName.Should().Be("Test Author");
        details.AuthorMailto.Should().Be("mailto:test@example.com");
        details.Initials.Should().Be("TA");
        details.AuthorDateText.Should().Contain("ago (");
        details.CommitDateText.Should().Contain("ago (");

        // Links of the message (the remote is a GitHub address): issue references and URLs.
        main.SelectedCommit = Commit(main, "Rework files");
        await WaitUntilAsync(() => details.RelatedLinks.Any(l => l.Text == "Issue 42"));
        details.RelatedLinks.Select(l => (l.Text, l.Url)).Should().Contain(("Issue 42", "https://github.com/octo/repo/issues/42"))
            .And.Contain(("https://example.com/spec", "https://example.com/spec"))
            .And.Contain(l => l.Text == "View on GitHub" && l.Url.StartsWith("https://github.com/octo/repo/commit/", StringComparison.Ordinal));
        details.HasRelatedLinks.Should().BeTrue();

        await QuiesceAsync(main);
    }

    [AvaloniaTest]
    public async Task Selecting_a_parent_link_selects_that_commit_in_the_grid()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);
        main.SelectedCommit = Commit(main, "Merge feature");
        await WaitUntilAsync(() => main.CommitDetails.ParentLinks.Count == 2);

        main.SelectCommit(main.CommitDetails.ParentLinks[1].Id);

        main.SelectedCommit!.Subject.Should().Be("Feature work");

        await QuiesceAsync(main);
    }

    // ---- Diff tab ---------------------------------------------------------------------------------------------------------

    [AvaloniaTest]
    public async Task Diff_tab_lists_modify_add_delete_and_rename_and_shows_the_first_diff()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);

        main.SelectedCommit = Commit(main, "Rework files");
        RevisionDiffViewModel diff = main.RevisionTabs.Diff;
        await WaitUntilAsync(() => diff.Files.Count == 5 && diff.Lines.Count > 0);

        diff.Files.Select(f => (f.DisplayName, f.Status)).Should().BeEquivalentTo(
            [("a.txt", "M"), ("bin.dat", "A"), ("c.txt → cc.txt", "R"), ("g.txt", "D"), ("large.txt", "A")]);
        diff.TabHeader.Should().Be("Diff (5)");
        diff.SelectedFile.Should().BeSameAs(diff.Files[0]);
        diff.Lines.Should().Contain(l => l.Kind == DiffLineKind.Added && l.Text == "+rewritten");
        diff.Lines.Should().Contain(l => l.Kind == DiffLineKind.Removed);
        diff.Notice.Should().BeEmpty();

        diff.SelectedFile = diff.Files.Single(f => f.Status == "D");
        await WaitUntilAsync(() => diff.Lines.Any(l => l.Kind == DiffLineKind.Removed));
        diff.Lines.Should().NotContain(l => l.Kind == DiffLineKind.Added);

        diff.SelectedFile = diff.Files.Single(f => f.Status == "R");
        await WaitUntilAsync(() => diff.Lines.Any(l => l.Text.Contains("rename", StringComparison.Ordinal)));

        diff.SelectedFile = diff.Files.Single(f => f.Name == "bin.dat");
        await WaitUntilAsync(() => diff.Lines.Any(l => l.Text == "Binary file differs."));

        await QuiesceAsync(main);
    }

    [AvaloniaTest]
    public async Task Diff_tab_filters_files_and_keeps_a_selection()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);
        main.SelectedCommit = Commit(main, "Rework files");
        RevisionDiffViewModel diff = main.RevisionTabs.Diff;
        await WaitUntilAsync(() => diff.Files.Count == 5);

        diff.FilterText = "cc.";

        diff.Files.Select(f => f.Name).Should().Equal("cc.txt");
        diff.SelectedFile!.Name.Should().Be("cc.txt");
        await WaitUntilAsync(() => diff.Lines.Count > 0);
        diff.FilterText = string.Empty;
        diff.Files.Should().HaveCount(5);
        await WaitUntilAsync(() => diff.Lines.Count > 0);

        await QuiesceAsync(main);
    }

    [AvaloniaTest]
    public async Task Diff_tab_groups_the_files_as_a_tree_by_path_or_extension()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);
        main.SelectedCommit = Commit(main, "Rework files");
        RevisionDiffViewModel diff = main.RevisionTabs.Diff;
        await WaitUntilAsync(() => diff.Files.Count == 5);
        diff.Tree.Should().BeEmpty("the tests use the flat list");

        try
        {
            diff.GroupByPath = true;
            diff.ShowTree.Should().BeTrue();
            ChangeTreeNode header = diff.Tree.Single();
            header.PlainLabel.Should().StartWith("(5) Diff with A ").And.EndWith(": Rework files");
            header.Files().Should().HaveCount(5);

            diff.GroupByExtension = true;
            diff.Tree.Single().Children.Select(n => n.PlainLabel).Should().Equal(".dat", ".txt");

            diff.FilterText = "cc.";
            diff.Tree.Single().PlainLabel.Should().StartWith("(1) ");
        }
        finally
        {
            diff.GroupByNone = true;
        }

        diff.Tree.Should().BeEmpty();
        await QuiesceAsync(main);
    }

    [AvaloniaTest]
    public async Task Diff_tab_diffs_a_merge_commit_against_its_first_parent_with_a_notice()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);

        main.SelectedCommit = Commit(main, "Merge feature");
        RevisionDiffViewModel diff = main.RevisionTabs.Diff;
        await WaitUntilAsync(() => diff.Files.Count == 1 && diff.Lines.Count > 0);

        diff.Notice.Should().Contain("Merge commit").And.Contain("first parent");
        diff.Files.Single().Name.Should().Be("f.txt");
        diff.Files.Single().Status.Should().Be("A");

        await QuiesceAsync(main);
    }

    [AvaloniaTest]
    public async Task Diff_tab_handles_the_root_commit()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);

        main.SelectedCommit = main.Commits[^1];
        RevisionDiffViewModel diff = main.RevisionTabs.Diff;
        await WaitUntilAsync(() => diff.Files.Count == 1 && diff.Lines.Count > 0);

        diff.Files[0].Name.Should().Be("a.txt");
        diff.Files[0].Status.Should().Be("A");
        diff.Lines.Should().Contain(l => l.Kind == DiffLineKind.Added && l.Text == "+first");

        await QuiesceAsync(main);
    }

    [AvaloniaTest]
    public async Task Diff_tab_can_diff_two_revisions()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);
        GitModule module = Module(repository.Path);

        main.RevisionTabs.ShowDiff(module, GitExtensions.Extensibility.Git.ObjectId.Parse(repository.Sha("v1.0")), GitExtensions.Extensibility.Git.ObjectId.Parse(repository.Sha("HEAD")));
        RevisionDiffViewModel diff = main.RevisionTabs.Diff;
        await WaitUntilAsync(() => diff.Files.Count > 0 && diff.Lines.Count > 0);

        main.RevisionTabs.SelectedTabIndex.Should().Be(RevisionTabsViewModel.DiffTab);
        diff.Files.Select(f => f.Name).Should().Contain(["a.txt", "bin.dat", "f.txt", "cc.txt"]);
        diff.Files.Should().NotContain(f => f.Name == "b.txt" && f.Status != "D");
        diff.Title.Should().Contain("↔");

        await QuiesceAsync(main);
    }

    [AvaloniaTest]
    public async Task Diff_tab_shows_the_working_tree_changes_for_the_uncommitted_row()
    {
        using RevisionTabsRepository repository = new();
        File.WriteAllText(Path.Combine(repository.Path, "a.txt"), "dirty\n");
        File.WriteAllText(Path.Combine(repository.Path, "untracked.txt"), "new\n");
        MainWindowViewModel main = await OpenAsync(repository.Path);

        main.IsWorkingTreeSelected = true;
        RevisionDiffViewModel diff = main.RevisionTabs.Diff;
        await WaitUntilAsync(() => diff.Files.Count == 2 && diff.Lines.Count > 0);

        main.RevisionTabs.SelectedTabIndex.Should().Be(RevisionTabsViewModel.DiffTab);
        main.RevisionTabs.ShowCommitTab.Should().BeFalse();
        main.RevisionTabs.ShowFileTreeTab.Should().BeFalse();
        diff.Revision.Should().BeNull();
        diff.Files.Select(f => f.Name).Should().BeEquivalentTo("a.txt", "untracked.txt");
        diff.Lines.Should().Contain(l => l.Text == "+dirty");

        main.IsWorkingTreeSelected = false;
        main.SelectedCommit = Commit(main, "Rework files");
        main.RevisionTabs.ShowCommitTab.Should().BeTrue();
        await WaitUntilAsync(() => diff.Files.Count == 5);

        await QuiesceAsync(main);
    }

    // ---- File tree tab ----------------------------------------------------------------------------------------------------

    [AvaloniaTest]
    public async Task File_tree_lists_the_revision_folders_first_and_expands_lazily()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);
        main.RevisionTabs.SelectedTabIndex = RevisionTabsViewModel.FileTreeTab;
        RevisionFileTreeViewModel tree = main.RevisionTabs.FileTree;

        main.SelectedCommit = Commit(main, "Rework files");
        await WaitUntilAsync(() => tree.Roots.Count > 0);

        tree.Roots.Select(n => n.Name).Should().Equal("src", "a.txt", "bin.dat", "cc.txt", "f.txt", "large.txt");
        FileTreeNode src = tree.Roots[0];
        src.IsFolder.Should().BeTrue();
        src.Children.Should().ContainSingle(c => c.IsPlaceholder);
        src.IsExpanded = true;
        src.Children.Select(c => c.Name).Should().Equal("deep", "inner.txt");
        tree.Roots.Should().NotContain(n => n.Name == "g.txt");

        await QuiesceAsync(main);
    }

    [AvaloniaTest]
    public async Task File_tree_keeps_the_open_file_when_another_commit_is_selected()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);
        main.RevisionTabs.SelectedTabIndex = RevisionTabsViewModel.FileTreeTab;
        RevisionFileTreeViewModel tree = main.RevisionTabs.FileTree;

        main.SelectedCommit = Commit(main, "Rework files");
        await WaitUntilAsync(() => tree.Roots.Count > 0);
        tree.SelectedNode = tree.Roots.Single(n => n.Name == "a.txt");
        await WaitUntilAsync(() => tree.Lines.Count == 1);
        tree.Lines[0].Text.Should().Be("rewritten");

        main.SelectedCommit = Commit(main, "Merge feature");
        await WaitUntilAsync(() => tree.SelectedNode is { FullPath: "a.txt" } && tree.Lines.Count == 1 && tree.Lines[0].Text != "rewritten");

        // A file that does not exist in the other commit says so instead of silently closing.
        main.SelectedCommit = Commit(main, "Rework files");
        await WaitUntilAsync(() => tree.Roots.Count > 0);
        tree.SelectedNode = tree.Roots.Single(n => n.Name == "cc.txt");
        await WaitUntilAsync(() => tree.Lines.Count > 0);
        main.SelectedCommit = Commit(main, "Merge feature");
        await WaitUntilAsync(() => tree.ContentMessage.Contains("does not exist", StringComparison.Ordinal));

        await QuiesceAsync(main);
    }

    [AvaloniaTest]
    public async Task File_tree_only_lists_when_the_tab_is_active()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);
        RevisionFileTreeViewModel tree = main.RevisionTabs.FileTree;

        main.SelectedCommit = Commit(main, "Rework files");
        await Task.Delay(300);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        tree.Roots.Should().BeEmpty("the Commit tab is shown, so the listing is not needed yet");

        main.RevisionTabs.SelectedTabIndex = RevisionTabsViewModel.FileTreeTab;
        await WaitUntilAsync(() => tree.Roots.Count > 0);

        await QuiesceAsync(main);
    }

    [AvaloniaTest]
    public async Task File_tree_shows_text_binary_and_large_files()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);
        RevisionFileTreeViewModel tree = main.RevisionTabs.FileTree;
        await tree.ShowRevisionAsync(Module(repository.Path), repository.Sha("HEAD"));
        await WaitUntilAsync(() => tree.Roots.Count > 0);

        tree.SelectedNode = tree.Roots.Single(n => n.Name == "a.txt");
        await WaitUntilAsync(() => tree.Lines.Count == 1);
        tree.Lines[0].Should().Be(new ContentLine(1, "rewritten"));
        tree.ContentMessage.Should().BeEmpty();

        tree.SelectedNode = tree.Roots.Single(n => n.Name == "bin.dat");
        await WaitUntilAsync(() => tree.ContentMessage == "Binary file");
        tree.Lines.Should().BeEmpty();

        tree.SelectedNode = tree.Roots.Single(n => n.Name == "large.txt");
        await WaitUntilAsync(() => tree.ContentMessage.Contains("too large", StringComparison.Ordinal));
        tree.Lines.Should().BeEmpty();

        await QuiesceAsync(main);
    }

    [AvaloniaTest]
    public async Task File_tree_filter_flattens_matches_and_restores_the_tree()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);
        RevisionFileTreeViewModel tree = main.RevisionTabs.FileTree;
        await tree.ShowRevisionAsync(Module(repository.Path), repository.Sha("HEAD"));
        await WaitUntilAsync(() => tree.Roots.Count > 0);

        tree.FilterText = "x.txt";

        tree.Roots.Select(n => n.Name).Should().Equal("src");
        tree.Roots[0].IsExpanded.Should().BeTrue();
        tree.Roots[0].Children[0].Name.Should().Be("deep");
        tree.Roots[0].Children[0].Children.Single().FullPath.Should().Be("src/deep/x.txt");

        tree.FilterText = "nothing-matches";
        tree.Roots.Should().BeEmpty();
        tree.Message.Should().Contain("No file matches");

        tree.FilterText = string.Empty;
        tree.Roots.Should().HaveCount(6);

        await QuiesceAsync(main);
    }

    // ---- Console tab ------------------------------------------------------------------------------------------------------

    [AvaloniaTest]
    public async Task Console_log_captures_real_git_calls_with_exit_codes()
    {
        using RevisionTabsRepository repository = new();
        GitModule module = Module(repository.Path);
        using CommandLogService log = new();

        _ = RevisionGit.GetTagCommit(module, "v1.0", CancellationToken.None);
        _ = RevisionGit.GetTagCommit(module, "no-such-tag-xyz", CancellationToken.None);
        await WaitUntilAsync(() => log.Items.Any(i => i.Text.Contains("no-such-tag-xyz", StringComparison.Ordinal) && i.Entry.ExitCode is not null));

        CommandLogItem ok = log.Items.Last(i => i.Text.Contains("refs/tags/v1.0", StringComparison.Ordinal));
        ok.IsError.Should().BeFalse();
        ok.Text.Should().Contain("git rev-parse --verify --quiet").And.Contain("exit 0").And.Contain("ms").And.Contain(repository.Path);
        CommandLogItem failed = log.Items.Last(i => i.Text.Contains("no-such-tag-xyz", StringComparison.Ordinal));
        failed.IsError.Should().BeTrue();
        failed.Text.Should().Contain("exit 1");
        log.GetText().Should().Contain("no-such-tag-xyz");

        log.Clear();
        log.Items.Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Console_log_is_bounded()
    {
        using CommandLogService log = new();
        log.Clear();

        for (int i = 0; i < CommandLogService.MaxEntries + 200; i++)
        {
            CommandLog.LogProcessStart("git", $"bounded-{i}", "/tmp").LogProcessEnd(0);
        }

        await WaitUntilAsync(() => log.Items.Any(i => i.Text.Contains($"bounded-{CommandLogService.MaxEntries + 199}", StringComparison.Ordinal)));
        log.Items.Count.Should().BeLessThanOrEqualTo(CommandLogService.MaxEntries);
        log.Items.Should().NotContain(i => i.Text.Contains("bounded-0 ", StringComparison.Ordinal));
        log.Clear();
    }

    [AvaloniaTest]
    public async Task Console_log_updates_a_running_entry_in_place()
    {
        using CommandLogService log = new();
        ProcessOperation operation = CommandLog.LogProcessStart("git", "slow-command-marker", "/tmp");
        await WaitUntilAsync(() => log.Items.Any(i => i.Text.Contains("slow-command-marker", StringComparison.Ordinal)));
        CommandLogItem item = log.Items.Last(i => i.Text.Contains("slow-command-marker", StringComparison.Ordinal));
        item.Text.Should().Contain("running");

        operation.LogProcessEnd(128);
        await WaitUntilAsync(() => item.IsError);

        item.Text.Should().Contain("exit 128");
        log.Items.Count(i => i.Text.Contains("slow-command-marker", StringComparison.Ordinal)).Should().Be(1);
    }

    // ---- Tab control ------------------------------------------------------------------------------------------------------

    [AvaloniaTest]
    public async Task Tab_control_has_the_original_tabs_and_hides_the_build_report()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);
        Window window = new() { Width = 1200, Height = 720, DataContext = main, Content = new RevisionTabsView { DataContext = main } };
        window.Show();
        Settle();

        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
        tabs.Items.OfType<TabItem>().Select(t => t.IsVisible).Should().Equal(true, true, true, true, false);
        window.GetVisualDescendants().OfType<TabItem>().Single(t => t.Name == "BuildReportTabItem").IsVisible.Should().BeFalse();

        main.IsWorkingTreeSelected = true;
        Settle();
        tabs.Items.OfType<TabItem>().Select(t => t.IsVisible).Should().Equal(false, true, false, true, false);
        tabs.SelectedIndex.Should().Be(RevisionTabsViewModel.DiffTab);
        window.Close();

        await QuiesceAsync(main);
    }

    // ---- Screenshots ------------------------------------------------------------------------------------------------------

    [AvaloniaTest]
    public async Task Tabs_render_in_light_and_dark()
    {
        using RevisionTabsRepository repository = new();
        MainWindowViewModel main = await OpenAsync(repository.Path);
        main.SelectedCommit = Commit(main, "Rework files");
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);

        // Something in the console, including a failure.
        _ = RevisionGit.GetTagCommit(Module(repository.Path), "no-such-tag-xyz", CancellationToken.None);

        foreach ((ThemeVariant variant, string name) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
        {
            RevisionTabsView view = new() { DataContext = main };
            Window window = new() { Width = 1200, Height = 720, RequestedThemeVariant = variant, Content = view };
            window.Show();
            string[] tabNames = ["commit", "diff", "filetree", "console"];
            for (int index = 0; index < tabNames.Length; index++)
            {
                main.RevisionTabs.SelectedTabIndex = index;
                await WaitUntilAsync(() => main.CommitDetails.BranchesText != "(…)" && main.RevisionTabs.Diff.Lines.Count > 0
                    && (index != RevisionTabsViewModel.FileTreeTab || main.RevisionTabs.FileTree.Roots.Count > 0));
                if (index == RevisionTabsViewModel.FileTreeTab)
                {
                    main.RevisionTabs.FileTree.SelectedNode = main.RevisionTabs.FileTree.Roots.Single(n => n.Name == "a.txt");
                    await WaitUntilAsync(() => main.RevisionTabs.FileTree.Lines.Count > 0);
                }

                Settle();
                string file = Path.Combine(directory, $"revision-tabs-{tabNames[index]}-{name}.png");
                WriteableBitmap? frame = window.CaptureRenderedFrame();
                frame.Should().NotBeNull();
                frame!.Save(file);
                TestContext.AddTestAttachment(file);
            }

            window.Close();
        }

        await QuiesceAsync(main);
    }
}
