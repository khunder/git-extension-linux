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
public class PullPushTests
{
    private static async Task<MainWindowViewModel> OpenAsync(string path)
    {
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoPicker());
        await viewModel.OpenAsync(path);
        await viewModel.Remote.ReloadStateAsync();
        return viewModel;
    }

    private static void PushFromBeta(RemoteScenario scenario, string file, string content, string message)
    {
        RemoteScenario.Commit(scenario.Beta, file, content, message);
        RemoteScenario.Run(scenario.Beta, "push", "--quiet", "origin", "main");
    }

    [AvaloniaTest]
    public async Task Pull_dialog_defaults_to_the_branch_remote_and_builds_the_command()
    {
        using RemoteScenario scenario = new();
        MainWindowViewModel viewModel = await OpenAsync(scenario.Alpha);

        await viewModel.ShowPullDialogCommand.ExecuteAsync(null);
        PullDialogViewModel pull = viewModel.Pull;

        pull.IsOpen.Should().BeTrue();
        pull.SelectedRemote.Should().Be("origin");
        pull.LocalBranch.Should().Be("main");
        pull.RemoteBranch.Should().Be("main");
        pull.IsMerge.Should().BeTrue();
        pull.BuildArguments().Should().Equal("pull", "--progress", "--no-edit", "--no-rebase", "origin", "main");

        pull.IsRebase = true;
        pull.AutoStash = true;
        pull.TagMode = PullTagMode.NoTags;
        pull.BuildArguments().Should().Equal("pull", "--progress", "--no-edit", "--rebase", "--autostash", "--no-tags", "origin", "main");

        pull.IsFetch = true;
        pull.PruneTags = true;
        pull.FetchAll = true;
        pull.BuildArguments().Should().Equal("fetch", "--progress", "--prune", "--prune-tags", "--no-tags", "--all");

        pull.UseUrl = true;
        pull.FetchAll = false;
        pull.Url = " ";
        pull.BuildArguments().Should().BeNull();
        pull.HasError.Should().BeTrue();
        pull.Url = scenario.Origin;
        pull.BuildArguments().Should().Contain(scenario.Origin);
        pull.HasError.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Pull_dialog_merges_and_shows_the_output()
    {
        using RemoteScenario scenario = new();
        PushFromBeta(scenario, "b.txt", "two", "Add b.txt");
        string expected = RemoteScenario.Run(scenario.Beta, "rev-parse", "HEAD");
        MainWindowViewModel viewModel = await OpenAsync(scenario.Alpha);

        await viewModel.ShowPullDialogCommand.ExecuteAsync(null);
        await viewModel.Pull.PullCommand.ExecuteAsync(null);

        RemoteScenario.Run(scenario.Alpha, "rev-parse", "HEAD").Should().Be(expected);
        viewModel.Pull.OutputText.Should().Contain("> git pull --progress --no-edit --no-rebase origin main").And.Contain("Pull (merge) completed.");
        viewModel.Pull.IsOpen.Should().BeTrue("the dialog stays open to show the result");
        viewModel.Remote.LastRunSucceeded.Should().BeTrue();
    }

    [AvaloniaTest]
    public async Task Pull_dialog_fetch_only_does_not_change_the_branch()
    {
        using RemoteScenario scenario = new();
        PushFromBeta(scenario, "b.txt", "two", "Add b.txt");
        string head = RemoteScenario.Run(scenario.Alpha, "rev-parse", "HEAD");
        string expected = RemoteScenario.Run(scenario.Beta, "rev-parse", "HEAD");
        MainWindowViewModel viewModel = await OpenAsync(scenario.Alpha);

        await viewModel.ShowPullDialogCommand.ExecuteAsync(null);
        viewModel.Pull.IsFetch = true;
        viewModel.Pull.RemoteBranch = string.Empty;
        await viewModel.Pull.PullCommand.ExecuteAsync(null);

        RemoteScenario.Run(scenario.Alpha, "rev-parse", "HEAD").Should().Be(head);
        RemoteScenario.Run(scenario.Alpha, "rev-parse", "origin/main").Should().Be(expected);
    }

    [AvaloniaTest]
    public async Task Pull_dialog_with_auto_stash_keeps_local_changes()
    {
        using RemoteScenario scenario = new();
        PushFromBeta(scenario, "b.txt", "two", "Add b.txt");
        File.WriteAllText(Path.Combine(scenario.Alpha, "a.txt"), "locally changed");
        MainWindowViewModel viewModel = await OpenAsync(scenario.Alpha);

        await viewModel.ShowPullDialogCommand.ExecuteAsync(null);
        viewModel.Pull.AutoStash = true;
        await viewModel.Pull.PullCommand.ExecuteAsync(null);

        File.Exists(Path.Combine(scenario.Alpha, "b.txt")).Should().BeTrue();
        File.ReadAllText(Path.Combine(scenario.Alpha, "a.txt")).Should().Be("locally changed");
    }

    [AvaloniaTest]
    public async Task Pull_dialog_hands_conflicts_over_to_the_operation_bar()
    {
        using RemoteScenario scenario = new();
        RemoteScenario.Commit(scenario.Alpha, "a.txt", "alpha edit", "Alpha edits a.txt");
        PushFromBeta(scenario, "a.txt", "beta edit", "Beta edits a.txt");
        MainWindowViewModel viewModel = await OpenAsync(scenario.Alpha);

        await viewModel.ShowPullDialogCommand.ExecuteAsync(null);
        viewModel.Pull.IsOpen.Should().BeTrue();
        await viewModel.Pull.PullCommand.ExecuteAsync(null);
        ShellTestSupport.Settle();

        viewModel.Remote.LastRunSucceeded.Should().BeFalse();
        viewModel.Operation.IsActive.Should().BeTrue();
        viewModel.Pull.IsOpen.Should().BeFalse("the merge is continued from the operation bar");
    }

    [AvaloniaTest]
    public async Task Push_dialog_pushes_the_branch()
    {
        using RemoteScenario scenario = new();
        RemoteScenario.Commit(scenario.Alpha, "c.txt", "three", "Add c.txt");
        string expected = RemoteScenario.Run(scenario.Alpha, "rev-parse", "HEAD");
        MainWindowViewModel viewModel = await OpenAsync(scenario.Alpha);

        await viewModel.ShowPushDialogCommand.ExecuteAsync(null);
        PushDialogViewModel push = viewModel.Push;
        push.IsOpen.Should().BeTrue();
        push.SelectedRemote.Should().Be("origin");
        push.LocalBranch.Should().Be("main");
        push.RemoteBranch.Should().Be("main");
        push.SetUpstream.Should().BeFalse("main already tracks origin/main");
        push.BuildArguments().Should().Equal("push", "--progress", "origin", "main:main");

        await push.PushCommand.ExecuteAsync(null);

        RemoteScenario.Run(scenario.Origin, "rev-parse", "main").Should().Be(expected);
        push.OutputText.Should().Contain("Push completed.");
    }

    [AvaloniaTest]
    public async Task Push_dialog_creates_a_remote_branch_with_upstream_and_supports_force_options()
    {
        using RemoteScenario scenario = new();
        RemoteScenario.Run(scenario.Alpha, "checkout", "-q", "-b", "feature");
        RemoteScenario.Commit(scenario.Alpha, "f.txt", "f", "Add f.txt");
        MainWindowViewModel viewModel = await OpenAsync(scenario.Alpha);

        await viewModel.ShowPushDialogCommand.ExecuteAsync(null);
        PushDialogViewModel push = viewModel.Push;
        push.LocalBranch.Should().Be("feature");
        push.SetUpstream.Should().BeTrue("the new branch has no upstream yet");
        push.RemoteBranch = "feature-remote";
        push.BuildArguments().Should().Equal("push", "--progress", "--set-upstream", "origin", "feature:feature-remote");

        push.ForceWithLease = true;
        push.BuildArguments().Should().Contain("--force-with-lease");
        push.ForcePush = true;
        push.ForceWithLease.Should().BeFalse("the two force options exclude each other");
        push.ShowForceWarning.Should().BeTrue();
        push.BuildArguments().Should().Contain("--force").And.NotContain("--force-with-lease");
        push.ForcePush = false;

        push.PushAllTags = true;
        push.BuildArguments().Should().Equal("push", "--progress", "--tags", "origin");
        push.PushAllTags = false;
        push.PushAllBranches = true;
        push.BuildArguments().Should().Equal("push", "--progress", "--all", "origin");
        push.PushAllBranches = false;

        await push.PushCommand.ExecuteAsync(null);

        RemoteScenario.Run(scenario.Origin, "rev-parse", "feature-remote").Should().Be(RemoteScenario.Run(scenario.Alpha, "rev-parse", "feature"));
        RemoteScenario.Run(scenario.Alpha, "rev-parse", "--abbrev-ref", "feature@{upstream}").Should().Be("origin/feature-remote");
    }

    [AvaloniaTest]
    public async Task Menu_and_toolbar_open_the_pull_and_push_dialogs_and_they_render()
    {
        using RemoteScenario scenario = new();
        PushFromBeta(scenario, "b.txt", "two", "Add b.txt");
        RemoteScenario.Commit(scenario.Alpha, "c.txt", "three", "Add c.txt");
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoPicker());
        MainWindow window = new() { DataContext = viewModel, Width = 1100, Height = 800 };
        window.Show();
        try
        {
            await viewModel.OpenAsync(scenario.Alpha);
            await viewModel.Remote.ReloadStateAsync();
            ShellTestSupport.Settle();
            DialogWindow.Open.Should().BeEmpty();

            await viewModel.ShowPullDialogCommand.ExecuteAsync(null);
            ShellTestSupport.Settle();
            DialogWindow pull = DialogWindow.Open.Should().ContainSingle().Which;
            pull.Title.Should().Be("Pull");
            pull.Find<ComboBox>("RemoteCombo").SelectedItem.Should().Be("origin");
            pull.Find<Button>("PullButton").Content.Should().Be("Pull");
            await viewModel.Pull.PullCommand.ExecuteAsync(null);
            ShellTestSupport.Settle();
            pull.Find<TextBox>("OutputBox").Text.Should().Contain("Pull (merge) completed.");
            pull.Find<ComboBox>("RemoteCombo").SelectedItem.Should().Be("origin", "the selection survives the operation");
            Capture(pull, "pull");
            viewModel.Pull.CloseCommand.Execute(null);
            ShellTestSupport.Settle();
            DialogWindow.Open.Should().BeEmpty();

            await viewModel.ShowPushDialogCommand.ExecuteAsync(null);
            ShellTestSupport.Settle();
            DialogWindow push = DialogWindow.Open.Should().ContainSingle().Which;
            push.Title.Should().Be("Push");
            push.Find<Button>("PushButton").Content.Should().Be("Push");
            viewModel.Push.ForcePush = true;
            await viewModel.Push.PushCommand.ExecuteAsync(null);
            ShellTestSupport.Settle();
            Capture(push, "push");
            push.Close();
            ShellTestSupport.Settle();
            viewModel.Push.IsOpen.Should().BeFalse();
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
            dialog.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"{name}-dialog-{suffix}.png"));
        }
    }
}
