using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;
using GitExtensions.Extensibility;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class RemoteOperationsTests
{
    private sealed class RecordingMessages : IMessageDialogs
    {
        public List<string> Errors { get; } = [];

        public void ShowError(object? owner, string text, string? caption = null) => Errors.Add($"{caption}: {text}");

        public void ShowWarning(object? owner, string text, string caption) => Errors.Add($"{caption}: {text}");
    }

    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static async Task<(RemoteOperationsViewModel ViewModel, List<string> Statuses, RecordingMessages Messages, int[] Refreshes)> CreateAsync(string path)
    {
        GitEnvironment git = GitEnvironment.Initialize();
        GitCommands.GitModule module = git.OpenRepository(path)!;
        List<string> statuses = [];
        RecordingMessages messages = new();
        int[] refreshes = [0];
        RemoteOperationsViewModel viewModel = new(() => module, statuses.Add, messages, () =>
        {
            refreshes[0]++;
            return Task.CompletedTask;
        });
        await viewModel.ReloadStateAsync();
        return (viewModel, statuses, messages, refreshes);
    }

    [Test]
    public void Accumulator_splits_lines_and_collapses_progress()
    {
        OutputAccumulator output = new();
        output.Append("first\nRecei");
        output.Append("ving 10%\rReceiving 50%\rReceiving 100%, done.\nlast");

        output.Lines.Should().Equal("first", "Receiving 100%, done.", "last");

        output.Append("\r\n");
        output.Lines.Should().Equal("first", "Receiving 100%, done.", "last");
        output.Append("\rnext");
        output.Lines.Should().Equal("first", "Receiving 100%, done.", "last", "next");
    }

    [Test]
    public void Accumulator_caps_the_number_of_lines_and_can_be_cleared()
    {
        OutputAccumulator output = new(maxLines: 3);
        output.Append("1\n2\n3\n4\n5\n");
        output.Lines.Should().Equal("3", "4", "5");

        output.AppendLine("x");
        output.Lines.Should().Equal("4", "5", "x");

        output.Clear();
        output.Text.Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Fetch_brings_new_remote_refs()
    {
        using RemoteScenario scenario = new();
        RemoteScenario.Commit(scenario.Beta, "b.txt", "two", "Add b.txt");
        RemoteScenario.Run(scenario.Beta, "push", "--quiet", "origin", "main");
        string expected = RemoteScenario.Run(scenario.Beta, "rev-parse", "HEAD");
        (RemoteOperationsViewModel viewModel, List<string> statuses, _, int[] refreshes) = await CreateAsync(scenario.Alpha);

        await viewModel.FetchCommand.ExecuteAsync(null);

        RemoteScenario.Run(scenario.Alpha, "rev-parse", "origin/main").Should().Be(expected);
        statuses.Last().Should().Be("Fetch completed.");
        viewModel.OutputText.Should().Contain("> git fetch --progress origin").And.Contain("Fetch completed.");
        viewModel.IsOutputVisible.Should().BeTrue();
        viewModel.IsRunning.Should().BeFalse();
        refreshes[0].Should().Be(1);
    }

    [AvaloniaTest]
    public async Task Pull_fast_forwards_the_current_branch()
    {
        using RemoteScenario scenario = new();
        RemoteScenario.Commit(scenario.Beta, "b.txt", "two", "Add b.txt");
        RemoteScenario.Run(scenario.Beta, "push", "--quiet", "origin", "main");
        string expected = RemoteScenario.Run(scenario.Beta, "rev-parse", "HEAD");
        (RemoteOperationsViewModel viewModel, _, _, _) = await CreateAsync(scenario.Alpha);
        viewModel.HasUpstream.Should().BeTrue();

        await viewModel.PullCommand.ExecuteAsync(null);

        RemoteScenario.Run(scenario.Alpha, "rev-parse", "HEAD").Should().Be(expected);
        File.Exists(Path.Combine(scenario.Alpha, "b.txt")).Should().BeTrue();
    }

    [AvaloniaTest]
    public async Task Push_updates_the_remote()
    {
        using RemoteScenario scenario = new();
        RemoteScenario.Commit(scenario.Alpha, "c.txt", "three", "Add c.txt");
        string expected = RemoteScenario.Run(scenario.Alpha, "rev-parse", "HEAD");
        (RemoteOperationsViewModel viewModel, List<string> statuses, _, _) = await CreateAsync(scenario.Alpha);

        await viewModel.PushCommand.ExecuteAsync(null);

        RemoteScenario.Run(scenario.Origin, "rev-parse", "main").Should().Be(expected);
        statuses.Last().Should().Be("Push completed.");
    }

    [AvaloniaTest]
    public async Task Push_of_a_new_branch_sets_the_upstream()
    {
        using RemoteScenario scenario = new();
        RemoteScenario.Run(scenario.Alpha, "checkout", "-q", "-b", "feature");
        RemoteScenario.Commit(scenario.Alpha, "f.txt", "feature", "Add f.txt");
        (RemoteOperationsViewModel viewModel, _, _, _) = await CreateAsync(scenario.Alpha);
        viewModel.HasUpstream.Should().BeFalse();
        viewModel.SelectedRemote.Should().Be("origin");

        await viewModel.PushCommand.ExecuteAsync(null);

        viewModel.OutputText.Should().Contain("--set-upstream origin feature");
        RemoteScenario.Run(scenario.Origin, "rev-parse", "feature").Should().Be(RemoteScenario.Run(scenario.Alpha, "rev-parse", "HEAD"));
        RemoteScenario.Run(scenario.Alpha, "config", "branch.feature.remote").Should().Be("origin");
        viewModel.HasUpstream.Should().BeTrue();
    }

    [AvaloniaTest]
    public async Task A_failing_fetch_reports_an_error_and_re_enables_the_buttons()
    {
        using RemoteScenario scenario = new();
        RemoteScenario.Run(scenario.Alpha, "remote", "set-url", "origin", Path.Combine(scenario.Origin, "does-not-exist"));
        (RemoteOperationsViewModel viewModel, List<string> statuses, RecordingMessages messages, int[] refreshes) = await CreateAsync(scenario.Alpha);

        await viewModel.FetchCommand.ExecuteAsync(null);

        statuses.Last().Should().StartWith("Fetch failed:");
        messages.Errors.Should().ContainSingle().Which.Should().StartWith("Fetch failed:");
        viewModel.OutputText.Should().Contain("Fetch failed");
        viewModel.IsRunning.Should().BeFalse();
        viewModel.FetchCommand.CanExecute(null).Should().BeTrue();
        viewModel.PullCommand.CanExecute(null).Should().BeTrue();
        viewModel.PushCommand.CanExecute(null).Should().BeTrue();
        refreshes[0].Should().Be(0);
    }

    [AvaloniaTest]
    public async Task Buttons_are_disabled_while_running_and_cancel_stops_the_command()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("The slow hook is a POSIX shell script.");
        }

        using RemoteScenario scenario = new();

        // A pre-push hook that never finishes stands in for a slow network transfer.
        string hook = Path.Combine(scenario.Alpha, ".git", "hooks", "pre-push");
        File.WriteAllText(hook, "#!/bin/sh\nsleep 60\n");
        using (Process chmod = Process.Start("chmod", ["+x", hook]))
        {
            await chmod.WaitForExitAsync();
        }

        RemoteScenario.Commit(scenario.Alpha, "c.txt", "three", "Add c.txt");
        (RemoteOperationsViewModel viewModel, List<string> statuses, RecordingMessages messages, _) = await CreateAsync(scenario.Alpha);

        Stopwatch stopwatch = Stopwatch.StartNew();
        Task push = viewModel.PushCommand.ExecuteAsync(null);
        viewModel.IsRunning.Should().BeTrue();
        viewModel.FetchCommand.CanExecute(null).Should().BeFalse();
        viewModel.PushCommand.CanExecute(null).Should().BeFalse();
        viewModel.OutputText.Should().Contain("> git push --progress");

        // Give git time to start the hook so the whole process tree has to be killed.
        await Task.Delay(500);
        viewModel.IsRunning.Should().BeTrue();
        viewModel.CancelCommand.Execute(null);
        await push;

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
        statuses.Last().Should().Be("Push cancelled.");
        messages.Errors.Should().BeEmpty();
        viewModel.IsRunning.Should().BeFalse();
        viewModel.PushCommand.CanExecute(null).Should().BeTrue();
        RemoteScenario.Run(scenario.Origin, "rev-parse", "main").Should().NotBe(RemoteScenario.Run(scenario.Alpha, "rev-parse", "HEAD"));
    }

    [AvaloniaTest]
    public async Task Main_window_shows_the_toolbar_and_output_panel()
    {
        using RemoteScenario scenario = new();
        RemoteScenario.Commit(scenario.Beta, "b.txt", "two", "Add b.txt");
        RemoteScenario.Run(scenario.Beta, "push", "--quiet", "origin", "main");
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.OpenAsync(scenario.Alpha);
        await viewModel.Remote.ReloadStateAsync();

        window.FindControl<MainToolbarView>("MainToolbar")!.IsVisible.Should().BeTrue();
        window.FindControl<MainToolbarView>("MainToolbar")!.FindControl<Button>("PushButton").Should().NotBeNull();
        window.FindControl<OutputPanelView>("OutputPanel")!.IsVisible.Should().BeFalse();

        await viewModel.Remote.FetchCommand.ExecuteAsync(null);
        await viewModel.Remote.PullCommand.ExecuteAsync(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        window.FindControl<OutputPanelView>("OutputPanel")!.IsVisible.Should().BeTrue();
        viewModel.Commits.Should().HaveCount(2);
        viewModel.Status.Should().Be("Pull completed.");

        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "main-window-output.png");
        window.CaptureRenderedFrame()!.Save(file);
        TestContext.AddTestAttachment(file);
        window.Close();
    }
}
