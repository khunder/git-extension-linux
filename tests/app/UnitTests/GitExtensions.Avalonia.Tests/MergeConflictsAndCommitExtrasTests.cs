using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class MergeConflictsAndCommitExtrasTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static string NewStorePath() => Path.Combine(Path.GetTempPath(), "ge-avalonia-commit-" + Guid.NewGuid().ToString("N") + ".json");

    private static async Task WaitForAsync(Func<bool> condition, string because = "")
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(25);
            Dispatcher.UIThread.RunJobs();
        }

        condition().Should().BeTrue(because);
    }

    private static async Task<MainWindowViewModel> OpenConflictAsync(TestRepository repository)
    {
        repository.AddConflictingBranches();
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(repository.Path);
        await main.Operation.MergeAsync("topic");
        return main;
    }

    private static void Identity(TestRepository repository)
    {
        repository.RunGit("config", "user.name", "Committer");
        repository.RunGit("config", "user.email", "committer@example.com");
    }

    [AvaloniaTest]
    public async Task Merge_conflicts_view_model_lists_the_files_and_resolves_them()
    {
        using TestRepository repository = new();
        MainWindowViewModel main = await OpenConflictAsync(repository);
        using MergeConflictsViewModel viewModel = main.CreateMergeConflictsViewModel()!;

        await viewModel.LoadAsync();
        viewModel.Files.Select(f => f.Name).Should().Equal("conflict.txt");
        viewModel.Summary.Should().Be("1 file has a merge conflict.");
        viewModel.SelectedFile!.Name.Should().Be("conflict.txt");
        await WaitForAsync(() => viewModel.Resolver.IsActive && viewModel.Resolver.Lines.Count > 0);
        viewModel.Resolver.HasMarkers.Should().BeTrue();

        (await viewModel.Resolver.UseTheirsAsync()).Should().BeTrue();
        await WaitForAsync(() => viewModel.Files.Count == 0);
        viewModel.HasNoConflicts.Should().BeTrue();
        repository.WorkFile("conflict.txt").Should().Contain("topic version").And.NotContain("<<<<<<<");
        repository.GitOutput("ls-files", "-u").Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Solve_merge_conflicts_command_opens_one_window_that_renders()
    {
        using TestRepository repository = new();
        MainWindowViewModel main = await OpenConflictAsync(repository);
        Window owner = new() { Width = 800, Height = 600 };
        owner.Show();
        WindowService service = new(main, owner);
        main.WindowService = service;

        main.ShowMergeConflictsCommand.Execute(null);
        MergeConflictsWindow window = service.MergeConflictsWindow!;
        window.Should().NotBeNull();
        window.Width = 900;
        window.Height = 600;
        MergeConflictsViewModel viewModel = (MergeConflictsViewModel)window.DataContext!;
        await WaitForAsync(() => viewModel.Files.Count == 1 && viewModel.Resolver.Lines.Count > 0);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        window.FindControl<ListBox>("ConflictFilesList")!.ItemCount.Should().Be(1);
        window.GetVisualDescendants().OfType<Button>().Select(b => b.Name).Should().Contain(["UseOursButton", "UseTheirsButton", "MarkResolvedButton"]);
        window.Find<Control>("ConflictPanel")!.IsVisible.Should().BeTrue();
        main.ShowMergeConflictsCommand.Execute(null);
        service.MergeConflictsWindow.Should().BeSameAs(window);

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "merge-conflicts-window.png");
        frame!.Save(file);
        TestContext.AddTestAttachment(file);

        window.Find<Button>("UseOursButton")!.Command!.Execute(null);
        await WaitForAsync(() => viewModel.Files.Count == 0);
        window.Close();
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Commit_window_hosts_the_resolver_for_a_conflicted_file()
    {
        using TestRepository repository = new();
        MainWindowViewModel main = await OpenConflictAsync(repository);
        Window owner = new() { Width = 800, Height = 600 };
        owner.Show();
        WindowService service = new(main, owner);
        main.WindowService = service;
        CommitWindowStore.Current = new CommitWindowStore(NewStorePath());
        main.ShowCommitWindowCommand.Execute(null);
        CommitWindow window = service.CommitWindow!;
        CommitWindowViewModel viewModel = (CommitWindowViewModel)window.DataContext!;
        await WaitForAsync(() => viewModel.Branch is not null && !viewModel.IsBusy);

        viewModel.HasConflicts.Should().BeTrue();
        viewModel.Resolver.IsActive.Should().BeFalse();
        viewModel.SelectedUnstaged = viewModel.Unstaged.First(f => f.IsConflicted);
        await WaitForAsync(() => viewModel.Resolver.IsActive && viewModel.Resolver.Lines.Count > 0);
        Dispatcher.UIThread.RunJobs();
        window.Find<Control>("ConflictPanel")!.IsVisible.Should().BeTrue();
        window.Find<Control>("DiffHost")!.IsVisible.Should().BeFalse();

        window.Find<Button>("UseOursButton")!.Command!.Execute(null);
        await WaitForAsync(() => !viewModel.HasConflicts && !viewModel.Resolver.IsActive);
        repository.WorkFile("conflict.txt").Should().Contain("main version");
        window.Find<Control>("DiffHost")!.IsVisible.Should().BeTrue();
        window.Close();
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Amend_with_reset_author_overwrites_the_author()
    {
        using TestRepository repository = new();
        Identity(repository);
        File.WriteAllText(Path.Combine(repository.Path, "d.txt"), "d");
        repository.RunGit("add", "d.txt");
        repository.RunGit("commit", "--quiet", "--author=Someone Else <else@example.com>", "-m", "Authored by someone else");
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(repository.Path);
        CommitWindowViewModel window = main.CreateCommitWindowViewModel(new CommitWindowStore(NewStorePath()))!;
        await window.LoadAsync();

        window.Commit.Amend = true;
        await WaitForAsync(() => window.Commit.Message == "Authored by someone else");
        window.Commit.ResetAuthor = true;
        await window.CommitNowCommand.ExecuteAsync(null);

        repository.GitOutput("log", "-1", "--format=%an").Trim().Should().Be("Committer");
        window.Commit.ResetAuthor.Should().BeFalse("the options reset with the amend flag");
    }

    [AvaloniaTest]
    public async Task Amend_without_reset_author_keeps_the_author()
    {
        using TestRepository repository = new();
        Identity(repository);
        File.WriteAllText(Path.Combine(repository.Path, "d.txt"), "d");
        repository.RunGit("add", "d.txt");
        repository.RunGit("commit", "--quiet", "--author=Someone Else <else@example.com>", "-m", "Authored by someone else");
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(repository.Path);
        CommitWindowViewModel window = main.CreateCommitWindowViewModel(new CommitWindowStore(NewStorePath()))!;
        await window.LoadAsync();

        window.Commit.Amend = true;
        await WaitForAsync(() => window.Commit.Message == "Authored by someone else");
        window.Commit.Message = "Reworded";
        await window.CommitNowCommand.ExecuteAsync(null);

        repository.GitOutput("log", "-1", "--format=%an").Trim().Should().Be("Someone Else");
    }

    [AvaloniaTest]
    public async Task Amend_with_reset_soft_turns_the_last_commit_into_a_new_one_with_the_staged_changes()
    {
        using TestRepository repository = new();
        Identity(repository);
        repository.RunGit("add", "a.txt");
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(repository.Path);
        CommitWindowViewModel window = main.CreateCommitWindowViewModel(new CommitWindowStore(NewStorePath()))!;
        await window.LoadAsync();

        window.Commit.Amend = true;
        await WaitForAsync(() => window.Commit.Message == "Add b.txt");
        window.Commit.ResetSoft = true;
        window.Commit.Message = "Squashed";
        await window.CommitNowCommand.ExecuteAsync(null);

        repository.GitOutput("log", "--format=%s").Trim().Split('\n').Should().Equal("Squashed", "Add a.txt");
        repository.GitOutput("show", "--stat", "--format=", "HEAD").Should().Contain("a.txt").And.Contain("b.txt");
    }

    [AvaloniaTest]
    public async Task Title_names_the_repository_and_branch_and_gitignore_commands_work()
    {
        using TestRepository repository = new();
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(repository.Path);
        CommitWindowViewModel window = main.CreateCommitWindowViewModel(new CommitWindowStore(NewStorePath()))!;
        await window.LoadAsync();
        window.Title.Should().Be($"Commit to {Path.GetFileName(repository.Path)} (main)");

        window.UnstagedSelection = [window.Unstaged.Single(f => f.Name == "c.txt")];
        await window.AddToGitIgnoreCommand.ExecuteAsync(null);
        File.ReadAllText(Path.Combine(repository.Path, ".gitignore")).Should().Contain("/c.txt");
        await WaitForAsync(() => window.AllChanges.All(f => f.Name != "c.txt"));

        // Ignoring it again does not duplicate the entry.
        window.UnstagedSelection = [];
        await window.AddToGitIgnoreCommand.ExecuteAsync(null);
        File.ReadAllLines(Path.Combine(repository.Path, ".gitignore")).Count(l => l == "/c.txt").Should().Be(1);
    }

    [AvaloniaTest]
    public async Task The_commit_template_fills_an_empty_message()
    {
        using TestRepository repository = new();
        File.WriteAllText(Path.Combine(repository.Path, ".gitmessage"), "Subject line\n\n# a comment\nBody\n");
        repository.RunGit("config", "commit.template", ".gitmessage");
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(repository.Path);
        CommitWindowViewModel window = main.CreateCommitWindowViewModel(new CommitWindowStore(NewStorePath()))!;
        await window.LoadAsync();

        window.Commit.Message.Should().StartWith("Subject line").And.Contain("Body").And.NotContain("# a comment");
        window.Templates.Should().Contain(t => t.Name.StartsWith("Template:"));
    }

    [AvaloniaTest]
    public async Task Keys_s_u_and_r_stage_unstage_and_reset_the_focused_pane()
    {
        using TestRepository repository = new();
        CommitWindowStore.Current = new CommitWindowStore(NewStorePath());
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        Window owner = new() { Width = 800, Height = 600 };
        owner.Show();
        await main.OpenAsync(repository.Path);
        WindowService service = new(main, owner);
        main.WindowService = service;
        main.ShowCommitWindowCommand.Execute(null);
        CommitWindow window = service.CommitWindow!;
        CommitWindowViewModel viewModel = (CommitWindowViewModel)window.DataContext!;
        await WaitForAsync(() => viewModel.Branch is not null && !viewModel.IsBusy && viewModel.Unstaged.Count == 2);

        ListBox unstaged = window.FindControl<ListBox>("UnstagedList")!;
        ListBox staged = window.FindControl<ListBox>("StagedList")!;
        unstaged.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        ((Control)unstaged.ContainerFromIndex(0)!).Focus();
        window.KeyPress(Key.S, RawInputModifiers.None, PhysicalKey.S, "s");
        await WaitForAsync(() => viewModel.Staged.Count == 1);

        staged.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        ((Control)staged.ContainerFromIndex(0)!).Focus();
        window.KeyPress(Key.U, RawInputModifiers.None, PhysicalKey.U, "u");
        await WaitForAsync(() => viewModel.Staged.Count == 0);

        unstaged.SelectedIndex = 0;
        Dispatcher.UIThread.RunJobs();
        ((Control)unstaged.ContainerFromIndex(0)!).Focus();
        window.KeyPress(Key.R, RawInputModifiers.None, PhysicalKey.R, "r");
        viewModel.IsConfirmingReset.Should().BeTrue("R asks before discarding");
        viewModel.ConfirmResetCommand.Execute(null);
        await WaitForAsync(() => viewModel.Unstaged.Count == 1);
        repository.GitOutput("status", "--porcelain").Should().NotContain("a.txt");

        window.Close();
        owner.Close();
    }
}
