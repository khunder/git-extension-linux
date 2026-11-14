using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class CommitViewModelTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static void ConfigureIdentity(TestRepository repository)
    {
        repository.RunGit("config", "user.name", "Committer");
        repository.RunGit("config", "user.email", "committer@example.com");
    }

    private static async Task<MainWindowViewModel> OpenAsync(TestRepository repository)
    {
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await viewModel.OpenAsync(repository.Path);
        await viewModel.Commit!.LoadAsync();
        return viewModel;
    }

    [AvaloniaTest]
    public async Task Commit_is_blocked_without_staged_changes_or_message()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        CommitViewModel commit = viewModel.Commit!;

        commit.StagedCount.Should().Be(0);
        commit.UnstagedCount.Should().Be(2);
        commit.Message = "Something";
        commit.CommitCommand.CanExecute(null).Should().BeFalse("nothing is staged");

        repository.RunGit("add", "a.txt");
        await viewModel.RefreshChangesAsync();
        commit.StagedCount.Should().Be(1);
        commit.CommitCommand.CanExecute(null).Should().BeTrue();

        commit.Message = "   ";
        commit.CommitCommand.CanExecute(null).Should().BeFalse("the message is blank");
    }

    [AvaloniaTest]
    public async Task Commit_creates_a_commit_and_refreshes()
    {
        using TestRepository repository = new();
        ConfigureIdentity(repository);
        repository.RunGit("add", "a.txt");
        MainWindowViewModel viewModel = await OpenAsync(repository);

        viewModel.Commit!.Message = "Change a.txt\n\nWith a body.";
        await viewModel.Commit.CommitCommand.ExecuteAsync(null);

        repository.GitOutput("log", "-1", "--format=%B").Trim().Should().Be("Change a.txt\n\nWith a body.");
        repository.GitOutput("rev-list", "--count", "HEAD").Trim().Should().Be("3");
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.Commits.Should().HaveCount(3);
        viewModel.Commits[0].Subject.Should().Be("Change a.txt");
        viewModel.SelectedCommit.Should().BeSameAs(viewModel.Commits[0]);
        viewModel.ChangedFiles.Should().ContainSingle(f => f.Name == "c.txt");
        viewModel.Commit!.Message.Should().BeEmpty();
        viewModel.Commit.StagedCount.Should().Be(0);
    }

    [AvaloniaTest]
    public async Task Amend_prefills_and_replaces_the_last_commit()
    {
        using TestRepository repository = new();
        ConfigureIdentity(repository);
        MainWindowViewModel viewModel = await OpenAsync(repository);
        CommitViewModel commit = viewModel.Commit!;

        commit.Amend = true;
        await WaitForAsync(() => commit.Message == "Add b.txt");
        commit.CommitCommand.CanExecute(null).Should().BeTrue("amending only rewords");

        commit.Message = "Reworded b";
        await commit.CommitCommand.ExecuteAsync(null);

        repository.GitOutput("rev-list", "--count", "HEAD").Trim().Should().Be("2");
        repository.GitOutput("log", "-1", "--format=%s").Trim().Should().Be("Reworded b");
        viewModel.Commits.Select(c => c.Subject).Should().Equal("Reworded b", "Add a.txt");
        viewModel.Commit!.Amend.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Initial_commit_works_in_a_repository_without_head()
    {
        using TestRepository source = new();
        string path = Path.Combine(Path.GetTempPath(), "ge-avalonia-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            RunGit(path, "init", "--quiet", "--initial-branch=main");
            RunGit(path, "config", "user.name", "Committer");
            RunGit(path, "config", "user.email", "committer@example.com");
            File.WriteAllText(Path.Combine(path, "x.txt"), "x");
            RunGit(path, "add", "x.txt");

            MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
            await viewModel.OpenAsync(path);
            await viewModel.Commit!.LoadAsync();
            viewModel.Commit.HasHead.Should().BeFalse();

            viewModel.Commit.Message = "Initial";
            await viewModel.Commit.CommitCommand.ExecuteAsync(null);

            viewModel.ErrorMessage.Should().BeNull();
            viewModel.Commits.Should().ContainSingle(c => c.Subject == "Initial");
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [AvaloniaTest]
    public async Task Missing_identity_is_reported_not_hung()
    {
        using TestRepository repository = new();
        repository.RunGit("config", "user.useConfigOnly", "true");
        repository.RunGit("add", "a.txt");
        string? oldGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        string? oldSystem = Environment.GetEnvironmentVariable("GIT_CONFIG_NOSYSTEM");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", "/dev/null");
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        try
        {
            MainWindowViewModel viewModel = await OpenAsync(repository);
            viewModel.Commit!.Message = "Will fail";

            await viewModel.Commit.CommitCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(30));

            viewModel.ErrorMessage.Should().NotBeNullOrEmpty();
            viewModel.ErrorMessage.Should().Contain("Commit failed").And.Contain("identity");
            viewModel.Commit.Message.Should().Be("Will fail", "the message is kept for a retry");
            repository.GitOutput("rev-list", "--count", "HEAD").Trim().Should().Be("2");
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", oldGlobal);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", oldSystem);
        }
    }

    [AvaloniaTest]
    public async Task Merge_message_prefills_the_commit_message()
    {
        using TestRepository repository = new();
        File.WriteAllText(Path.Combine(repository.Path, ".git", "MERGE_MSG"), "Merge branch 'x'\n# Conflicts:\n#\ta.txt\n");
        MainWindowViewModel viewModel = await OpenAsync(repository);

        viewModel.Commit!.Message.Should().Be("Merge branch 'x'");
    }

    [AvaloniaTest]
    public async Task MainWindow_renders_the_commit_area()
    {
        using TestRepository repository = new();
        repository.RunGit("add", "a.txt");
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.OpenAsync(repository.Path);
        viewModel.Commit!.Message = "Update a.txt\n\nExplain the change here.";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        CommitView view = window.GetCommitView();
        view.FindControl<TextBlock>("SummaryText")!.Text.Should().Be("1 staged · 1 unstaged");
        view.FindControl<Button>("CommitButton")!.IsEffectivelyEnabled.Should().BeTrue();

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "main-window-commit.png");
        frame!.Save(file);
        TestContext.AddTestAttachment(file);
        window.Close();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        condition().Should().BeTrue();
    }

    private static void RunGit(string directory, params string[] arguments)
    {
        System.Diagnostics.ProcessStartInfo startInfo = new("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
        process.WaitForExit();
    }
}

internal static class CommitRenderExtensions
{
    public static CommitView GetCommitView(this MainWindow window)
        => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<CommitView>().Single();
}
