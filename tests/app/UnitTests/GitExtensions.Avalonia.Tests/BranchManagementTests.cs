using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class BranchManagementTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static MainWindowViewModel CreateViewModel() => new(GitEnvironment.Initialize(), new NoFolderPicker());

    private static IEnumerable<RefNodeViewModel> Flatten(IEnumerable<RefNodeViewModel> nodes)
        => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    private static RefNodeViewModel Node(MainWindowViewModel vm, RefNodeKind kind, string fullName)
        => Flatten(vm.Refs.Roots).Single(n => n.Kind == kind && n.FullName == fullName);

    private static bool HasNode(MainWindowViewModel vm, RefNodeKind kind, string fullName)
        => Flatten(vm.Refs.Roots).Any(n => n.Kind == kind && n.FullName == fullName);

    private static string Git(string directory, params string[] arguments) => RemoteScenario.Run(directory, arguments);

    private static async Task<(RemoteScenario Scenario, MainWindowViewModel ViewModel)> OpenAlphaAsync()
    {
        RemoteScenario scenario = new();
        MainWindowViewModel vm = CreateViewModel();
        await vm.OpenAsync(scenario.Alpha);
        return (scenario, vm);
    }

    private static async Task ConfirmAsync(MainWindowViewModel vm, string? input = null)
    {
        if (input is not null)
        {
            vm.Refs.InputText = input;
        }

        await vm.Refs.ConfirmPromptCommand.ExecuteAsync(null);
    }

    // ---- pure helpers ----

    [TestCase("", 0, 0, false)]
    [TestCase("[ahead 2, behind 1]", 2, 1, false)]
    [TestCase("[ahead 3]", 3, 0, false)]
    [TestCase("[behind 4]", 0, 4, false)]
    [TestCase("[gone]", 0, 0, true)]
    [TestCase("  [ahead 12, behind 30]  ", 12, 30, false)]
    [TestCase("garbage", 0, 0, false)]
    public void UpstreamTrackParser_parses_the_track_text(string text, int ahead, int behind, bool gone)
        => UpstreamTrackParser.Parse(text).Should().Be((ahead, behind, gone));

    [AvaloniaTest]
    public void UpstreamTrackParser_parses_for_each_ref_lines()
    {
        Dictionary<string, UpstreamTrack> tracks = UpstreamTrackParser.ParseAll(
            "refs/heads/main\torigin/main\t[ahead 2, behind 1]\torigin\trefs/heads/main\n"
            + "refs/heads/local\t\t\t\t\n"
            + "refs/heads/feature/x\torigin/x\t[gone]\torigin\trefs/heads/x\n"
            + "refs/heads/synced\torigin/synced\t\torigin\trefs/heads/synced\n");

        tracks.Keys.Should().BeEquivalentTo("main", "feature/x", "synced");
        tracks["main"].Should().Be(new UpstreamTrack("origin/main", 2, 1, false, "origin", "main"));
        tracks["main"].Text.Should().Be("↑2 ↓1");
        tracks["feature/x"].Gone.Should().BeTrue();
        tracks["feature/x"].RemoteRef.Should().Be("x");
        tracks["synced"].InSync.Should().BeTrue();
    }

    [TestCase("", false)]
    [TestCase("  ", false)]
    [TestCase("-x", false)]
    [TestCase("a b", false)]
    [TestCase("a\"b", false)]
    [TestCase("@{u}", false)]
    [TestCase("ok/name", true)]
    public void RefNameValidator_prechecks_without_git(string name, bool valid)
        => (RefNameValidator.PreCheck(name) is null).Should().Be(valid);

    [TestCase("good-name", false, true)]
    [TestCase("feature/ok", false, true)]
    [TestCase("a..b", false, false)]
    [TestCase("x.lock", false, false)]
    [TestCase("a~b", false, false)]
    [TestCase("a^b", false, false)]
    [TestCase("feat//x", false, false)]
    [TestCase("end/", false, false)]
    [TestCase("a:b", false, false)]
    [TestCase("HEAD", false, false)]
    [TestCase("v1.0", true, true)]
    [TestCase("bad[tag", true, false)]
    public void RefNameValidator_asks_git(string name, bool isTag, bool valid)
    {
        using TestRepository repository = new();
        GitCommands.GitModule module = GitEnvironment.Initialize().OpenRepository(repository.Path)!;
        (RefNameValidator.Validate(module.GitExecutable, name, isTag) is null).Should().Be(valid);
    }

    // ---- rename ----

    [AvaloniaTest]
    public async Task Rename_another_branch_keeps_its_upstream()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            Git(scenario.Alpha, "branch", "feat");
            Git(scenario.Alpha, "push", "-q", "-u", "origin", "feat");
            await vm.OpenAsync(scenario.Alpha);

            vm.Refs.RequestRenameCommand.Execute(Node(vm, RefNodeKind.LocalBranch, "feat"));
            vm.Refs.PromptKind.Should().Be(RefPromptKind.RenameBranch);
            vm.Refs.InputText.Should().Be("feat");
            await ConfirmAsync(vm, "feat2");

            vm.Refs.PromptKind.Should().Be(RefPromptKind.None);
            HasNode(vm, RefNodeKind.LocalBranch, "feat").Should().BeFalse();
            RefNodeViewModel renamed = Node(vm, RefNodeKind.LocalBranch, "feat2");
            renamed.Track!.Upstream.Should().Be("origin/feat");
            Git(scenario.Alpha, "config", "branch.feat2.remote").Should().Be("origin");
            Git(scenario.Alpha, "config", "branch.feat2.merge").Should().Be("refs/heads/feat");
        }
    }

    [AvaloniaTest]
    public async Task Rename_the_current_branch_updates_head()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            vm.Refs.RequestRenameCommand.Execute(Node(vm, RefNodeKind.LocalBranch, "main"));
            await ConfirmAsync(vm, "trunk");

            Git(scenario.Alpha, "rev-parse", "--abbrev-ref", "HEAD").Should().Be("trunk");
            Node(vm, RefNodeKind.LocalBranch, "trunk").IsCurrent.Should().BeTrue();
            vm.Branch.Should().Be("trunk");
            vm.Refs.CurrentTrack!.Upstream.Should().Be("origin/main");
        }
    }

    [AvaloniaTest]
    public async Task Rename_rejects_invalid_and_existing_names_and_keeps_the_prompt_open()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            Git(scenario.Alpha, "branch", "other");
            await vm.OpenAsync(scenario.Alpha);
            vm.Refs.RequestRenameCommand.Execute(Node(vm, RefNodeKind.LocalBranch, "other"));

            foreach (string bad in new[] { "a..b", "has space", "x.lock", "main", "" })
            {
                await ConfirmAsync(vm, bad);
                vm.Refs.PromptKind.Should().Be(RefPromptKind.RenameBranch, bad);
                vm.Refs.HasValidationError.Should().BeTrue(bad);
            }

            Git(scenario.Alpha, "branch", "--list", "other").Should().Contain("other");
        }
    }

    // ---- delete ----

    [AvaloniaTest]
    public async Task Delete_removes_a_merged_branch_without_prompting()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            Git(scenario.Alpha, "branch", "merged");
            await vm.OpenAsync(scenario.Alpha);

            await vm.Refs.DeleteBranchCommand.ExecuteAsync(Node(vm, RefNodeKind.LocalBranch, "merged"));

            vm.Refs.PromptKind.Should().Be(RefPromptKind.None);
            HasNode(vm, RefNodeKind.LocalBranch, "merged").Should().BeFalse();
            Git(scenario.Alpha, "branch", "--list", "merged").Should().BeEmpty();
        }
    }

    [AvaloniaTest]
    public async Task Delete_of_an_unmerged_branch_asks_and_names_the_lost_commits()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            Git(scenario.Alpha, "checkout", "-q", "-b", "wip");
            RemoteScenario.Commit(scenario.Alpha, "w1.txt", "1", "wip one");
            RemoteScenario.Commit(scenario.Alpha, "w2.txt", "2", "wip two");
            Git(scenario.Alpha, "checkout", "-q", "main");
            await vm.OpenAsync(scenario.Alpha);

            await vm.Refs.DeleteBranchCommand.ExecuteAsync(Node(vm, RefNodeKind.LocalBranch, "wip"));

            vm.Refs.PromptKind.Should().Be(RefPromptKind.ForceDeleteBranch);
            vm.Refs.PromptDetail.Should().Contain("2 commits will be lost");
            vm.Refs.ConfirmText.Should().Be("Force delete");
            Git(scenario.Alpha, "branch", "--list", "wip").Should().Contain("wip", "nothing is deleted before the confirmation");

            await ConfirmAsync(vm);

            vm.Refs.PromptKind.Should().Be(RefPromptKind.None);
            Git(scenario.Alpha, "branch", "--list", "wip").Should().BeEmpty();
            HasNode(vm, RefNodeKind.LocalBranch, "wip").Should().BeFalse();
        }
    }

    [AvaloniaTest]
    public async Task Cancelling_the_force_delete_keeps_the_branch()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            Git(scenario.Alpha, "checkout", "-q", "-b", "wip");
            RemoteScenario.Commit(scenario.Alpha, "w1.txt", "1", "wip one");
            Git(scenario.Alpha, "checkout", "-q", "main");
            await vm.OpenAsync(scenario.Alpha);
            await vm.Refs.DeleteBranchCommand.ExecuteAsync(Node(vm, RefNodeKind.LocalBranch, "wip"));
            vm.Refs.PromptDetail.Should().Contain("1 commit will be lost");

            vm.Refs.CancelPromptCommand.Execute(null);

            vm.Refs.IsPromptVisible.Should().BeFalse();
            Git(scenario.Alpha, "branch", "--list", "wip").Should().Contain("wip");
        }
    }

    [AvaloniaTest]
    public async Task Delete_refuses_the_current_branch_with_a_message()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            await vm.Refs.DeleteBranchCommand.ExecuteAsync(Node(vm, RefNodeKind.LocalBranch, "main"));

            vm.ErrorMessage.Should().Contain("current branch");
            vm.Refs.PromptKind.Should().Be(RefPromptKind.None);
            Git(scenario.Alpha, "branch", "--list", "main").Should().Contain("main");
        }
    }

    // ---- upstream ----

    [AvaloniaTest]
    public async Task Set_and_unset_upstream()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            Git(scenario.Alpha, "branch", "topic");
            await vm.OpenAsync(scenario.Alpha);
            RefNodeViewModel topic = Node(vm, RefNodeKind.LocalBranch, "topic");
            topic.HasTrack.Should().BeFalse();

            vm.Refs.RequestSetUpstreamCommand.Execute(topic);
            vm.Refs.PromptKind.Should().Be(RefPromptKind.SetUpstream);
            vm.Refs.UpstreamChoices.Should().Contain("origin/main");
            vm.Refs.SelectedUpstream = "origin/main";
            await ConfirmAsync(vm);

            Git(scenario.Alpha, "config", "branch.topic.merge").Should().Be("refs/heads/main");
            Node(vm, RefNodeKind.LocalBranch, "topic").Track!.Upstream.Should().Be("origin/main");

            await vm.Refs.UnsetUpstreamCommand.ExecuteAsync(Node(vm, RefNodeKind.LocalBranch, "topic"));

            Node(vm, RefNodeKind.LocalBranch, "topic").HasTrack.Should().BeFalse();
            Git(scenario.Alpha, "config", "--local", "--list").Should().NotContain("branch.topic.merge");
        }
    }

    [AvaloniaTest]
    public async Task Push_without_upstream_sets_it()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            Git(scenario.Alpha, "checkout", "-q", "-b", "topic");
            RemoteScenario.Commit(scenario.Alpha, "t.txt", "t", "topic commit");
            await vm.OpenAsync(scenario.Alpha);

            await vm.Refs.PushBranchCommand.ExecuteAsync(Node(vm, RefNodeKind.LocalBranch, "topic"));

            Git(scenario.Origin, "rev-parse", "topic").Should().Be(Git(scenario.Alpha, "rev-parse", "topic"));
            Git(scenario.Alpha, "config", "branch.topic.remote").Should().Be("origin");
            RefNodeViewModel node = Node(vm, RefNodeKind.LocalBranch, "topic");
            node.Track!.InSync.Should().BeTrue();
        }
    }

    [AvaloniaTest]
    public async Task Push_of_another_branch_with_upstream_pushes_that_branch()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            Git(scenario.Alpha, "checkout", "-q", "-b", "topic");
            Git(scenario.Alpha, "push", "-q", "-u", "origin", "topic");
            RemoteScenario.Commit(scenario.Alpha, "t.txt", "t", "topic commit");
            Git(scenario.Alpha, "checkout", "-q", "main");
            await vm.OpenAsync(scenario.Alpha);
            Node(vm, RefNodeKind.LocalBranch, "topic").Track!.Ahead.Should().Be(1);

            await vm.Refs.PushBranchCommand.ExecuteAsync(Node(vm, RefNodeKind.LocalBranch, "topic"));

            Git(scenario.Origin, "rev-parse", "topic").Should().Be(Git(scenario.Alpha, "rev-parse", "topic"));
            Node(vm, RefNodeKind.LocalBranch, "topic").Track!.InSync.Should().BeTrue();
        }
    }

    [AvaloniaTest]
    public async Task Checkout_remote_branch_creates_a_tracking_branch()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            Git(scenario.Beta, "checkout", "-q", "-b", "feature/y");
            RemoteScenario.Commit(scenario.Beta, "y.txt", "y", "feature y");
            Git(scenario.Beta, "push", "-q", "origin", "feature/y");
            Git(scenario.Alpha, "fetch", "-q");
            await vm.OpenAsync(scenario.Alpha);

            await vm.Refs.CheckoutRemoteAsTrackingCommand.ExecuteAsync(Node(vm, RefNodeKind.RemoteBranch, "origin/feature/y"));

            Git(scenario.Alpha, "rev-parse", "--abbrev-ref", "HEAD").Should().Be("feature/y");
            Git(scenario.Alpha, "rev-parse", "--abbrev-ref", "@{upstream}").Should().Be("origin/feature/y");
            Node(vm, RefNodeKind.LocalBranch, "feature/y").IsCurrent.Should().BeTrue();
        }
    }

    [AvaloniaTest]
    public async Task Delete_remote_branch_needs_confirmation()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            Git(scenario.Alpha, "branch", "gone-soon");
            Git(scenario.Alpha, "push", "-q", "origin", "gone-soon");
            await vm.OpenAsync(scenario.Alpha);

            vm.Refs.RequestDeleteRemoteBranchCommand.Execute(Node(vm, RefNodeKind.RemoteBranch, "origin/gone-soon"));

            vm.Refs.PromptKind.Should().Be(RefPromptKind.DeleteRemoteBranch);
            vm.Refs.PromptTitle.Should().Contain("gone-soon").And.Contain("origin");
            Git(scenario.Origin, "branch", "--list", "gone-soon").Should().Contain("gone-soon");

            await ConfirmAsync(vm);

            Git(scenario.Origin, "branch", "--list", "gone-soon").Should().BeEmpty();
            HasNode(vm, RefNodeKind.RemoteBranch, "origin/gone-soon").Should().BeFalse();
        }
    }

    [AvaloniaTest]
    public async Task Fetch_remote_updates_remote_branches()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            Git(scenario.Beta, "push", "-q", "origin", "main:newbranch");

            await vm.Refs.FetchRemoteCommand.ExecuteAsync(Node(vm, RefNodeKind.RemoteBranch, "origin/main"));

            HasNode(vm, RefNodeKind.RemoteBranch, "origin/newbranch").Should().BeTrue();
        }
    }

    // ---- tags ----

    [AvaloniaTest]
    public async Task Create_a_lightweight_tag_at_head_and_delete_it_after_confirmation()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            vm.Refs.RequestNewTagCommand.Execute(null);
            vm.Refs.StartPoint.Should().Be("HEAD");
            await ConfirmAsync(vm, "light");

            Git(scenario.Alpha, "cat-file", "-t", "light").Should().Be("commit");
            Git(scenario.Alpha, "rev-parse", "light").Should().Be(Git(scenario.Alpha, "rev-parse", "HEAD"));

            vm.Refs.RequestDeleteTagCommand.Execute(Node(vm, RefNodeKind.Tag, "light"));
            vm.Refs.PromptKind.Should().Be(RefPromptKind.DeleteTag);
            Git(scenario.Alpha, "tag", "--list", "light").Should().Contain("light", "not deleted before confirmation");
            await ConfirmAsync(vm);

            Git(scenario.Alpha, "tag", "--list", "light").Should().BeEmpty();
            HasNode(vm, RefNodeKind.Tag, "light").Should().BeFalse();
        }
    }

    [AvaloniaTest]
    public async Task Create_an_annotated_tag_at_a_chosen_commit_with_a_message()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            string first = Git(scenario.Alpha, "rev-parse", "HEAD");
            RemoteScenario.Commit(scenario.Alpha, "b.txt", "b", "second");
            await vm.OpenAsync(scenario.Alpha);

            vm.SelectedCommit = vm.Commits.Single(c => c.ObjectId.ToString() == first);
            vm.Refs.RequestNewTagCommand.Execute(null);
            vm.Refs.StartPoint.Should().Be(first, "the selected commit is the default start point");
            vm.Refs.MessageText = "Release \"one\"\nsecond line";
            await ConfirmAsync(vm, "v1");

            Git(scenario.Alpha, "cat-file", "-t", "v1").Should().Be("tag");
            Git(scenario.Alpha, "rev-parse", "v1^{commit}").Should().Be(first);
            Git(scenario.Alpha, "tag", "-l", "--format=%(contents)", "v1").Should().Be("Release \"one\"\nsecond line");
        }
    }

    [AvaloniaTest]
    public async Task Push_tag_and_push_all_tags()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            Git(scenario.Alpha, "tag", "t1");
            Git(scenario.Alpha, "tag", "t2");
            await vm.OpenAsync(scenario.Alpha);

            await vm.Refs.PushTagCommand.ExecuteAsync(Node(vm, RefNodeKind.Tag, "t1"));
            Git(scenario.Origin, "tag", "--list").Should().Be("t1");

            vm.Refs.RequestPushAllTagsCommand.Execute(null);
            vm.Refs.PromptKind.Should().Be(RefPromptKind.PushAllTags);
            Git(scenario.Origin, "tag", "--list").Should().Be("t1", "nothing is pushed before the confirmation");
            await ConfirmAsync(vm);

            Git(scenario.Origin, "tag", "--list").Split('\n').Should().BeEquivalentTo("t1", "t2");
        }
    }

    [AvaloniaTest]
    public async Task Duplicate_tag_names_are_rejected()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            Git(scenario.Alpha, "tag", "dup");
            await vm.OpenAsync(scenario.Alpha);
            vm.Refs.RequestNewTagCommand.Execute(null);

            await ConfirmAsync(vm, "dup");

            vm.Refs.ValidationError.Should().Contain("already exists");
            vm.Refs.PromptKind.Should().Be(RefPromptKind.NewTag);
        }
    }

    // ---- new branch ----

    [AvaloniaTest]
    public async Task New_branch_at_selected_commit_with_and_without_checkout()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            string first = Git(scenario.Alpha, "rev-parse", "HEAD");
            RemoteScenario.Commit(scenario.Alpha, "b.txt", "b", "second");
            await vm.OpenAsync(scenario.Alpha);
            vm.SelectedCommit = vm.Commits.Single(c => c.ObjectId.ToString() == first);

            vm.Refs.RequestNewBranchCommand.Execute(null);
            vm.Refs.StartPoint.Should().Be(first);
            vm.Refs.CheckoutAfterCreate = false;
            await ConfirmAsync(vm, "here");
            Git(scenario.Alpha, "rev-parse", "here").Should().Be(first);
            Git(scenario.Alpha, "rev-parse", "--abbrev-ref", "HEAD").Should().Be("main");

            vm.Refs.RequestNewBranchCommand.Execute(Node(vm, RefNodeKind.LocalBranch, "here"));
            vm.Refs.StartPoint.Should().Be("here");
            vm.Refs.CheckoutAfterCreate.Should().BeTrue();
            await ConfirmAsync(vm, "feature/from-here");
            Git(scenario.Alpha, "rev-parse", "--abbrev-ref", "HEAD").Should().Be("feature/from-here");
            Git(scenario.Alpha, "rev-parse", "HEAD").Should().Be(first);
        }
    }

    [AvaloniaTest]
    public async Task New_branch_rejects_bad_names_and_unknown_start_points()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            vm.Refs.RequestNewBranchCommand.Execute(null);

            await ConfirmAsync(vm, "bad name");
            vm.Refs.HasValidationError.Should().BeTrue();

            vm.Refs.StartPoint = "no-such-ref";
            await ConfirmAsync(vm, "fine");
            vm.Refs.ValidationError.Should().Contain("not a commit");
            Git(scenario.Alpha, "branch", "--list", "fine").Should().BeEmpty();
        }
    }

    // ---- ahead / behind ----

    [AvaloniaTest]
    public async Task Ahead_behind_numbers_show_on_branches_and_for_the_current_branch()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            RemoteScenario.Commit(scenario.Alpha, "l1.txt", "1", "local 1");
            RemoteScenario.Commit(scenario.Alpha, "l2.txt", "2", "local 2");
            RemoteScenario.Commit(scenario.Beta, "r1.txt", "1", "remote 1");
            Git(scenario.Beta, "push", "-q", "origin", "main");
            Git(scenario.Alpha, "fetch", "-q");

            await vm.OpenAsync(scenario.Alpha);

            RefNodeViewModel main = Node(vm, RefNodeKind.LocalBranch, "main");
            (main.Track!.Ahead, main.Track.Behind).Should().Be((2, 1));
            main.TrackText.Should().Be("↑2 ↓1");
            main.TrackToolTip.Should().Contain("2 commits not pushed").And.Contain("1 commit not pulled");
            vm.Refs.CurrentTrackText.Should().Be("↑2 ↓1");
            vm.Refs.HasCurrentTrack.Should().BeTrue();
        }
    }

    [AvaloniaTest]
    public async Task A_branch_without_upstream_has_no_marker()
    {
        using TestRepository repository = new();
        MainWindowViewModel vm = CreateViewModel();

        await vm.OpenAsync(repository.Path);

        Node(vm, RefNodeKind.LocalBranch, "main").HasTrack.Should().BeFalse();
        vm.Refs.HasCurrentTrack.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Deleting_the_remote_branch_marks_the_upstream_as_gone()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            Git(scenario.Alpha, "checkout", "-q", "-b", "feat");
            Git(scenario.Alpha, "push", "-q", "-u", "origin", "feat");
            Git(scenario.Beta, "push", "-q", "origin", "--delete", "feat");
            Git(scenario.Alpha, "fetch", "-q", "--prune");

            await vm.OpenAsync(scenario.Alpha);

            RefNodeViewModel feat = Node(vm, RefNodeKind.LocalBranch, "feat");
            feat.TrackIsGone.Should().BeTrue();
            feat.TrackText.Should().Be("gone");
            feat.TrackToolTip.Should().Contain("no longer exists");
            vm.Refs.CurrentTrackText.Should().Be("gone");
        }
    }

    // ---- screenshots ----

    private static void Screenshot(Window window, string name)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, name);
        window.CaptureRenderedFrame()!.Save(file);
        TestContext.AddTestAttachment(file);
        TestContext.Out.WriteLine(file);
    }

    [AvaloniaTest]
    public async Task Sidebar_screenshots_with_markers_and_an_open_confirmation([Values("light", "dark")] string theme)
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAlphaAsync();
        using (scenario)
        {
            RemoteScenario.Commit(scenario.Alpha, "l1.txt", "1", "local 1");
            RemoteScenario.Commit(scenario.Beta, "r1.txt", "1", "remote 1");
            Git(scenario.Beta, "push", "-q", "origin", "main");
            Git(scenario.Alpha, "branch", "synced", "origin/main");
            Git(scenario.Alpha, "checkout", "-q", "-b", "gone-branch");
            Git(scenario.Alpha, "push", "-q", "-u", "origin", "gone-branch");
            Git(scenario.Beta, "push", "-q", "origin", "--delete", "gone-branch");
            Git(scenario.Alpha, "checkout", "-q", "-b", "wip");
            RemoteScenario.Commit(scenario.Alpha, "w1.txt", "1", "wip one");
            RemoteScenario.Commit(scenario.Alpha, "w2.txt", "2", "wip two");
            Git(scenario.Alpha, "checkout", "-q", "main");
            Git(scenario.Alpha, "tag", "v1.0");
            Git(scenario.Alpha, "fetch", "-q", "--prune");

            MainWindow window = new() { DataContext = vm, Width = 1000, Height = 600, RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light };
            window.Show();
            await vm.OpenAsync(scenario.Alpha);
            Screenshot(window, $"branches-sidebar-{theme}.png");

            await vm.Refs.DeleteBranchCommand.ExecuteAsync(Node(vm, RefNodeKind.LocalBranch, "wip"));
            vm.Refs.PromptKind.Should().Be(RefPromptKind.ForceDeleteBranch);
            Screenshot(window, $"branches-confirm-{theme}.png");

            vm.Refs.RequestNewTagCommand.Execute(null);
            vm.Refs.InputText = "v2.0";
            vm.Refs.MessageText = "Second release";
            Screenshot(window, $"branches-new-tag-{theme}.png");
            window.Close();
        }
    }
}
