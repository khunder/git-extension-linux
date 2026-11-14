using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;
using GitExtensions.Extensibility.Git;
using static GitExtensionsDesktopTests.ToolsTestSupport;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class NavigationTests
{
    private BehaviorSettings _previousBehavior = null!;
    private string _directory = null!;

    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    [SetUp]
    public void IsolateSettings()
    {
        _directory = MakeTempFolder();
        _previousBehavior = BehaviorSettings.Current;
        BehaviorSettings.Current = new BehaviorSettings(Path.Combine(_directory, "behavior.json"));
    }

    [TearDown]
    public void RestoreSettings()
    {
        BehaviorSettings.Current = _previousBehavior;
        DeleteFolder(_directory);
    }

    private static MainWindowViewModel CreateViewModel() => new(GitEnvironment.Initialize(), new NoFolderPicker());

    private static ObjectId Id(string hash) => ObjectId.Parse(hash);

    // ---- pure ----

    [AvaloniaTest]
    public void History_moves_back_and_forward_and_drops_the_forward_part_on_a_new_selection()
    {
        NavigationHistory history = new();
        ObjectId a = Id(new string('a', 40));
        ObjectId b = Id(new string('b', 40));
        ObjectId c = Id(new string('c', 40));
        ObjectId d = Id(new string('d', 40));
        history.Record(a);
        history.Record(a);
        history.Record(b);
        history.Record(c);
        history.Count.Should().Be(3, "the same commit twice in a row is one entry");

        history.Back().Should().Be(b);
        history.Back().Should().Be(a);
        history.Back().Should().BeNull();
        history.CanGoForward.Should().BeTrue();
        history.Forward().Should().Be(b);

        history.Record(d);
        history.CanGoForward.Should().BeFalse("selecting something new after going back drops c");
        history.Back().Should().Be(b);
    }

    [AvaloniaTest]
    public void History_keeps_at_most_its_capacity()
    {
        NavigationHistory history = new();
        for (int i = 0; i < NavigationHistory.Capacity + 20; i++)
        {
            history.Record(Id(i.ToString("x40")));
        }

        history.Count.Should().Be(NavigationHistory.Capacity);
        history.Current.Should().Be(Id((NavigationHistory.Capacity + 19).ToString("x40")));
    }

    [AvaloniaTest]
    public void Bisect_refs_are_parsed_into_marks()
    {
        string bad = new('a', 40);
        string good = new('b', 40);
        string skip = new('c', 40);
        BisectState state = BisectStateParser.Parse(true, $"{bad} refs/bisect/bad\n{good} refs/bisect/good-{good}\n{skip} refs/bisect/skip-{skip}\n");

        state.Bad.Should().Be(Id(bad));
        state.Good.Should().Equal(Id(good));
        state.Skipped.Should().Equal(Id(skip));
        state.Marks[Id(bad)].Should().Be(BisectMark.Bad);
        state.Marks[Id(good)].Should().Be(BisectMark.Good);
        state.Marks[Id(skip)].Should().Be(BisectMark.Skip);
        state.Summary.Should().Be($"Bisecting: bad {Id(bad).ToShortString()}, 1 good, 1 skipped.");
        BisectState.None.Summary.Should().Be("Not bisecting.");
        BisectStateParser.ParseFirstBad($"{bad} is the first bad commit\ncommit {bad}").Should().Be(Id(bad));
        BisectStateParser.ParseFirstBad($"{good} is the first 'bad' commit\n").Should().Be(Id(good), "git quotes the term in newer versions");
    }

    [AvaloniaTest]
    public void Fsck_lines_are_parsed()
    {
        string commit = new('a', 40);
        string blob = new('b', 40);
        IReadOnlyList<LostObjectLine> lines = LostObjectParser.Parse($"Checking object directories: 100%\ndangling commit {commit}\nunreachable blob {blob}\nnoise\n");

        lines.Select(l => (l.Type, l.Hash)).Should().Equal((LostObjectType.Commit, commit), (LostObjectType.Blob, blob));
        lines[0].RawType.Should().Be("dangling commit");
    }

    // ---- go to commit and navigation ----

    private static async Task<(MainWindowViewModel Vm, List<string> Hashes)> OpenWithHistoryAsync(TestRepository repository, int extraCommits)
    {
        repository.CleanWorkingTree();
        List<string> hashes = [];
        for (int i = 0; i < extraCommits; i++)
        {
            hashes.Add(repository.Commit($"f{i}.txt", i.ToString(), $"Commit {i}"));
        }

        MainWindowViewModel vm = CreateViewModel();
        await vm.OpenAsync(repository.Path);
        return (vm, hashes);
    }

    [AvaloniaTest]
    public async Task Go_to_commit_loads_deeper_history_when_the_limit_hides_the_commit()
    {
        using TestRepository repository = new();
        string first = repository.GitOutput("rev-list", "--max-parents=0", "HEAD").Trim();
        BehaviorSettings.Current.SetHistoryLimit(3);
        (MainWindowViewModel vm, _) = await OpenWithHistoryAsync(repository, 6);
        vm.Commits.Should().HaveCount(3);

        vm.ShowGoToCommitCommand.Execute(null);
        vm.Dialogs.IsOpen.Should().BeTrue();
        GoToCommitViewModel dialog = (GoToCommitViewModel)vm.Dialogs.Current!;
        dialog.Expression = first;
        await dialog.GoAsync();

        dialog.Error.Should().BeEmpty();
        vm.Dialogs.IsOpen.Should().BeFalse();
        vm.Commits.Count.Should().BeGreaterThan(3);
        vm.SelectedCommit!.ObjectId.ToString().Should().Be(first);
    }

    [AvaloniaTest]
    public async Task Go_to_commit_accepts_revision_expressions_and_lists_branches_and_tags()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        (MainWindowViewModel vm, List<string> hashes) = await OpenWithHistoryAsync(repository, 3);

        vm.ShowGoToCommitCommand.Execute(null);
        GoToCommitViewModel dialog = (GoToCommitViewModel)vm.Dialogs.Current!;
        await dialog.LoadRefsAsync();
        dialog.Branches.Should().Contain(["main", "develop", "feature/x"]);
        dialog.Tags.Should().Contain(["v1.0", "release/2.0"]);

        dialog.Expression = "HEAD~2";
        await dialog.GoAsync();
        vm.SelectedCommit!.ObjectId.ToString().Should().Be(repository.GitOutput("rev-parse", "HEAD~2").Trim());

        vm.ShowGoToCommitCommand.Execute(null);
        dialog = (GoToCommitViewModel)vm.Dialogs.Current!;
        await dialog.LoadRefsAsync();
        dialog.SelectedTag = "v1.0";
        dialog.Expression.Should().Be("v1.0", "picking from a list fills the expression");
        dialog.SelectedBranch = "develop";
        dialog.SelectedTag.Should().BeNull("picking a branch empties the tag list");
        dialog.Expression.Should().Be("develop");
        dialog.SelectedBranch = null;
        dialog.Expression = "v1.0";
        await dialog.GoAsync();
        vm.SelectedCommit!.ObjectId.ToString().Should().Be(repository.GitOutput("rev-parse", "v1.0").Trim());
        hashes.Should().NotBeEmpty();
    }

    [AvaloniaTest]
    public async Task Go_to_commit_explains_a_bad_expression_and_stays_open()
    {
        using TestRepository repository = new();
        (MainWindowViewModel vm, _) = await OpenWithHistoryAsync(repository, 1);
        vm.ShowGoToCommitCommand.Execute(null);
        GoToCommitViewModel dialog = (GoToCommitViewModel)vm.Dialogs.Current!;
        ObjectId? before = vm.SelectedCommit?.ObjectId;

        dialog.Expression = "no-such-thing";
        await dialog.GoAsync();

        dialog.HasError.Should().BeTrue();
        dialog.Error.Should().Contain("no-such-thing");
        vm.Dialogs.IsOpen.Should().BeTrue();
        vm.SelectedCommit?.ObjectId.Should().Be(before!.Value);

        dialog.Expression = string.Empty;
        await dialog.GoAsync();
        dialog.Error.Should().Contain("Type a revision");
        dialog.CancelCommand.Execute(null);
        vm.Dialogs.IsOpen.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Go_to_commit_widens_a_branch_filter_to_reach_a_commit_on_another_branch()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        repository.RunGit("checkout", "--quiet", "-b", "side");
        string side = repository.Commit("side.txt", "s", "Only on side");
        repository.RunGit("checkout", "--quiet", "main");
        MainWindowViewModel vm = CreateViewModel();
        await vm.OpenAsync(repository.Path);
        await vm.UseCurrentBranchAsync();
        vm.SelectedBranchChoice.Should().Be(MainWindowViewModel.CurrentBranchChoice);
        vm.Commits.Any(c => c.ObjectId.ToString() == side).Should().BeFalse();

        string? problem = await vm.GoToCommitAsync(Id(side));

        problem.Should().BeNull();
        vm.SelectedBranchChoice.Should().Be(MainWindowViewModel.AllBranchesChoice);
        vm.SelectedCommit!.ObjectId.ToString().Should().Be(side);
    }

    [AvaloniaTest]
    public async Task Go_to_commit_refuses_a_commit_no_ref_reaches()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        string lost = repository.Commit("l.txt", "l", "Soon lost");
        repository.RunGit("reset", "--hard", "HEAD~1");
        repository.RunGit("reflog", "expire", "--expire=now", "--all");
        MainWindowViewModel vm = CreateViewModel();
        await vm.OpenAsync(repository.Path);

        string? problem = await vm.GoToCommitAsync(Id(lost));

        problem.Should().Contain("not reachable");
    }

    [AvaloniaTest]
    public async Task Navigate_backward_and_forward_walk_the_selection_history()
    {
        using TestRepository repository = new();
        (MainWindowViewModel vm, _) = await OpenWithHistoryAsync(repository, 4);
        vm.NavigateBackCommand.CanExecute(null).Should().BeFalse();
        CommitItemViewModel one = vm.Commits[1];
        CommitItemViewModel three = vm.Commits[3];
        CommitItemViewModel zero = vm.Commits[0];
        vm.SelectedCommit = zero;
        vm.SelectedCommit = one;
        vm.SelectedCommit = three;
        vm.NavigateBackCommand.CanExecute(null).Should().BeTrue();
        vm.NavigateForwardCommand.CanExecute(null).Should().BeFalse();

        await vm.NavigateBackCommand.ExecuteAsync(null);
        vm.SelectedCommit.Should().BeSameAs(one);
        await vm.NavigateBackCommand.ExecuteAsync(null);
        vm.SelectedCommit.Should().BeSameAs(zero);
        vm.NavigateBackCommand.CanExecute(null).Should().BeFalse();
        vm.NavigateForwardCommand.CanExecute(null).Should().BeTrue();

        await vm.NavigateForwardCommand.ExecuteAsync(null);
        vm.SelectedCommit.Should().BeSameAs(one);
        vm.NavigateForwardCommand.CanExecute(null).Should().BeTrue("going back and forth does not drop the later entries");

        vm.SelectedCommit = vm.Commits[2];
        vm.NavigateForwardCommand.CanExecute(null).Should().BeFalse("a new selection ends the forward history");
        await vm.NavigateBackCommand.ExecuteAsync(null);
        vm.SelectedCommit.Should().BeSameAs(one);
    }

    [AvaloniaTest]
    public async Task Alt_arrows_and_ctrl_shift_g_are_registered_shortcuts()
    {
        using TestRepository repository = new();
        (MainWindowViewModel vm, _) = await OpenWithHistoryAsync(repository, 2);

        vm.Commands.Single(c => c.Id == "history.goToCommit").PrimaryGestureText.Should().Be("Ctrl+Shift+G");
        vm.Commands.Single(c => c.Id == "history.back").PrimaryGestureText.Should().Be("Alt+Left");
        vm.Commands.Single(c => c.Id == "history.forward").PrimaryGestureText.Should().Be("Alt+Right");
        vm.Commands.Single(c => c.Id == "history.back").CanExecute().Should().BeFalse();
        vm.IsOverlayOpen.Should().BeFalse();
        vm.Commands.Single(c => c.Id == "history.goToCommit").Execute();
        vm.IsOverlayOpen.Should().BeTrue("an open dialog counts as an overlay, so shortcuts do not leak through it");
    }

    // ---- bisect ----

    [AvaloniaTest]
    public async Task Bisect_finds_the_first_bad_commit_marks_the_grid_and_resets_after_confirmation()
    {
        using TestRepository repository = new();
        (MainWindowViewModel vm, List<string> hashes) = await OpenWithHistoryAsync(repository, 5);
        string firstBad = hashes[2];
        string root = repository.GitOutput("rev-list", "--max-parents=0", "HEAD").Trim();
        BisectViewModel bisect = vm.Bisect;

        // During a bisect HEAD is detached in the middle of the range: show every branch so the marked tip is in the list.
        vm.SelectedBranchChoice = MainWindowViewModel.AllBranchesChoice;
        await ReflogTests.WaitAsync(() => vm.Commits.Count >= 6 && !vm.IsBusy);

        vm.ShowBisectCommand.Execute(null);
        vm.Dialogs.Current.Should().BeSameAs(bisect);
        await ReflogTests.WaitAsync(() => !bisect.IsBusy);
        bisect.IsBisecting.Should().BeFalse();
        bisect.CanMark.Should().BeFalse();

        bisect.BadRevision = "HEAD";
        bisect.GoodRevision = root;
        await bisect.StartAsync();

        bisect.IsBisecting.Should().BeTrue();
        bisect.StateText.Should().Contain("bad").And.Contain("1 good");
        await ReflogTests.WaitAsync(() => vm.Commits.Count > 0 && vm.Commits.Any(c => c.BisectMark == BisectMark.Bad));
        vm.Commits.Single(c => c.BisectMark == BisectMark.Bad).ObjectId.ToString().Should().Be(hashes[4]);
        vm.Commits.Single(c => c.BisectMark == BisectMark.Good).ObjectId.ToString().Should().Be(root);

        // Answer like a tester: everything from firstBad on is bad.
        for (int step = 0; step < 10 && !bisect.HasFirstBad; step++)
        {
            string head = repository.HeadId();
            bool bad = hashes.IndexOf(head) >= hashes.IndexOf(firstBad);
            await (bad ? bisect.MarkBadAsync() : bisect.MarkGoodAsync());
        }

        bisect.FirstBadText.Should().Contain(firstBad[..7]);
        bisect.Output.Should().Contain("is the first");

        bisect.RequestResetCommand.Execute(null);
        bisect.IsResetPromptVisible.Should().BeTrue();
        bisect.IsBisecting.Should().BeTrue("nothing happens before the confirmation");
        bisect.CancelResetCommand.Execute(null);
        bisect.IsBisecting.Should().BeTrue();

        bisect.RequestResetCommand.Execute(null);
        await bisect.ConfirmResetAsync();
        bisect.IsBisecting.Should().BeFalse();
        bisect.StateText.Should().Be("Not bisecting.");
        repository.GitOutput("rev-parse", "--abbrev-ref", "HEAD").Trim().Should().Be("main");
        await ReflogTests.WaitAsync(() => vm.Commits.Count > 0 && vm.Commits.All(c => c.BisectMark == BisectMark.None));
    }

    [AvaloniaTest]
    public async Task Bisect_can_mark_the_selected_commit_and_skip()
    {
        using TestRepository repository = new();
        (MainWindowViewModel vm, List<string> hashes) = await OpenWithHistoryAsync(repository, 5);
        BisectViewModel bisect = vm.Bisect;
        await bisect.StartAsync();
        bisect.IsBisecting.Should().BeTrue();

        await ReflogTests.WaitAsync(() => vm.Commits.Count > 0);
        vm.SelectedCommit = vm.Commits.Single(c => c.ObjectId.ToString() == hashes[0]);
        bisect.MarkSelectedCommit = true;
        await bisect.MarkGoodAsync();
        bisect.State.Good.Should().Contain(Id(hashes[0]));

        vm.SelectedCommit = vm.Commits.Single(c => c.ObjectId.ToString() == hashes[3]);
        await bisect.SkipAsync();
        bisect.State.Skipped.Should().Contain(Id(hashes[3]));

        await ReflogTests.WaitAsync(() => vm.Commits.Any(c => c.BisectMark == BisectMark.Skip));
        bisect.MarkSelectedCommit = false;
        await bisect.MarkBadAsync();
        bisect.State.Bad.Should().NotBeNull();
    }

    // ---- lost objects ----

    private static async Task<(LostObjectsViewModel Vm, Context Context, string Lost)> OpenLostAsync(TestRepository repository)
    {
        repository.CleanWorkingTree();
        string lost = repository.Commit("l.txt", "l", "A lost commit");
        repository.RunGit("reset", "--hard", "HEAD~1");
        repository.RunGit("reflog", "expire", "--expire=now", "--all");
        Context context = CreateContext(repository);
        LostObjectsViewModel vm = new(context.Tools, () => { });
        await vm.ScanAsync();
        return (vm, context, lost);
    }

    [AvaloniaTest]
    public async Task Lost_objects_lists_the_dangling_commit_with_details_and_a_preview()
    {
        using TestRepository repository = new();
        (LostObjectsViewModel vm, Context context, string lost) = await OpenLostAsync(repository);

        LostObjectItem item = vm.Items.Single(i => i.Hash == lost);
        item.Type.Should().Be(LostObjectType.Commit);
        item.Subject.Should().Be("A lost commit");
        item.Author.Should().NotBeEmpty();
        item.Date.Should().NotBeNull();
        item.ShortParent.Should().NotBeEmpty();

        vm.SelectedItem = item;
        await ReflogTests.WaitAsync(() => vm.Preview.Contains("A lost commit", StringComparison.Ordinal));
        vm.Preview.Should().Contain("l.txt");
        vm.CanRestore.Should().BeTrue();
        context.Errors.Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Lost_objects_restores_a_commit_as_a_branch_a_named_tag_and_lost_found_tags()
    {
        using TestRepository repository = new();
        (LostObjectsViewModel vm, Context context, string lost) = await OpenLostAsync(repository);
        LostObjectItem item = vm.Items.Single(i => i.Hash == lost);
        vm.SelectedItem = item;

        // A bad name is explained and creates nothing.
        (await vm.CreateRefAsync(item, "bad name", isTag: false)).Should().BeFalse();
        context.Errors.Should().Contain(e => e.Contains("space", StringComparison.OrdinalIgnoreCase));

        vm.RequestCreateBranchCommand.Execute(null);
        vm.PromptKind.Should().Be(LostObjectsPromptKind.CreateBranch);
        vm.PromptName.Should().StartWith("recovered-");
        vm.PromptName = "rescued";
        await vm.ConfirmPromptAsync();
        vm.PromptKind.Should().Be(LostObjectsPromptKind.None);
        repository.GitOutput("rev-parse", "rescued").Trim().Should().Be(lost);

        (await vm.CreateRefAsync(item, "my-tag", isTag: true)).Should().BeTrue();
        repository.GitOutput("rev-parse", "my-tag^{commit}").Trim().Should().Be(lost);

        item.IsChecked = true;
        (await vm.RestoreCheckedAsTagsAsync()).Should().Be(1);
        repository.GitOutput("rev-parse", $"{LostObjectsViewModel.TagPrefix}{lost}").Trim().Should().Be(lost);
        (await vm.RestoreCheckedAsTagsAsync()).Should().Be(1, "restoring twice keeps the one tag");

        vm.RequestDeleteTagsCommand.Execute(null);
        vm.PromptKind.Should().Be(LostObjectsPromptKind.ConfirmDeleteTags);
        repository.GitOutput("tag", "--list", $"{LostObjectsViewModel.TagPrefix}*").Should().NotBeEmpty("nothing is deleted before the confirmation");
        await vm.ConfirmPromptAsync();
        repository.GitOutput("tag", "--list", $"{LostObjectsViewModel.TagPrefix}*").Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Lost_objects_prune_needs_confirmation()
    {
        using TestRepository repository = new();
        (LostObjectsViewModel vm, _, string lost) = await OpenLostAsync(repository);
        vm.Items.Should().Contain(i => i.Hash == lost);

        vm.RequestPruneCommand.Execute(null);
        vm.PromptKind.Should().Be(LostObjectsPromptKind.ConfirmPrune);
        vm.PromptText.Should().Contain("for good");
        vm.CancelPromptCommand.Execute(null);
        repository.GitOutput("cat-file", "-t", lost).Trim().Should().Be("commit");

        vm.RequestPruneCommand.Execute(null);
        await vm.ConfirmPromptAsync();
        vm.Items.Should().NotContain(i => i.Hash == lost);
        vm.StatusText.Should().Contain("No lost objects");
    }

    [AvaloniaTest]
    public async Task Maintenance_menu_opens_the_lost_objects_dialog()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        MainWindowViewModel vm = CreateViewModel();
        await vm.OpenAsync(repository.Path);

        vm.ShowLostObjectsCommand.Execute(null);

        vm.Dialogs.Current.Should().BeOfType<LostObjectsViewModel>();
        await ReflogTests.WaitAsync(() => !((LostObjectsViewModel)vm.Dialogs.Current!).IsBusy);
    }

    // ---- rendering ----

    [AvaloniaTest]
    public async Task Dialogs_and_bisect_marks_render_in_light_and_dark()
    {
        using TestRepository repository = new();
        (MainWindowViewModel vm, List<string> hashes) = await OpenWithHistoryAsync(repository, 4);
        MainWindow window = new() { DataContext = vm, Width = 1000, Height = 680 };
        window.Show();
        await vm.OpenAsync(repository.Path);
        vm.SelectedBranchChoice = MainWindowViewModel.AllBranchesChoice;
        await ReflogTests.WaitAsync(() => vm.Commits.Count >= 6 && !vm.IsBusy);

        // The menu entries are wired.
        MainMenuView menu = window.FindControl<MainMenuView>("MainMenuBar")!;
        MenuItem Item(string name) => menu.FindControl<MenuItem>(name)!;
        Item("GoToCommitMenuItem").Command.Should().BeSameAs(vm.ShowGoToCommitCommand);
        Item("NavigateBackMenuItem").Command.Should().BeSameAs(vm.NavigateBackCommand);
        Item("NavigateForwardMenuItem").Command.Should().BeSameAs(vm.NavigateForwardCommand);
        Item("BisectMenuItem").Command.Should().BeSameAs(vm.ShowBisectCommand);
        Item("RecoverLostMenuItem").Command.Should().BeSameAs(vm.ShowLostObjectsCommand);

        vm.ShowGoToCommitCommand.Execute(null);
        await ReflogTests.WaitAsync(() => window.GetVisualDescendants().OfType<GoToCommitView>().Any());
        ((GoToCommitViewModel)vm.Dialogs.Current!).Expression = "nonsense";
        await ((GoToCommitViewModel)vm.Dialogs.Current!).GoAsync();
        window.FindControl<ToolDialogsView>("ToolDialogsOverlay")!.IsVisible.Should().BeTrue();
        window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "ErrorText" && t.IsVisible).Should().BeTrue();
        Capture(window, "goto");
        vm.Dialogs.Close();

        vm.Bisect.BadRevision = "HEAD";
        vm.Bisect.GoodRevision = hashes[0];
        await vm.Bisect.StartAsync();
        vm.ShowBisectCommand.Execute(null);
        await ReflogTests.WaitAsync(() => window.GetVisualDescendants().OfType<BisectView>().Any());
        vm.Bisect.RequestResetCommand.Execute(null);
        Capture(window, "bisect");
        vm.Dialogs.Close();
        await ReflogTests.WaitAsync(() => vm.Commits.Any(c => c.BisectMark == BisectMark.Bad));
        window.GetVisualDescendants().OfType<Border>().Count(b => b.Classes.Contains("bisect")).Should().BeGreaterThan(0);
        Capture(window, "bisect-grid");
        await vm.Bisect.ConfirmResetAsync();

        repository.RunGit("checkout", "--quiet", "main");
        vm.ShowLostObjectsCommand.Execute(null);
        await ReflogTests.WaitAsync(() => window.GetVisualDescendants().OfType<LostObjectsView>().Any());
        Capture(window, "lost");
    }

    private static void Capture(MainWindow window, string name)
    {
        foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            window.RequestedThemeVariant = variant;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            WriteableBitmap? frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, $"nav-{name}-{variant.Key.ToString()!.ToLowerInvariant()}.png");
            frame!.Save(file);
            TestContext.AddTestAttachment(file);
        }
    }
}
