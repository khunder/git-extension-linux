using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class BranchDialogTests
{
    private static async Task<MainWindowViewModel> OpenAsync(TestRepository repository)
    {
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoPicker(), new FakeHistory());
        await viewModel.OpenAsync(repository.Path);
        await viewModel.Operation.RefreshAsync();
        return viewModel;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(20);
            ShellTestSupport.Settle();
        }

        condition().Should().BeTrue();
    }

    // ---- merge ----

    [AvaloniaTest]
    public async Task Merge_lists_branches_and_prefills_the_target()
    {
        using TestRepository repository = new();
        repository.AddCleanDivergedBranches();
        MainWindowViewModel viewModel = await OpenAsync(repository);

        await viewModel.OpenMergeDialogAsync("topic");

        viewModel.MergeDialog.IsOpen.Should().BeTrue();
        viewModel.MergeDialog.Target.Should().Be("topic");
        viewModel.MergeDialog.CurrentBranch.Should().Be("main");
        viewModel.MergeDialog.AllBranches.Should().Equal("topic");
        viewModel.MergeDialog.IsFastForward.Should().BeTrue();
    }

    [AvaloniaTest]
    public async Task Merge_no_ff_with_a_message_creates_a_merge_commit_and_closes()
    {
        using TestRepository repository = new();
        repository.AddCleanDivergedBranches();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        await viewModel.OpenMergeDialogAsync("topic");

        viewModel.MergeDialog.IsNoFastForward = true;
        viewModel.MergeDialog.Message = "Merge the topic work";
        await viewModel.MergeDialog.MergeCommand.ExecuteAsync(null);

        viewModel.MergeDialog.IsOpen.Should().BeFalse();
        repository.Parents().Should().HaveCount(2);
        repository.GitOutput("log", "-1", "--format=%s").Trim().Should().Be("Merge the topic work");
        viewModel.Status.Should().Be("Merged topic");
    }

    [AvaloniaTest]
    public async Task Merge_fast_forward_only_failure_stays_inline()
    {
        using TestRepository repository = new();
        repository.AddCleanDivergedBranches();
        string head = repository.Head();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        await viewModel.OpenMergeDialogAsync("topic");

        viewModel.MergeDialog.IsFastForwardOnly = true;
        await viewModel.MergeDialog.MergeCommand.ExecuteAsync(null);

        viewModel.MergeDialog.IsOpen.Should().BeTrue();
        viewModel.MergeDialog.HasError.Should().BeTrue();
        repository.Head().Should().Be(head);
    }

    [AvaloniaTest]
    public async Task Merge_squash_and_no_commit_and_strategy_become_arguments()
    {
        using TestRepository repository = new();
        repository.AddCleanDivergedBranches();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        MergeDialogViewModel dialog = viewModel.MergeDialog;

        dialog.NoCommit = true;
        dialog.Strategy = "ours";
        dialog.BuildArguments("topic").Should().Equal("merge", "--no-commit", "--strategy=ours", "--no-edit", "topic");
        dialog.Squash = true;
        dialog.CanChooseFastForward.Should().BeFalse();
        dialog.BuildArguments("topic").Should().Equal("merge", "--squash", "--strategy=ours", "--no-edit", "topic");

        dialog.Strategy = MergeDialogViewModel.DefaultStrategy;
        await dialog.OpenAsync("topic");
        dialog.Squash = true;
        await dialog.MergeCommand.ExecuteAsync(null);
        dialog.IsOpen.Should().BeFalse();
        repository.Parents().Should().HaveCount(1);
        File.Exists(Path.Combine(repository.Path, "t.txt")).Should().BeTrue();
        repository.GitOutput("status", "--porcelain").Should().Contain("t.txt");
    }

    [AvaloniaTest]
    public async Task Merge_unknown_target_is_reported_inline()
    {
        using TestRepository repository = new();
        repository.AddCleanDivergedBranches();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        await viewModel.OpenMergeDialogAsync("nope");

        await viewModel.MergeDialog.MergeCommand.ExecuteAsync(null);

        viewModel.MergeDialog.IsOpen.Should().BeTrue();
        viewModel.MergeDialog.Error.Should().Contain("nope");
    }

    [AvaloniaTest]
    public async Task Merge_conflicts_close_the_dialog_and_hand_over_to_the_operation_bar()
    {
        using TestRepository repository = new();
        repository.AddConflictingBranches();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        await viewModel.OpenMergeDialogAsync("topic");

        await viewModel.MergeDialog.MergeCommand.ExecuteAsync(null);

        viewModel.MergeDialog.IsOpen.Should().BeFalse();
        viewModel.Operation.IsActive.Should().BeTrue();
        viewModel.Operation.Kind.Should().Be(OperationKind.Merge);
        viewModel.Status.Should().Contain("conflicts");
    }

    // ---- rebase ----

    [AvaloniaTest]
    public async Task Rebase_puts_the_branch_on_top_of_the_target()
    {
        using TestRepository repository = new();
        repository.AddCleanDivergedBranches();
        repository.RunGit("checkout", "--quiet", "topic");
        MainWindowViewModel viewModel = await OpenAsync(repository);

        await viewModel.OpenRebaseDialogAsync("main");
        viewModel.RebaseDialog.CurrentBranch.Should().Be("topic");
        await viewModel.RebaseDialog.RebaseCommand.ExecuteAsync(null);

        viewModel.RebaseDialog.IsOpen.Should().BeFalse();
        repository.GitOutput("rev-parse", "HEAD~1").Trim().Should().Be(repository.GitOutput("rev-parse", "main").Trim());
        viewModel.Status.Should().Be("Rebased onto main");
    }

    [AvaloniaTest]
    public async Task Rebase_options_become_arguments_and_interactive_is_unavailable()
    {
        using TestRepository repository = new();
        repository.AddCleanDivergedBranches();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        RebaseDialogViewModel dialog = viewModel.RebaseDialog;

        dialog.CanInteractive.Should().BeFalse();
        dialog.RebaseMerges = true;
        dialog.AutoStash = true;
        dialog.IgnoreDate = true;
        dialog.CommitterDateIsAuthorDate = true;
        dialog.BuildArguments("main").Should().Equal("rebase", "--rebase-merges", "--autostash", "--ignore-date", "--committer-date-is-author-date", "main");
        dialog.AutoSquash = true;
        dialog.SpecificRange = true;
        dialog.RangeFrom = "a";
        dialog.RangeTo = "b";
        dialog.BuildArguments("main").Should().Equal("rebase", "--interactive", "--autosquash", "--rebase-merges", "--autostash", "--ignore-date", "--committer-date-is-author-date", "--onto", "main", "a", "b");
    }

    [AvaloniaTest]
    public async Task Rebase_with_a_specific_range_moves_only_those_commits()
    {
        using TestRepository repository = new();
        repository.AddCleanDivergedBranches();
        repository.RunGit("checkout", "--quiet", "topic");
        repository.CommitFile("t2.txt", "t2", "Topic adds t2.txt");
        MainWindowViewModel viewModel = await OpenAsync(repository);

        // Only the last commit (t2.txt) goes onto main; "Topic adds t.txt" is left behind.
        await viewModel.OpenRebaseDialogAsync("main");
        viewModel.RebaseDialog.SpecificRange = true;
        viewModel.RebaseDialog.RangeFrom = "HEAD~1";
        await viewModel.RebaseDialog.RebaseCommand.ExecuteAsync(null);

        viewModel.RebaseDialog.IsOpen.Should().BeFalse();
        File.Exists(Path.Combine(repository.Path, "t2.txt")).Should().BeTrue();
        File.Exists(Path.Combine(repository.Path, "t.txt")).Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Rebase_conflicts_hand_over_to_the_operation_bar()
    {
        using TestRepository repository = new();
        repository.AddConflictingBranches();
        repository.RunGit("checkout", "--quiet", "topic");
        MainWindowViewModel viewModel = await OpenAsync(repository);
        await viewModel.OpenRebaseDialogAsync("main");

        await viewModel.RebaseDialog.RebaseCommand.ExecuteAsync(null);

        viewModel.RebaseDialog.IsOpen.Should().BeFalse();
        viewModel.Operation.Kind.Should().Be(OperationKind.Rebase);
    }

    // ---- checkout ----

    [AvaloniaTest]
    public async Task Checkout_a_local_branch()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        MainWindowViewModel viewModel = await OpenAsync(repository);

        await viewModel.OpenCheckoutDialogAsync("develop");
        viewModel.CheckoutDialog.LocalBranch.Should().Be("develop");
        viewModel.CheckoutDialog.RemoteBranches.Should().Equal("origin/feature/y", "origin/main");
        await viewModel.CheckoutDialog.CheckoutCommand.ExecuteAsync(null);

        viewModel.CheckoutDialog.IsOpen.Should().BeFalse();
        repository.GitOutput("branch", "--show-current").Trim().Should().Be("develop");
    }

    [AvaloniaTest]
    public async Task Checkout_a_remote_branch_creates_a_local_branch_with_the_given_name()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        MainWindowViewModel viewModel = await OpenAsync(repository);

        await viewModel.OpenCheckoutDialogAsync("origin/feature/y");
        CheckoutBranchDialogViewModel dialog = viewModel.CheckoutDialog;
        dialog.IsRemoteBranch.Should().BeTrue();
        dialog.NewBranchName.Should().Be("feature/y");
        dialog.NewBranchName = "my-y";
        await dialog.CheckoutCommand.ExecuteAsync(null);

        dialog.IsOpen.Should().BeFalse();
        repository.GitOutput("branch", "--show-current").Trim().Should().Be("my-y");
        repository.GitOutput("config", "branch.my-y.remote").Trim().Should().Be("origin");
    }

    [AvaloniaTest]
    public async Task Checkout_a_remote_branch_refuses_an_existing_local_name_but_can_reset_it()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        repository.RunGit("stash", "--include-untracked");
        repository.RunGit("config", "user.name", "T");
        repository.RunGit("config", "user.email", "t@example.com");
        repository.CommitFile("x.txt", "x", "Main moves on");
        MainWindowViewModel viewModel = await OpenAsync(repository);

        await viewModel.OpenCheckoutDialogAsync("origin/main");
        CheckoutBranchDialogViewModel dialog = viewModel.CheckoutDialog;
        dialog.NewBranchName = "develop";
        await dialog.CheckoutCommand.ExecuteAsync(null);
        dialog.IsOpen.Should().BeTrue();
        dialog.Error.Should().Contain("develop");

        dialog.IsResetLocal = true;
        dialog.NewBranchName = "develop";
        await dialog.CheckoutCommand.ExecuteAsync(null);
        dialog.Error.Should().BeNull();
        dialog.IsOpen.Should().BeFalse();
        repository.GitOutput("branch", "--show-current").Trim().Should().Be("main");
    }

    [AvaloniaTest]
    public async Task Checkout_a_remote_branch_detached()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        MainWindowViewModel viewModel = await OpenAsync(repository);

        await viewModel.OpenCheckoutDialogAsync("origin/main");
        viewModel.CheckoutDialog.IsDetached = true;
        await viewModel.CheckoutDialog.CheckoutCommand.ExecuteAsync(null);

        viewModel.CheckoutDialog.IsOpen.Should().BeFalse();
        repository.GitOutput("branch", "--show-current").Trim().Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Checkout_handles_local_changes_as_chosen()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        repository.RunGit("config", "user.name", "T");
        repository.RunGit("config", "user.email", "t@example.com");
        repository.RunGit("stash", "--include-untracked");
        repository.RunGit("checkout", "--quiet", "-b", "other");
        repository.CommitFile("a.txt", "other", "Other changes a.txt");
        repository.RunGit("checkout", "--quiet", "main");
        repository.RunGit("stash", "pop");
        MainWindowViewModel viewModel = await OpenAsync(repository);
        CheckoutBranchDialogViewModel dialog = viewModel.CheckoutDialog;

        // Don't change: git refuses and the error stays in the dialog.
        await viewModel.OpenCheckoutDialogAsync("other");
        await dialog.CheckoutCommand.ExecuteAsync(null);
        dialog.IsOpen.Should().BeTrue();
        dialog.HasError.Should().BeTrue();
        repository.GitOutput("branch", "--show-current").Trim().Should().Be("main");

        // Stash and apply again: the stash conflicts (a.txt differs), so the checkout still happened.
        dialog.IsStashChanges = true;
        dialog.ReapplyStash = false;
        await dialog.CheckoutCommand.ExecuteAsync(null);
        dialog.IsOpen.Should().BeFalse();
        repository.GitOutput("branch", "--show-current").Trim().Should().Be("other");
        repository.GitOutput("stash", "list").Should().Contain("Stashed by checkout of other");
        repository.GitOutput("status", "--porcelain").Should().NotContain("a.txt");

        // Reset throws the changes away.
        repository.RunGit("stash", "drop");
        File.WriteAllText(Path.Combine(repository.Path, "b.txt"), "dirty");
        await viewModel.OpenAsync(repository.Path);
        await viewModel.OpenCheckoutDialogAsync("main");
        dialog.IsResetChanges = true;
        await dialog.CheckoutCommand.ExecuteAsync(null);
        dialog.IsOpen.Should().BeFalse();
        repository.GitOutput("branch", "--show-current").Trim().Should().Be("main");
        File.ReadAllText(Path.Combine(repository.Path, "b.txt")).Should().Be("second");
    }

    [AvaloniaTest]
    public async Task Checkout_stash_is_applied_again_by_default()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        MainWindowViewModel viewModel = await OpenAsync(repository);

        await viewModel.OpenCheckoutDialogAsync("develop");
        viewModel.CheckoutDialog.IsStashChanges = true;
        await viewModel.CheckoutDialog.CheckoutCommand.ExecuteAsync(null);

        viewModel.CheckoutDialog.IsOpen.Should().BeFalse();
        repository.GitOutput("branch", "--show-current").Trim().Should().Be("develop");
        repository.GitOutput("stash", "list").Should().BeEmpty();
        File.ReadAllText(Path.Combine(repository.Path, "a.txt")).Should().Be("changed");
        File.Exists(Path.Combine(repository.Path, "c.txt")).Should().BeTrue();
    }

    // ---- windows ----

    [AvaloniaTest]
    public async Task Commands_open_the_dialog_windows_and_they_render_in_both_themes()
    {
        using TestRepository repository = new();
        repository.AddCleanDivergedBranches();
        repository.RunGit("branch", "feature/x");
        repository.RunGit("remote", "add", "origin", "https://example.invalid/repo.git");
        repository.RunGit("update-ref", "refs/remotes/origin/main", "HEAD");
        repository.RunGit("update-ref", "refs/remotes/origin/feature/y", "HEAD");
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoPicker(), new FakeHistory());
        MainWindow window = new() { DataContext = viewModel, Width = 1100, Height = 760 };
        window.Show();
        try
        {
            await viewModel.OpenAsync(repository.Path);
            ShellTestSupport.Settle();
            DialogWindow.Open.Should().BeEmpty();

            await viewModel.OpenMergeDialogAsync("topic");
            await WaitUntilAsync(() => viewModel.MergeDialog.AllBranches.Count > 0);
            DialogWindow merge = DialogWindow.Open.Should().ContainSingle().Which;
            merge.Title.Should().Be("Merge branches");
            merge.Find<ComboBox>("TargetCombo").ItemCount.Should().Be(4);
            merge.Find<Button>("MergeButton").IsDefault.Should().BeTrue();
            Capture(merge, "merge");
            viewModel.MergeDialog.CloseCommand.Execute(null);
            ShellTestSupport.Settle();
            DialogWindow.Open.Should().BeEmpty();

            await viewModel.OpenRebaseDialogAsync("origin/main");
            await WaitUntilAsync(() => viewModel.RebaseDialog.AllBranches.Count > 0);
            DialogWindow rebase = DialogWindow.Open.Should().ContainSingle().Which;
            rebase.Title.Should().Be("Rebase");
            rebase.Find<CheckBox>("InteractiveBox").IsEnabled.Should().BeFalse();
            Capture(rebase, "rebase");
            viewModel.RebaseDialog.CloseCommand.Execute(null);
            ShellTestSupport.Settle();

            await viewModel.ShowCheckoutBranchDialogCommand.ExecuteAsync(null);
            await WaitUntilAsync(() => viewModel.CheckoutDialog.LocalBranches.Count > 0);
            viewModel.CheckoutDialog.IsRemoteBranch = true;
            viewModel.CheckoutDialog.RemoteBranch = "origin/feature/y";
            ShellTestSupport.Settle();
            DialogWindow checkout = DialogWindow.Open.Should().ContainSingle().Which;
            checkout.Title.Should().Be("Checkout branch");
            checkout.Find<TextBox>("NewNameBox").Text.Should().Be("feature/y");
            Capture(checkout, "checkout-branch");
            viewModel.CheckoutDialog.CloseCommand.Execute(null);
            ShellTestSupport.Settle();
            DialogWindow.Open.Should().BeEmpty();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            window.Close();
        }
    }

    private static void Capture(DialogWindow dialog, string name)
    {
        foreach ((ThemeVariant variant, string suffix) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
        {
            Application.Current!.RequestedThemeVariant = variant;
            ShellTestSupport.Settle();
            string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(directory);
            dialog.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"{name}-{suffix}.png"));
        }
    }
}
