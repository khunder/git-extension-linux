using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExtensions.Desktop.History;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class RevisionGridTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private string _directory = null!;
    private GridColumnStore _previousStore = null!;

    [SetUp]
    public void Isolate()
    {
        _directory = Directory.CreateTempSubdirectory("ge-grid-").FullName;
        _previousStore = GridColumnStore.Current;
        GridColumnStore.Current = new GridColumnStore(Path.Combine(_directory, "columns.json"));
    }

    [TearDown]
    public void Restore()
    {
        GridColumnStore.Current = _previousStore;
        Directory.Delete(_directory, recursive: true);
    }

    // ---- pure pieces ----

    [TestCase(0, "just now")]
    [TestCase(-30, "just now")]
    [TestCase(59, "just now")]
    [TestCase(60, "1 minute ago")]
    [TestCase(150, "2 minutes ago")]
    [TestCase(3599, "59 minutes ago")]
    [TestCase(3600, "1 hour ago")]
    [TestCase(14 * 3600, "14 hours ago")]
    [TestCase((24 * 3600) - 1, "23 hours ago")]
    [TestCase(24 * 3600, "1 day ago")]
    [TestCase(15 * 24 * 3600, "15 days ago")]
    [TestCase(29 * 24 * 3600, "29 days ago")]
    [TestCase(30 * 24 * 3600, "1 month ago")]
    [TestCase(100 * 24 * 3600, "3 months ago")]
    [TestCase(364 * 24 * 3600, "12 months ago")]
    [TestCase(365 * 24 * 3600, "1 year ago")]
    [TestCase(800 * 24 * 3600, "2 years ago")]
    public void RelativeDateFormatter_table(int ageSeconds, string expected)
    {
        DateTimeOffset now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        RelativeDateFormatter.Format(now.AddSeconds(-ageSeconds), now).Should().Be(expected);
    }

    [AvaloniaTest]
    public void Badges_put_the_current_branch_first_then_local_remote_and_tags()
    {
        IReadOnlyList<RefBadge> badges = RefBadgeBuilder.Arrange(
        [
            new RefBadge(RefBadgeKind.Tag, "v1", false),
            new RefBadge(RefBadgeKind.RemoteBranch, "origin/main", false),
            new RefBadge(RefBadgeKind.LocalBranch, "zeta", false),
            new RefBadge(RefBadgeKind.LocalBranch, "main", true),
        ],
        maxVisible: 4);
        badges.Select(b => b.Name).Should().Equal("main", "zeta", "origin/main", "v1");
        badges[0].IsCurrent.Should().BeTrue();
        badges.Should().NotContain(b => b.IsOverflow);
    }

    [AvaloniaTest]
    public void Many_badges_collapse_into_an_overflow_chip_listing_everything()
    {
        RefBadge[] all = Enumerable.Range(1, 6).Select(i => new RefBadge(RefBadgeKind.LocalBranch, "b" + i, false)).ToArray();
        IReadOnlyList<RefBadge> badges = RefBadgeBuilder.Arrange(all, maxVisible: 3);
        badges.Should().HaveCount(4);
        badges[3].IsOverflow.Should().BeTrue();
        badges[3].Text.Should().Be("…");
        badges[3].ToolTip.Should().Contain("b1").And.Contain("b6");
    }

    [AvaloniaTest]
    public void Avatar_initials_and_colour_derive_from_the_name()
    {
        AvatarStyle.Initials("Philippe Miossec").Should().Be("PM");
        AvatarStyle.Initials("RussKie").Should().Be("R");
        AvatarStyle.Initials("  ").Should().Be("?");
        AvatarStyle.Initials("jan-de.vries").Should().Be("JV");
        AvatarStyle.ColorIndex("Alice").Should().Be(AvatarStyle.ColorIndex("alice"));
        AvatarStyle.ColorIndex("Alice").Should().BeInRange(0, AvatarStyle.ColorCount - 1);
    }

    [AvaloniaTest]
    public void Filter_maps_to_git_arguments()
    {
        RevisionFilterArgs.Build(new RevisionFilter(), 100).Should().Be("--topo-order --max-count=100 --branches --remotes --tags HEAD");
        RevisionFilterArgs.Build(new RevisionFilter { ShowRemoteBranches = false, ShowTags = false }, 5).Should().Be("--topo-order --max-count=5 --branches HEAD");
        RevisionFilterArgs.Build(new RevisionFilter { Branches = BranchFilterMode.CurrentBranch }, 5).Should().Be("--topo-order --max-count=5 HEAD");
        RevisionFilterArgs.Build(new RevisionFilter { Branches = BranchFilterMode.SpecificBranch, Branch = "feature/x" }, 5).Should().Be("--topo-order --max-count=5 \"refs/heads/feature/x\"");
        RevisionFilterArgs.Build(new RevisionFilter { Branches = BranchFilterMode.CurrentBranch, ShowMergeCommits = false }, 5).Should().Be("--topo-order --max-count=5 --no-merges HEAD");
        RevisionFilterArgs.Build(new RevisionFilter { Branches = BranchFilterMode.CurrentBranch, Text = "fix \"it\"" }, 5)
            .Should().Be("--topo-order --max-count=5 -i --fixed-strings --all-match --grep=\"fix \\\"it\\\"\" HEAD");
        RevisionFilterArgs.Build(new RevisionFilter { Branches = BranchFilterMode.CurrentBranch, Text = "ann", TextMode = TextFilterMode.Author }, 5)
            .Should().Be("--topo-order --max-count=5 -i --fixed-strings --author=\"ann\" HEAD");
        RevisionFilterArgs.Build(new RevisionFilter { Branches = BranchFilterMode.CurrentBranch, Text = "bob", TextMode = TextFilterMode.Committer }, 5)
            .Should().Contain("--committer=\"bob\"");
        RevisionFilterArgs.Build(new RevisionFilter { Branches = BranchFilterMode.CurrentBranch, Text = "foo(", TextMode = TextFilterMode.DiffContent }, 5)
            .Should().Contain("-G\"foo(\"");
        RevisionFilterArgs.Build(new RevisionFilter { Text = "abc1234", TextMode = TextFilterMode.HashOrRef }, 5)
            .Should().Be("--topo-order --max-count=5 --no-walk=unsorted abc1234");
        RevisionFilterArgs.Build(new RevisionFilter { Text = "--output=x", TextMode = TextFilterMode.HashOrRef }, 5)
            .Should().Contain("--max-count=0");
        new RevisionFilter().IsFiltering.Should().BeFalse();
        new RevisionFilter { Text = "x" }.IsFiltering.Should().BeTrue();
        new RevisionFilter { Branches = BranchFilterMode.CurrentBranch }.IsFiltering.Should().BeFalse();
        new RevisionFilter { Branches = BranchFilterMode.SpecificBranch, Branch = "x" }.IsFiltering.Should().BeTrue();
    }

    [AvaloniaTest]
    public void Column_store_round_trips_clamps_and_survives_bad_files()
    {
        GridColumnStore store = new(Path.Combine(_directory, "sub", "cols.json"));
        GridColumnSettings settings = new(40, 200, 90, 70, 30);
        store.Save(settings);
        store.Load().Should().Be(settings);

        File.WriteAllText(store.FilePath, "{\"AuthorWidth\": 99999, \"DateWidth\": -5, \"HashWidth\": 1e300}");
        GridColumnSettings loaded = store.Load();
        loaded.AuthorWidth.Should().Be(GridColumnSettings.MaxWidth);
        loaded.DateWidth.Should().Be(GridColumnSettings.MinWidth);
        loaded.AvatarWidth.Should().Be(new GridColumnSettings().AvatarWidth);

        File.WriteAllText(store.FilePath, "{ nope");
        store.Load().Should().Be(new GridColumnSettings());
        new GridColumnStore(Path.Combine(_directory, "missing.json")).Load().Should().Be(new GridColumnSettings());
    }

    [AvaloniaTest]
    public void Resizing_a_column_clamps_the_width()
    {
        RevisionGridColumns columns = new();
        double before = columns.AuthorWidth;
        columns.Resize("Author", -30);
        columns.AuthorWidth.Should().Be(before + 30);
        columns.Resize("Author", 100000);
        columns.AuthorWidth.Should().Be(GridColumnSettings.MinWidth);
        columns.ShowAuthor = false;
        columns.AuthorColumnWidth.Should().Be(0);
        columns.AvatarColumnWidth.Should().Be(0);
    }

    // ---- view-model with real repositories ----

    private static void Commit(TestRepository repository, string file, string message, string author)
    {
        File.WriteAllText(Path.Combine(repository.Path, file), message);
        repository.RunGit("add", file);
        repository.RunGit("-c", "user.name=" + author, "-c", "user.email=a@example.com", "commit", "--quiet", "-m", message);
    }

    // main: first, second (tag v1.0), Update docs, merge of feature/login (Add login form, Fix login bug); plus refs.
    private static TestRepository CreateBranchyRepository()
    {
        TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        repository.RunGit("checkout", "-q", "-b", "feature/login");
        Commit(repository, "login.txt", "Add login form", "Alice Smith");
        Commit(repository, "login2.txt", "Fix login bug", "Alice Smith");
        repository.RunGit("checkout", "-q", "main");
        Commit(repository, "docs.txt", "Update docs", "Bob Jones");
        repository.RunGit("-c", "user.name=Bob Jones", "-c", "user.email=b@example.com", "merge", "--no-ff", "-q", "feature/login", "-m", "Merge feature/login");
        repository.RunGit("update-ref", "refs/remotes/origin/main", "HEAD~1");
        repository.RunGit("config", "user.name", "Alice Smith");
        return repository;
    }

    private static async Task<MainWindowViewModel> OpenViewModelAsync(TestRepository repository)
    {
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker()) { FilterDebounce = TimeSpan.FromMilliseconds(30) };
        await viewModel.OpenAsync(repository.Path);
        return viewModel;
    }

    private static string[] Subjects(MainWindowViewModel viewModel) => viewModel.Commits.Select(c => c.Subject).ToArray();

    [AvaloniaTest]
    public async Task Branch_filter_changes_the_commits_shown()
    {
        using TestRepository repository = CreateBranchyRepository();
        repository.RunGit("checkout", "-q", "develop");
        Commit(repository, "dev.txt", "Develop only", "Carol");
        repository.RunGit("checkout", "-q", "main");
        MainWindowViewModel viewModel = await OpenViewModelAsync(repository);

        viewModel.SelectedBranchChoice.Should().Be(MainWindowViewModel.DefaultBranchChoice);
        viewModel.BranchChoices.Should().Contain([MainWindowViewModel.AllBranchesChoice, MainWindowViewModel.CurrentBranchChoice, "develop", "feature/login", "feature/x", "main"]);
        viewModel.SelectedBranchChoice.Should().Be(MainWindowViewModel.AllBranchesChoice, "the grid opens with all branches, as in the original");
        Subjects(viewModel).Should().Contain("Develop only").And.Contain("Merge feature/login");

        viewModel.SelectedBranchChoice = MainWindowViewModel.CurrentBranchChoice;
        await SettleAsync(viewModel);
        Subjects(viewModel).Should().NotContain("Develop only").And.Contain("Merge feature/login");

        viewModel.SelectedBranchChoice = "develop";
        await SettleAsync(viewModel);
        Subjects(viewModel).Should().Contain("Develop only").And.NotContain("Merge feature/login");

        viewModel.SelectedBranchChoice = "feature/login";
        await SettleAsync(viewModel);
        Subjects(viewModel).Should().Contain("Fix login bug").And.NotContain("Update docs");
    }

    [AvaloniaTest]
    public async Task Message_author_and_merge_filters_show_only_matching_commits()
    {
        using TestRepository repository = CreateBranchyRepository();
        MainWindowViewModel viewModel = await OpenViewModelAsync(repository);
        Subjects(viewModel).Should().HaveCount(6);

        viewModel.FilterText = "LOGIN";
        await SettleAsync(viewModel);
        Subjects(viewModel).Should().BeEquivalentTo("Merge feature/login", "Fix login bug", "Add login form");

        viewModel.FilterMode = TextFilterMode.Author;
        viewModel.FilterText = "bob";
        await SettleAsync(viewModel);
        Subjects(viewModel).Should().BeEquivalentTo("Merge feature/login", "Update docs");

        viewModel.FilterText = "alice";
        await SettleAsync(viewModel);
        Subjects(viewModel).Should().BeEquivalentTo("Fix login bug", "Add login form");

        viewModel.FilterMode = TextFilterMode.DiffContent;
        viewModel.FilterText = "Update docs";
        await SettleAsync(viewModel);
        Subjects(viewModel).Should().Equal("Update docs");

        string hash = viewModel.Commits[0].ObjectId.ToString()[..8];
        viewModel.FilterMode = TextFilterMode.HashOrRef;
        viewModel.FilterText = hash;
        await SettleAsync(viewModel);
        Subjects(viewModel).Should().Equal("Update docs");

        viewModel.FilterText = "does-not-exist";
        await SettleAsync(viewModel);
        viewModel.Commits.Should().BeEmpty();

        viewModel.ClearFilterCommand.Execute(null);
        viewModel.FilterMode = TextFilterMode.CommitMessage;
        await WaitForAsync(() => viewModel.Commits.Count == 6 && !viewModel.IsBusy);
        viewModel.HasFilterText.Should().BeFalse();

        viewModel.ShowMergeCommits = false;
        await WaitForAsync(() => viewModel.Commits.Count == 5 && !viewModel.IsBusy);
        Subjects(viewModel).Should().NotContain("Merge feature/login");
    }

    [AvaloniaTest]
    public async Task Typing_in_the_filter_is_debounced_and_applies_only_the_last_text()
    {
        using TestRepository repository = CreateBranchyRepository();
        MainWindowViewModel viewModel = await OpenViewModelAsync(repository);
        viewModel.FilterDebounce = TimeSpan.FromMilliseconds(150);

        viewModel.FilterText = "d";
        viewModel.FilterText = "do";
        viewModel.FilterText = "docs";
        await Task.Delay(30);
        Dispatcher.UIThread.RunJobs();
        viewModel.Commits.Should().HaveCount(6, "nothing is applied before the debounce time passed");

        await WaitForAsync(() => viewModel.Commits.Count == 1 && !viewModel.IsBusy);
        Subjects(viewModel).Should().Equal("Update docs");
    }

    [AvaloniaTest]
    public async Task Remote_and_tag_toggles_hide_those_badges_and_branch_choice_survives_a_refresh()
    {
        using TestRepository repository = CreateBranchyRepository();
        MainWindowViewModel viewModel = await OpenViewModelAsync(repository);
        CommitItemViewModel second = viewModel.Commits.First(c => c.Subject == "Add b.txt");

        // Five refs point here, so three are shown and the "…" chip lists all of them.
        second.RefBadges.Should().HaveCount(4);
        second.RefBadges[3].IsOverflow.Should().BeTrue();
        second.RefBadges[3].ToolTip.Should().Contain("v1.0").And.Contain("release/2.0").And.Contain("origin/feature/y");

        viewModel.ShowTags = false;
        await WaitForAsync(() => !viewModel.IsBusy && viewModel.Commits.Count > 0);
        CommitItemViewModel withoutTags = viewModel.Commits.First(c => c.Subject == "Add b.txt");
        withoutTags.RefBadges.Should().NotContain(b => b.Kind == RefBadgeKind.Tag);
        withoutTags.RefBadges.Should().HaveCount(3, "no more than three remain, so no overflow chip");

        viewModel.ShowRemoteBranches = false;
        await WaitForAsync(() => !viewModel.IsBusy && viewModel.Commits.Count > 0);
        viewModel.Commits.First(c => c.Subject == "Add b.txt").RefBadges.Should().OnlyContain(b => b.Kind == RefBadgeKind.LocalBranch);

        CommitItemViewModel head = viewModel.Commits[0];
        head.RefBadges.Should().Contain(b => b.Kind == RefBadgeKind.LocalBranch && b.Name == "main" && b.IsCurrent);
        head.IsCurrentHead.Should().BeTrue();

        viewModel.SelectedBranchChoice = "feature/login";
        await SettleAsync(viewModel);
        await viewModel.OpenAsync(repository.Path);
        viewModel.SelectedBranchChoice.Should().Be("feature/login");
        Subjects(viewModel).Should().NotContain("Update docs");
    }

    [AvaloniaTest]
    public async Task Rows_know_merges_authors_and_the_current_user()
    {
        using TestRepository repository = CreateBranchyRepository();
        MainWindowViewModel viewModel = await OpenViewModelAsync(repository);
        viewModel.Commits.Where(c => c.IsMerge).Select(c => c.Subject).Should().Equal("Merge feature/login");
        viewModel.Commits.Where(c => c.IsMine).Select(c => c.Subject).Should().BeEquivalentTo("Fix login bug", "Add login form");
        viewModel.Commits.Select(c => c.IsAlternate).Take(4).Should().Equal(false, true, false, true);
        viewModel.Commits.First(c => c.Subject == "Update docs").Initials.Should().Be("BJ");
        viewModel.Commits[0].RelativeDate.Should().Be("just now");
        viewModel.Commits[0].DateToolTip.Should().NotBeNullOrEmpty();
    }

    // ---- view ----

    private static (Window Window, RevisionGridView Grid) Host(MainWindowViewModel viewModel, double width, double height, ThemeVariant? variant = null)
    {
        RevisionGridView grid = new() { DataContext = viewModel };
        Window window = new() { Content = grid, Width = width, Height = height, RequestedThemeVariant = variant ?? ThemeVariant.Light };
        window.Show();
        Pump();
        return (window, grid);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    // Every filter change schedules a reload; wait until the last one has finished.
    private static async Task SettleAsync(MainWindowViewModel viewModel)
    {
        await Task.Delay(viewModel.FilterDebounce + TimeSpan.FromMilliseconds(60));
        await WaitForAsync(() => !viewModel.IsBusy);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }

        Pump();
        condition().Should().BeTrue("the condition was not met in time");
    }

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        Pump();
    }

    [AvaloniaTest]
    public async Task Selecting_a_row_fills_SelectedCommit_and_clears_the_working_tree_flag()
    {
        using TestRepository repository = CreateBranchyRepository();
        MainWindowViewModel viewModel = await OpenViewModelAsync(repository);
        (Window window, RevisionGridView grid) = Host(viewModel, 1000, 600);

        grid.List.ItemCount.Should().Be(viewModel.Commits.Count);
        grid.List.SelectedIndex = 2;
        Pump();
        viewModel.SelectedCommit.Should().BeSameAs(viewModel.Commits[2]);
        viewModel.IsWorkingTreeSelected.Should().BeFalse();

        viewModel.IsWorkingTreeSelected = true;
        Pump();
        viewModel.SelectedCommit.Should().BeNull();
        grid.List.SelectedIndex.Should().Be(-1);

        grid.List.SelectedIndex = 0;
        Pump();
        viewModel.IsWorkingTreeSelected.Should().BeFalse();
        viewModel.SelectedCommit.Should().BeSameAs(viewModel.Commits[0]);
        window.Close();
    }

    [AvaloniaTest]
    public async Task Keyboard_navigation_moves_the_selection()
    {
        using TestRepository repository = CreateBranchyRepository();
        repository.RunGit("add", "-A");
        repository.RunGit("-c", "user.name=T", "-c", "user.email=t@e.x", "commit", "--quiet", "-m", "Clean");
        MainWindowViewModel viewModel = await OpenViewModelAsync(repository);
        viewModel.ShowUncommittedRow.Should().BeFalse();
        (Window window, RevisionGridView grid) = Host(viewModel, 1000, 600);

        grid.List.SelectedIndex = 0;
        Pump();
        grid.List.ContainerFromIndex(0)!.Focus();
        Pump();
        int last = viewModel.Commits.Count - 1;

        Press(window, Key.Down);
        grid.List.SelectedIndex.Should().Be(1);
        Press(window, Key.Down);
        grid.List.SelectedIndex.Should().Be(2);
        Press(window, Key.Up);
        grid.List.SelectedIndex.Should().Be(1);
        Press(window, Key.End);
        grid.List.SelectedIndex.Should().Be(last);
        viewModel.SelectedCommit.Should().BeSameAs(viewModel.Commits[last]);
        Press(window, Key.PageUp);
        grid.List.SelectedIndex.Should().BeLessThan(last);
        Press(window, Key.Home);
        grid.List.SelectedIndex.Should().Be(0);
        Press(window, Key.PageDown);
        grid.List.SelectedIndex.Should().BeGreaterThan(0);

        int activated = 0;
        grid.RowActivated += (_, _) => activated++;
        Press(window, Key.Enter);
        activated.Should().Be(1);
        window.Close();
    }

    [AvaloniaTest]
    public async Task The_uncommitted_changes_row_appears_selects_and_disappears()
    {
        using TestRepository repository = CreateBranchyRepository();
        File.WriteAllText(Path.Combine(repository.Path, "a.txt"), "dirty");
        File.WriteAllText(Path.Combine(repository.Path, "new1.txt"), "x");
        MainWindowViewModel viewModel = await OpenViewModelAsync(repository);
        (Window window, RevisionGridView grid) = Host(viewModel, 1000, 600);

        Border row = grid.FindControl<Border>("UncommittedRow")!;
        row.IsEffectivelyVisible.Should().BeTrue();
        grid.FindControl<TextBlock>("UncommittedText")!.Text.Should().Be($"Uncommitted changes ({viewModel.ChangedFiles.Count})");
        viewModel.ChangedFiles.Count.Should().BeGreaterThanOrEqualTo(2);

        // Down from the working tree row selects the first commit; Up from the first commit goes back.
        viewModel.IsWorkingTreeSelected = true;
        Pump();
        row.Classes.Contains("selected").Should().BeTrue();
        row.Focus();
        Press(window, Key.Down);
        viewModel.IsWorkingTreeSelected.Should().BeFalse();
        viewModel.SelectedCommit.Should().BeSameAs(viewModel.Commits[0]);
        row.Classes.Contains("selected").Should().BeFalse();
        Press(window, Key.Up);
        viewModel.IsWorkingTreeSelected.Should().BeTrue();
        viewModel.SelectedCommit.Should().BeNull();

        // Narrowing the list by a filter hides it, clearing the filter brings it back.
        viewModel.FilterText = "docs";
        await SettleAsync(viewModel);
        Pump();
        row.IsEffectivelyVisible.Should().BeFalse();
        viewModel.ClearFilterCommand.Execute(null);
        await WaitForAsync(() => row.IsEffectivelyVisible);

        // A clean working tree has no row, and a selected row is deselected.
        repository.RunGit("add", "-A");
        repository.RunGit("-c", "user.name=T", "-c", "user.email=t@e.x", "commit", "--quiet", "-m", "Everything");
        await viewModel.RefreshChangesAsync();
        Pump();
        viewModel.ChangedFiles.Should().BeEmpty();
        row.IsEffectivelyVisible.Should().BeFalse();
        viewModel.IsWorkingTreeSelected.Should().BeFalse();
        window.Close();
    }

    [AvaloniaTest]
    public async Task Header_and_row_columns_line_up_and_resize_together()
    {
        using TestRepository repository = CreateBranchyRepository();
        MainWindowViewModel viewModel = await OpenViewModelAsync(repository);
        (Window window, RevisionGridView grid) = Host(viewModel, 1100, 600);

        void AssertAligned(string stage)
        {
            Pump();
            foreach ((string header, string cell) in new[] { ("HeaderAvatar", "avatarCell"), ("HeaderAuthor", "authorCell"), ("HeaderDate", "dateCell"), ("HeaderHash", "hashCell") })
            {
                Control headerCell = grid.FindControl<Control>(header)!;
                Control rowCell = grid.List.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains(cell));
                double headerX = headerCell.TranslatePoint(default, window)!.Value.X;
                double rowX = rowCell.TranslatePoint(default, window)!.Value.X;
                rowX.Should().BeApproximately(headerX, 0.51, $"{cell} {stage}");
                rowCell.Bounds.Width.Should().BeApproximately(headerCell.Bounds.Width, 0.51, $"{cell} width {stage}");
            }

            double messageRight = grid.FindControl<Control>("HeaderMessage")!.Bounds.Right;
            messageRight.Should().BeApproximately(grid.FindControl<Control>("HeaderAvatar")!.Bounds.X, 0.51, stage);
        }

        AssertAligned("initially");

        // Drag the left edge of the Author column 40px to the left with the mouse.
        Border grip = grid.FindControl<Control>("HeaderAuthor")!.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("grip"));
        Point start = grip.TranslatePoint(new Point(2, 10), window)!.Value;
        double before = viewModel.Columns.AuthorWidth;
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(new Point(start.X - 20, start.Y));
        window.MouseMove(new Point(start.X - 40, start.Y));
        window.MouseUp(new Point(start.X - 40, start.Y), MouseButton.Left);
        Pump();
        viewModel.Columns.AuthorWidth.Should().BeApproximately(before + 40, 1);
        AssertAligned("after resizing");
        GridColumnStore.Current.Load().AuthorWidth.Should().BeApproximately(before + 40, 1, "the widths are persisted when the drag ends");

        // A new view-model starts with the stored widths.
        new MainWindowViewModel(GitEnvironment.Initialize(), new NoFolderPicker()).Columns.AuthorWidth.Should().BeApproximately(before + 40, 1);
        window.Close();
    }

    [AvaloniaTest]
    public async Task Narrow_grids_hide_columns_in_header_and_rows_alike()
    {
        using TestRepository repository = CreateBranchyRepository();
        MainWindowViewModel viewModel = await OpenViewModelAsync(repository);
        foreach ((double width, string state, bool date, bool author, bool hash) in new[] { (900.0, "wide", true, true, true), (600.0, "medium", false, true, true), (400.0, "narrow", false, false, true), (330.0, "narrow", false, false, false) })
        {
            (Window window, RevisionGridView grid) = Host(viewModel, width, 500);
            grid.HistoryLayoutState.Should().Be(state, $"{width}px");
            grid.FindControl<Control>("HeaderDate")!.IsEffectivelyVisible.Should().Be(date, $"{width}px date");
            grid.FindControl<Control>("HeaderAuthor")!.IsEffectivelyVisible.Should().Be(author, $"{width}px author");
            grid.FindControl<Control>("HeaderAvatar")!.IsEffectivelyVisible.Should().Be(author, $"{width}px avatar");
            grid.FindControl<Control>("HeaderHash")!.IsEffectivelyVisible.Should().Be(hash, $"{width}px hash");
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task The_filter_bar_drives_the_view_model()
    {
        using TestRepository repository = CreateBranchyRepository();
        MainWindowViewModel viewModel = await OpenViewModelAsync(repository);
        (Window window, RevisionGridView grid) = Host(viewModel, 1000, 500);

        ComboBox combo = grid.FindControl<RevisionFilterBarView>("FilterBar")!.FindControl<ComboBox>("BranchCombo")!;
        combo.ItemCount.Should().Be(viewModel.BranchChoices.Count);
        combo.SelectedItem.Should().Be(MainWindowViewModel.DefaultBranchChoice);

        viewModel.FilterDebounce = TimeSpan.FromMilliseconds(20);
        grid.FilterTextBox.Text = "docs";
        await WaitForAsync(() => viewModel.Commits.Count == 1 && !viewModel.IsBusy);
        viewModel.FilterText.Should().Be("docs");
        grid.List.ItemCount.Should().Be(1);

        Button clear = grid.FindControl<RevisionFilterBarView>("FilterBar")!.FindControl<Button>("ClearFilterButton")!;
        clear.IsEffectivelyVisible.Should().BeTrue();
        clear.Command!.Execute(null);
        await WaitForAsync(() => viewModel.Commits.Count == 6 && !viewModel.IsBusy);
        grid.FilterTextBox.Text.Should().BeEmpty();

        combo.SelectedItem = "feature/login";
        await WaitForAsync(() => viewModel.SelectedBranchChoice == "feature/login" && !viewModel.IsBusy && !Subjects(viewModel).Contains("Update docs"));
        window.Close();
    }

    [AvaloniaTest]
    public async Task Grid_screenshots_in_light_and_dark()
    {
        using TestRepository repository = CreateBranchyRepository();
        File.WriteAllText(Path.Combine(repository.Path, "a.txt"), "dirty");
        foreach (string name in new[] { "a", "b", "c", "d", "e" })
        {
            repository.RunGit("branch", "many/" + name);
        }

        repository.RunGit("update-ref", "refs/remotes/origin/develop", "HEAD");
        MainWindowViewModel viewModel = await OpenViewModelAsync(repository);
        viewModel.SelectedBranchChoice = MainWindowViewModel.AllBranchesChoice;
        await SettleAsync(viewModel);
        viewModel.SelectedCommit = viewModel.Commits[2];

        foreach ((int width, int height) in new[] { (1200, 720), (900, 600) })
        {
            foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                (Window window, RevisionGridView grid) = Host(viewModel, width, height, variant);
                grid.List.ItemCount.Should().Be(viewModel.Commits.Count);
                WriteableBitmap? frame = window.CaptureRenderedFrame();
                frame.Should().NotBeNull();
                string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
                Directory.CreateDirectory(directory);
                string file = Path.Combine(directory, $"grid-{width}x{height}-{variant.Key.ToString()!.ToLowerInvariant()}.png");
                frame!.Save(file);
                TestContext.AddTestAttachment(file);
                window.Close();
            }
        }
    }
}
