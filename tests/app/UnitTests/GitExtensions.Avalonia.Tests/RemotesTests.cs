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
public class RemotesTests
{
    private static RemotesViewModel Create(TestRepository repo, List<string>? status = null)
        => new(() => repo.Path, message => status?.Add(message));

    [AvaloniaTest]
    public void Parses_remotes_with_fetch_and_push_urls()
    {
        IReadOnlyList<RemoteItem> remotes = RemotesViewModel.ParseRemotes(
            "origin\nmy.fork\n",
            "remote.origin.url https://h/a.git\nremote.origin.pushurl ssh://h/a.git\nremote.my.fork.url /srv/my fork.git");

        remotes.Should().Equal(
            new RemoteItem("origin", "https://h/a.git", "ssh://h/a.git"),
            new RemoteItem("my.fork", "/srv/my fork.git", string.Empty));
    }

    [AvaloniaTest]
    public void Pairs_remote_branches_with_tracking_local_branches()
    {
        IReadOnlyList<RemoteBranchRow> rows = RemotesViewModel.ParseBranches(
            "origin",
            "refs/remotes/origin/HEAD\nrefs/remotes/origin/main\nrefs/remotes/origin/feature/y\n",
            "main\trefs/remotes/origin/main\ndev\trefs/remotes/origin/main\nlocal\t\nother\trefs/remotes/up/main");

        rows.Should().Equal(new RemoteBranchRow("feature/y", string.Empty), new RemoteBranchRow("main", "main, dev"));
    }

    [TestCase("", false)]
    [TestCase("origin", true)]
    [TestCase("my fork", false)]
    [TestCase("-x", false)]
    [TestCase("a..b", false)]
    [TestCase("team/fork", true)]
    [TestCase("a:b", false)]
    public void Validates_names(string name, bool valid)
        => (RemotesViewModel.ValidateName(name) is null).Should().Be(valid);

    [AvaloniaTest]
    public async Task Add_edit_rename_and_remove_a_remote()
    {
        using TestRepository repo = new();
        List<string> status = [];
        RemotesViewModel vm = Create(repo, status);
        await vm.OpenAsync();
        vm.Remotes.Should().BeEmpty();
        vm.IsNew.Should().BeTrue();

        vm.Name = "origin";
        vm.Url = "https://example.invalid/a.git";
        vm.PushUrl = "ssh://example.invalid/a.git";
        await vm.SaveCommand.ExecuteAsync(null);
        vm.Message.Should().BeNull();
        vm.Remotes.Should().Equal(new RemoteItem("origin", "https://example.invalid/a.git", "ssh://example.invalid/a.git"));
        vm.SelectedRemote!.Name.Should().Be("origin");
        vm.SaveText.Should().Be("Save");
        status.Should().Contain("Added remote 'origin'.");
        repo.GitOutput("config", "remote.origin.pushurl").Trim().Should().Be("ssh://example.invalid/a.git");

        // Rename, change the fetch URL and clear the push URL in one save.
        vm.Name = "upstream";
        vm.Url = "https://example.invalid/b.git";
        vm.PushUrl = string.Empty;
        await vm.SaveCommand.ExecuteAsync(null);
        vm.Message.Should().BeNull();
        vm.Remotes.Should().Equal(new RemoteItem("upstream", "https://example.invalid/b.git", string.Empty));
        repo.GitOutput("remote").Trim().Should().Be("upstream");

        vm.RequestRemoveCommand.Execute(null);
        vm.IsConfirmingRemove.Should().BeTrue();
        vm.CancelRemoveCommand.Execute(null);
        vm.Remotes.Should().HaveCount(1);

        vm.RequestRemoveCommand.Execute(null);
        await vm.ConfirmRemoveCommand.ExecuteAsync(null);
        vm.Remotes.Should().BeEmpty();
        vm.SelectedRemote.Should().BeNull();
        repo.GitOutput("remote").Trim().Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Rejects_bad_input_and_duplicates_without_running_git()
    {
        using TestRepository repo = new();
        repo.AddBranchesRemoteAndTags();
        RemotesViewModel vm = Create(repo);
        await vm.OpenAsync();

        vm.Name = "bad name";
        vm.Url = "x";
        await vm.SaveCommand.ExecuteAsync(null);
        vm.Message.Should().Contain("not a valid remote name");

        vm.Name = "origin";
        vm.Url = string.Empty;
        await vm.SaveCommand.ExecuteAsync(null);
        vm.Message.Should().Contain("URL");

        vm.Name = "origin";
        vm.Url = "https://example.invalid/c.git";
        await vm.SaveCommand.ExecuteAsync(null);
        vm.Message.Should().Contain("already exists");
        vm.Remotes.Should().HaveCount(1);
    }

    [AvaloniaTest]
    public async Task Lists_branches_with_tracking_and_prunes_deleted_ones()
    {
        using TestRepository repo = new();
        string bare = Path.Combine(Path.GetTempPath(), "ge-remote-" + Guid.NewGuid().ToString("N") + ".git");
        try
        {
            repo.RunGit("init", "--bare", "--quiet", bare);
            repo.RunGit("remote", "add", "origin", bare);
            repo.RunGit("push", "--quiet", "origin", "main:main", "main:feature");
            repo.RunGit("fetch", "--quiet", "origin");
            repo.RunGit("branch", "--set-upstream-to=origin/main", "main");

            RemotesViewModel vm = Create(repo);
            await vm.OpenAsync();
            vm.SelectedRemote = vm.Remotes.Single();
            await WaitUntilAsync(() => vm.Branches.Count == 2);
            vm.Branches.Should().Equal(new RemoteBranchRow("feature", string.Empty), new RemoteBranchRow("main", "main"));

            repo.RunGit("--git-dir", bare, "branch", "-D", "feature");
            await vm.PruneCommand.ExecuteAsync(null);
            await WaitUntilAsync(() => vm.Branches.Count == 1);
            vm.Branches.Single().Branch.Should().Be("main");
            vm.Message.Should().Contain("feature");

            await vm.UpdateAllCommand.ExecuteAsync(null);
            vm.Message.Should().Be("Fetched all remotes.");
        }
        finally
        {
            try
            {
                Directory.Delete(bare, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [AvaloniaTest]
    public async Task Menu_opens_the_dialog_and_it_renders_in_both_themes()
    {
        using TestRepository repo = new();
        repo.AddBranchesRemoteAndTags();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoPicker(), new FakeHistory());
        MainWindow window = new() { DataContext = viewModel, Width = 1100, Height = 760 };
        window.Show();
        try
        {
            await viewModel.OpenAsync(repo.Path);
            ShellTestSupport.Settle();
            DialogWindow.Open.Should().BeEmpty();

            await viewModel.ShowRemotesCommand.ExecuteAsync(null);
            ShellTestSupport.Settle();
            DialogWindow dialog = DialogWindow.Open.Should().ContainSingle().Which;
            dialog.Title.Should().Be("Remote repositories");
            viewModel.Remotes.SelectedRemote = viewModel.Remotes.Remotes.Single();
            await WaitUntilAsync(() => viewModel.Remotes.Branches.Count == 2);
            ShellTestSupport.Settle();
            dialog.Find<ListBox>("BranchList").ItemCount.Should().Be(2);
            dialog.Find<TextBox>("UrlBox").Text.Should().Contain("example.invalid");

            foreach ((ThemeVariant variant, string suffix) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
            {
                Application.Current!.RequestedThemeVariant = variant;
                ShellTestSupport.Settle();
                string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
                Directory.CreateDirectory(directory);
                dialog.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"remotes-{suffix}.png"));
            }

            viewModel.Remotes.CloseCommand.Execute(null);
            ShellTestSupport.Settle();
            DialogWindow.Open.Should().BeEmpty();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            window.Close();
        }
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
}
