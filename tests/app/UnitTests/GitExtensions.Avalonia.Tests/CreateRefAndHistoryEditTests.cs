using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;
using GitExtensions.Desktop.Views.ContextMenus;

namespace GitExtensionsDesktopTests;

/// <summary>Create branch (checkout and local changes choices), create tag (push), and the history editing dialog.</summary>
[TestFixture]
public class CreateRefAndHistoryEditTests
{
    [SetUp]
    public void CloseLeakedDialogs()
    {
        foreach (DialogWindow leaked in DialogWindow.Open.ToList())
        {
            leaked.Close();
        }
    }

    private static string Git(string directory, params string[] arguments) => RemoteScenario.Run(directory, arguments);

    // main: C1 (a.txt=one, b.txt=bee) -> C2 (a.txt=two); the tag "first" marks C1.
    private static async Task<(RemoteScenario Scenario, MainWindowViewModel ViewModel)> OpenAsync()
    {
        RemoteScenario scenario = new();
        RemoteScenario.Commit(scenario.Alpha, "b.txt", "bee", "Add b.txt");
        Git(scenario.Alpha, "tag", "first");
        RemoteScenario.Commit(scenario.Alpha, "a.txt", "two", "Change a.txt");
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoPicker());
        await viewModel.OpenAsync(scenario.Alpha);
        return (scenario, viewModel);
    }

    private static CommitItemViewModel Commit(MainWindowViewModel vm, string subject) => vm.Commits.Single(c => c.Subject == subject);

    private static string Head(string directory) => Git(directory, "rev-parse", "--abbrev-ref", "HEAD");

    // ---- create branch ----

    [AvaloniaTest]
    public async Task Create_branch_checks_out_by_default_and_refuses_inline_when_local_changes_are_in_the_way()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAsync();
        using (scenario)
        {
            CommitItemViewModel first = Commit(vm, "Add b.txt");
            File.WriteAllText(Path.Combine(scenario.Alpha, "a.txt"), "dirty");
            vm.Actions.RequestCreateBranchCommand.Execute(first);
            vm.Actions.BranchOptions.CheckoutAfterCreate.Should().BeTrue("the original checks out after creating");
            vm.Actions.BranchOptions.IsKeep.Should().BeTrue();
            vm.Actions.InputText = "topic";
            await vm.Actions.ConfirmPromptAsync();

            vm.Actions.IsOpen.Should().BeTrue("git refuses to overwrite the local changes: the error is shown in the dialog");
            vm.Actions.DialogError.Should().Contain("local changes");
            Head(scenario.Alpha).Should().Be("main");

            // "Stash" re-applies the changes after the switch; it cannot, here, because the file differs: nothing is lost.
            vm.Actions.BranchOptions.IsReset = true;
            await vm.Actions.ConfirmPromptAsync();
            vm.Actions.IsOpen.Should().BeFalse();
            Head(scenario.Alpha).Should().Be("topic");
            File.ReadAllText(Path.Combine(scenario.Alpha, "a.txt")).Should().Be("one", "reset discards the changes");
        }
    }

    [AvaloniaTest]
    public async Task Create_branch_without_checkout_keeps_head()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAsync();
        using (scenario)
        {
            vm.Actions.RequestCreateBranchCommand.Execute(Commit(vm, "Add b.txt"));
            vm.Actions.BranchOptions.CheckoutAfterCreate = false;
            vm.Actions.BranchOptions.ShowLocalChanges.Should().BeFalse();
            vm.Actions.InputText = "topic";
            await vm.Actions.ConfirmPromptAsync();

            Head(scenario.Alpha).Should().Be("main");
            Git(scenario.Alpha, "rev-parse", "topic").Should().Be(Git(scenario.Alpha, "rev-parse", "first"));
        }
    }

    [AvaloniaTest]
    public async Task Refs_create_branch_merge_stash_and_reset_choices_handle_local_changes()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAsync();
        using (scenario)
        {
            string b = Path.Combine(scenario.Alpha, "b.txt");
            string a = Path.Combine(scenario.Alpha, "a.txt");

            // Merge: the local change of an unrelated file travels along.
            File.WriteAllText(b, "dirty b");
            vm.Refs.RequestNewBranchCommand.Execute(null);
            vm.Refs.StartPoint = "first";
            vm.Refs.BranchOptions.IsMerge = true;
            vm.Refs.InputText = "merged";
            await vm.Refs.ConfirmPromptAsync();
            vm.Refs.IsOpen.Should().BeFalse();
            Head(scenario.Alpha).Should().Be("merged");
            File.ReadAllText(b).Should().Be("dirty b");
            Git(scenario.Alpha, "checkout", "-q", "--force", "main");

            // Stash: stashed, switched, re-applied; the stash list ends up empty.
            File.WriteAllText(b, "dirty b");
            vm.Refs.RequestNewBranchCommand.Execute(null);
            vm.Refs.StartPoint = "first";
            vm.Refs.BranchOptions.IsStash = true;
            vm.Refs.InputText = "stashed";
            await vm.Refs.ConfirmPromptAsync();
            Head(scenario.Alpha).Should().Be("stashed");
            File.ReadAllText(b).Should().Be("dirty b");
            Git(scenario.Alpha, "stash", "list").Should().BeEmpty();
            Git(scenario.Alpha, "checkout", "-q", "--force", "main");

            // Stash with a change that conflicts after the switch: the branch exists, the error says the stash is kept.
            File.WriteAllText(a, "dirty a");
            vm.Refs.RequestNewBranchCommand.Execute(null);
            vm.Refs.StartPoint = "first";
            vm.Refs.BranchOptions.IsStash = true;
            vm.Refs.InputText = "conflicting";
            await vm.Refs.ConfirmPromptAsync();
            vm.Refs.IsOpen.Should().BeTrue();
            vm.Refs.ValidationError.Should().Contain("stash");
            Head(scenario.Alpha).Should().Be("conflicting");
        }
    }

    [AvaloniaTest]
    public async Task Create_orphan_branch_has_no_commits()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAsync();
        using (scenario)
        {
            vm.Refs.RequestNewBranchCommand.Execute(null);
            vm.Refs.BranchOptions.Orphan = true;
            vm.Refs.BranchOptions.ClearOrphan = true;
            vm.Refs.BranchOptions.ShowLocalChanges.Should().BeFalse();
            vm.Refs.InputText = "fresh";
            await vm.Refs.ConfirmPromptAsync();

            vm.Refs.IsOpen.Should().BeFalse();
            Git(scenario.Alpha, "symbolic-ref", "--short", "HEAD").Should().Be("fresh");
            Git(scenario.Alpha, "ls-files").Should().BeEmpty();
            FluentActions.Invoking(() => Git(scenario.Alpha, "rev-parse", "--verify", "HEAD")).Should().Throw<InvalidOperationException>();
        }
    }

    // ---- create tag ----

    [AvaloniaTest]
    public async Task Create_tag_can_push_it_and_closes_on_success()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAsync();
        using (scenario)
        {
            CommitItemViewModel first = Commit(vm, "Add b.txt");
            vm.Actions.RequestCreateTagCommand.Execute(first);
            vm.Actions.TagOptions.HasRemotes.Should().BeTrue();
            vm.Actions.TagOptions.SelectedRemote.Should().Be("origin");
            vm.Actions.TagOptions.PushAfterCreate.Should().BeFalse("publishing is opt-in");
            vm.Actions.TagOptions.PushAfterCreate = true;
            vm.Actions.TagOptions.Message = "Release one";
            vm.Actions.InputText = "v1";
            await vm.Actions.ConfirmPromptAsync();

            vm.Actions.IsOpen.Should().BeFalse(vm.Actions.DialogError);
            Git(scenario.Alpha, "cat-file", "-t", "v1").Should().Be("tag");
            Git(scenario.Origin, "rev-parse", "v1^{commit}").Should().Be(first.ObjectId.ToString());

            // The refs tree dialog does the same.
            vm.Refs.RequestNewTagCommand.Execute(null);
            vm.Refs.TagOptions.PushAfterCreate = true;
            vm.Refs.InputText = "v2";
            await vm.Refs.ConfirmPromptAsync();
            vm.Refs.IsOpen.Should().BeFalse(vm.Refs.ValidationError);
            Git(scenario.Origin, "tag", "--list").Split('\n').Should().BeEquivalentTo("v1", "v2");
        }
    }

    [AvaloniaTest]
    public async Task A_failed_tag_push_stays_in_the_dialog_and_can_be_retried()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAsync();
        using (scenario)
        {
            string url = Git(scenario.Alpha, "remote", "get-url", "origin");
            Git(scenario.Alpha, "remote", "set-url", "origin", Path.Combine(scenario.Alpha, "no-such-remote"));
            vm.Refs.RequestNewTagCommand.Execute(null);
            vm.Refs.TagOptions.PushAfterCreate = true;
            vm.Refs.InputText = "v3";
            await vm.Refs.ConfirmPromptAsync();

            vm.Refs.IsOpen.Should().BeTrue();
            vm.Refs.ValidationError.Should().Contain("was created, but pushing it to origin failed");
            Git(scenario.Alpha, "tag", "--list", "v3").Should().Be("v3");

            Git(scenario.Alpha, "remote", "set-url", "origin", url);
            await vm.Refs.ConfirmPromptAsync();
            vm.Refs.IsOpen.Should().BeFalse(vm.Refs.ValidationError);
            Git(scenario.Origin, "tag", "--list", "v3").Should().Be("v3");
        }
    }

    [AvaloniaTest]
    public async Task Create_tag_without_remotes_disables_the_push_and_a_signed_tag_needs_a_message()
    {
        using TestRepository repository = new();
        repository.RunGit("stash", "--include-untracked");
        MainWindowViewModel vm = new(GitEnvironment.Initialize(), new NoPicker());
        await vm.OpenAsync(repository.Path);
        vm.Refs.RequestNewTagCommand.Execute(null);

        vm.Refs.TagOptions.HasRemotes.Should().BeFalse();
        vm.Refs.TagOptions.RemotesNote.Should().Contain("no remotes");
        vm.Refs.TagOptions.PushAfterCreate = true;
        vm.Refs.TagOptions.PushTarget.Should().BeNull();

        vm.Refs.TagOptions.Sign = true;
        vm.Refs.InputText = "signed";
        await vm.Refs.ConfirmPromptAsync();
        vm.Refs.ValidationError.Should().Contain("needs a message");
    }

    // ---- history editing ----

    [AvaloniaTest]
    public async Task Fixup_and_squash_commits_need_staged_changes_and_target_the_commit()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAsync();
        using (scenario)
        {
            CommitItemViewModel first = Commit(vm, "Add b.txt");
            await vm.OpenHistoryEditAsync(HistoryEditKind.Fixup, first);
            await vm.HistoryEdit.ConfirmAsync();
            vm.HistoryEdit.Error.Should().Contain("Nothing is staged");
            vm.HistoryEdit.IsOpen.Should().BeTrue();

            File.WriteAllText(Path.Combine(scenario.Alpha, "b.txt"), "bee 2");
            Git(scenario.Alpha, "add", "b.txt");
            await vm.HistoryEdit.ConfirmAsync();
            vm.HistoryEdit.IsOpen.Should().BeFalse(vm.HistoryEdit.Error);
            Git(scenario.Alpha, "log", "-1", "--format=%s").Should().Be("fixup! Add b.txt");

            File.WriteAllText(Path.Combine(scenario.Alpha, "b.txt"), "bee 3");
            Git(scenario.Alpha, "add", "b.txt");
            await vm.OpenHistoryEditAsync(HistoryEditKind.Squash, first);
            await vm.HistoryEdit.ConfirmAsync();
            Git(scenario.Alpha, "log", "-1", "--format=%s").Should().Be("squash! Add b.txt");
        }
    }

    [AvaloniaTest]
    public async Task Fixup_and_squash_rebase_folds_the_staged_change_into_the_commit()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAsync();
        using (scenario)
        {
            File.WriteAllText(Path.Combine(scenario.Alpha, "b.txt"), "bee fixed");
            Git(scenario.Alpha, "add", "b.txt");
            await vm.OpenHistoryEditAsync(HistoryEditKind.FixupAndRebase, Commit(vm, "Add b.txt"));
            await vm.HistoryEdit.ConfirmAsync();

            vm.HistoryEdit.IsOpen.Should().BeFalse(vm.HistoryEdit.Error);
            Git(scenario.Alpha, "log", "--format=%s").Split('\n').Should().Equal("Change a.txt", "Add b.txt", "Add a.txt");
            Git(scenario.Alpha, "show", "HEAD~1:b.txt").Should().Be("bee fixed");
        }
    }

    [AvaloniaTest]
    public async Task Reword_edit_and_autosquash_rewrite_history()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAsync();
        using (scenario)
        {
            await vm.OpenHistoryEditAsync(HistoryEditKind.Reword, Commit(vm, "Add b.txt"));
            vm.HistoryEdit.Message.Should().Be("Add b.txt");
            vm.HistoryEdit.Message = "Add b.txt, reworded\n\nWith a body";
            await vm.HistoryEdit.ConfirmAsync();
            vm.HistoryEdit.IsOpen.Should().BeFalse(vm.HistoryEdit.Error);
            Git(scenario.Alpha, "log", "--format=%s").Split('\n').Should().Equal("Change a.txt", "Add b.txt, reworded", "Add a.txt");
            Git(scenario.Alpha, "log", "-1", "--skip=1", "--format=%b").Should().Be("With a body");

            // Edit: the rebase stops at the commit; the operation bar continues it.
            await vm.OpenAsync(scenario.Alpha);
            await vm.OpenHistoryEditAsync(HistoryEditKind.Edit, Commit(vm, "Add b.txt, reworded"));
            await vm.HistoryEdit.ConfirmAsync();
            vm.HistoryEdit.IsOpen.Should().BeFalse(vm.HistoryEdit.Error);
            RepositoryOperationState.Detect(Path.Combine(scenario.Alpha, ".git")).Should().Be(OperationKind.Rebase);
            Git(scenario.Alpha, "log", "-1", "--format=%s").Should().Be("Add b.txt, reworded");
            Git(scenario.Alpha, "rebase", "--abort");

            // Autosquash: a fixup commit created before is folded in.
            File.WriteAllText(Path.Combine(scenario.Alpha, "b.txt"), "bee 4");
            Git(scenario.Alpha, "commit", "-q", "-a", "--fixup", "HEAD~1");
            await vm.OpenAsync(scenario.Alpha);
            await vm.OpenHistoryEditAsync(HistoryEditKind.Autosquash, Commit(vm, "Add b.txt, reworded"));
            await vm.HistoryEdit.ConfirmAsync();
            vm.HistoryEdit.IsOpen.Should().BeFalse(vm.HistoryEdit.Error);
            Git(scenario.Alpha, "log", "--format=%s").Split('\n').Should().Equal("Change a.txt", "Add b.txt, reworded", "Add a.txt");
        }
    }

    [AvaloniaTest]
    public async Task Amend_commit_carries_the_new_message()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAsync();
        using (scenario)
        {
            await vm.OpenHistoryEditAsync(HistoryEditKind.Amend, Commit(vm, "Add b.txt"));
            vm.HistoryEdit.Message = "Better subject";
            await vm.HistoryEdit.ConfirmAsync();

            vm.HistoryEdit.IsOpen.Should().BeFalse(vm.HistoryEdit.Error);
            Git(scenario.Alpha, "log", "-1", "--format=%s").Should().Be("amend! Add b.txt");
            Git(scenario.Alpha, "log", "-1", "--format=%b").Should().Be("Better subject");

            // The autosquash rebase turns it into the new message of the commit.
            await vm.OpenAsync(scenario.Alpha);
            await vm.OpenHistoryEditAsync(HistoryEditKind.Autosquash, Commit(vm, "Add b.txt"));
            await vm.HistoryEdit.ConfirmAsync();
            Git(scenario.Alpha, "log", "--format=%s").Split('\n').Should().Equal("Change a.txt", "Better subject", "Add a.txt");
        }
    }

    [AvaloniaTest]
    public async Task Reset_another_branch_moves_it_and_tells_what_it_loses()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAsync();
        using (scenario)
        {
            Git(scenario.Alpha, "branch", "other");
            await vm.OpenAsync(scenario.Alpha);
            CommitItemViewModel first = Commit(vm, "Add b.txt");
            await vm.OpenHistoryEditAsync(HistoryEditKind.ResetBranch, first);
            vm.HistoryEdit.Choices.Should().Equal("other");
            vm.HistoryEdit.SelectedChoice = "other";
            for (int i = 0; i < 100 && !vm.HistoryEdit.HasNote; i++)
            {
                await Task.Delay(20);
            }

            vm.HistoryEdit.Note.Should().Contain("1 commit that is not in the history");
            await vm.HistoryEdit.ConfirmAsync();

            vm.HistoryEdit.IsOpen.Should().BeFalse(vm.HistoryEdit.Error);
            Git(scenario.Alpha, "rev-parse", "other").Should().Be(first.ObjectId.ToString());
            Head(scenario.Alpha).Should().Be("main");
        }
    }

    [AvaloniaTest]
    public async Task Compare_with_a_revision_a_base_and_the_working_directory_uses_the_diff_tab()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAsync();
        using (scenario)
        {
            CommitItemViewModel first = Commit(vm, "Add b.txt");
            CommitItemViewModel head = Commit(vm, "Change a.txt");
            vm.SelectedCommit = first;
            RevisionDiffViewModel diff = vm.RevisionTabs.Diff;

            await vm.OpenHistoryEditAsync(HistoryEditKind.CompareWithRevision, first);
            vm.HistoryEdit.SelectedChoice = "main";
            await vm.HistoryEdit.ConfirmAsync();
            vm.HistoryEdit.IsOpen.Should().BeFalse();
            vm.RevisionTabs.SelectedTabIndex.Should().Be(RevisionTabsViewModel.DiffTab);
            await WaitAsync(() => diff.Files.Count == 1);
            diff.Files[0].Name.Should().Be("a.txt");

            await vm.OpenHistoryEditAsync(HistoryEditKind.CompareWithRevision, first);
            vm.HistoryEdit.SelectedChoice = "no-such-ref";
            await vm.HistoryEdit.ConfirmAsync();
            vm.HistoryEdit.Error.Should().Contain("is not a commit");
            vm.HistoryEdit.CloseCommand.Execute(null);

            File.WriteAllText(Path.Combine(scenario.Alpha, "b.txt"), "worktree");
            vm.CompareWithWorkingDirectory(first);
            await WaitAsync(() => diff.Files.Count == 2);
            diff.Files.Select(f => f.Name).Should().BeEquivalentTo("a.txt", "b.txt");

            vm.CompareBase = first;
            vm.CompareToBase(head);
            await WaitAsync(() => diff.Files.Count == 1 && diff.Title.Contains('↔'));

            List<Avalonia.Controls.Control> menu = RevisionGridContextMenu.Build(vm, head, null);
            MenuItem compare = menu.OfType<MenuItem>().Single(m => MenuKit.TextOf(m) == "Compare");
            MenuItem toBase = compare.Items.OfType<MenuItem>().Single(m => MenuKit.TextOf(m).StartsWith("Compare to BASE", StringComparison.Ordinal));
            toBase.IsEnabled.Should().BeTrue();
            compare.Items.OfType<MenuItem>().Single(m => MenuKit.TextOf(m) == "Compare selected commits").IsEnabled.Should().BeFalse();

            await vm.GoToMergeBaseAsync(first);
            vm.SelectedCommit!.Subject.Should().Be("Add b.txt");
        }
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(20);
            ShellTestSupport.Settle();
        }

        condition().Should().BeTrue();
    }

    // ---- file list commands ----

    [AvaloniaTest]
    public async Task Stop_tracking_and_delete_work_on_working_directory_files()
    {
        using TestRepository repository = new();
        repository.RunGit("stash", "--include-untracked");
        int refreshed = 0;
        FileMenuContext Context(string path) => new()
        {
            Paths = [path],
            WorkingDir = repository.Path,
            Stage = () => { },
            Changed = () =>
            {
                refreshed++;
                return Task.CompletedTask;
            },
        };

        MenuItem stop = FileListContextMenu.Build(Context("a.txt")).OfType<MenuItem>().Single(m => MenuKit.TextOf(m) == "Stop tracking the file");
        stop.IsEnabled.Should().BeTrue();
        (await FileCommands.StopTrackingAsync(repository.Path, ["a.txt"])).Should().BeNull();
        repository.GitOutput("ls-files").Should().NotContain("a.txt");
        File.Exists(Path.Combine(repository.Path, "a.txt")).Should().BeTrue("the file stays on disk");

        FileCommands.Delete(repository.Path, ["a.txt"]).Should().BeNull();
        File.Exists(Path.Combine(repository.Path, "a.txt")).Should().BeFalse();
        FileCommands.Delete(repository.Path, ["../outside.txt"]).Should().Contain("outside");

        FileCommands.StartWith("true", Path.Combine(repository.Path, "b.txt")).Should().BeNull();
        FileCommands.StartWith("no-such-program-xyz", "b.txt").Should().Contain("Could not start");
        refreshed.Should().Be(0);
    }

    // ---- rendered dialogs ----

    private static void Capture(DialogWindow dialog, string name)
    {
        foreach ((ThemeVariant variant, string suffix) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
        {
            Application.Current!.RequestedThemeVariant = variant;
            ShellTestSupport.Settle();
            string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(directory);
            dialog.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"createref-{name}-{suffix}.png"));
        }
    }

    [AvaloniaTest]
    public async Task The_dialogs_render_with_their_options()
    {
        (RemoteScenario scenario, MainWindowViewModel vm) = await OpenAsync();
        using (scenario)
        {
            MainWindow window = new() { DataContext = vm, Width = 1100, Height = 760 };
            window.Show();
            await vm.OpenAsync(scenario.Alpha);
            ShellTestSupport.Settle();
            try
            {
                vm.Actions.RequestCreateBranchCommand.Execute(vm.Commits[0]);
                ShellTestSupport.Settle();
                DialogWindow dialog = DialogWindow.Open.Should().ContainSingle().Which;
                dialog.Find<CheckBox>("BranchCheckout").IsChecked.Should().BeTrue();
                dialog.Find<RadioButton>("BranchKeep").IsChecked.Should().BeTrue();
                dialog.Find<CheckBox>("BranchOrphan").IsChecked.Should().BeFalse();
                dialog.Find<Control>("BranchLocalChanges").IsVisible.Should().BeTrue();
                Capture(dialog, "branch");
                vm.Actions.CancelPromptCommand.Execute(null);
                ShellTestSupport.Settle();

                vm.Actions.RequestCreateTagCommand.Execute(vm.Commits[0]);
                ShellTestSupport.Settle();
                dialog = DialogWindow.Open.Should().ContainSingle().Which;
                dialog.Find<CheckBox>("TagPush").IsEnabled.Should().BeTrue();
                dialog.Find<ComboBox>("TagRemote").SelectedItem.Should().Be("origin");
                Capture(dialog, "tag");
                vm.Actions.CancelPromptCommand.Execute(null);
                ShellTestSupport.Settle();

                vm.Refs.RequestNewTagCommand.Execute(null);
                ShellTestSupport.Settle();
                dialog = DialogWindow.Open.Should().ContainSingle().Which;
                dialog.Find<CheckBox>("TagSign").IsVisible.Should().BeTrue();
                vm.Refs.CancelPromptCommand.Execute(null);
                ShellTestSupport.Settle();

                await vm.OpenHistoryEditAsync(HistoryEditKind.Reword, vm.Commits[0]);
                ShellTestSupport.Settle();
                dialog = DialogWindow.Open.Should().ContainSingle().Which;
                dialog.Title.Should().Be("Reword commit");
                dialog.Find<TextBox>("HistoryEditMessage").Text.Should().Be("Change a.txt");
                Capture(dialog, "reword");
                vm.HistoryEdit.CloseCommand.Execute(null);
                ShellTestSupport.Settle();

                Git(scenario.Alpha, "branch", "other");
                await vm.OpenAsync(scenario.Alpha);
                await vm.OpenHistoryEditAsync(HistoryEditKind.ResetBranch, vm.Commits[1]);
                vm.HistoryEdit.SelectedChoice = "other";
                await WaitAsync(() => vm.HistoryEdit.HasNote);
                ShellTestSupport.Settle();
                dialog = DialogWindow.Open.Should().ContainSingle().Which;
                dialog.Title.Should().Be("Reset another branch");
                Capture(dialog, "reset-branch");
                vm.HistoryEdit.CloseCommand.Execute(null);
                ShellTestSupport.Settle();
                DialogWindow.Open.Should().BeEmpty();
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
                window.Close();
            }
        }
    }
}
