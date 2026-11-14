using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class RevisionOperationsTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class FixedSavePicker(string file) : IFileSavePicker
    {
        public Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension) => Task.FromResult<string?>(file);
    }

    private static void Commit(TestRepository repository, string file, string content, string message)
    {
        File.WriteAllText(Path.Combine(repository.Path, file), content);
        repository.RunGit("add", file);
        repository.RunGit("-c", "user.name=Test Author", "-c", "user.email=test@example.com", "commit", "--quiet", "-m", message);
    }

    /// <summary>A clean repository on main with branch <c>feature</c> holding one extra commit.</summary>
    private static async Task<(TestRepository Repository, MainWindowViewModel ViewModel, string FeatureSha)> OpenAsync(bool clean = true)
    {
        TestRepository repository = new();
        repository.RunGit("checkout", "--quiet", "--", "a.txt");
        File.Delete(Path.Combine(repository.Path, "c.txt"));
        repository.RunGit("checkout", "--quiet", "-b", "feature");
        Commit(repository, "f.txt", "feature", "Feature work");
        string sha = repository.GitOutput("rev-parse", "HEAD").Trim();
        repository.RunGit("checkout", "--quiet", "main");
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await viewModel.OpenAsync(repository.Path);
        _ = clean;
        return (repository, viewModel, sha);
    }

    [AvaloniaTest]
    public async Task Cherry_pick_commits_with_the_original_id_when_asked()
    {
        (TestRepository repository, MainWindowViewModel viewModel, string sha) = await OpenAsync();
        using (repository)
        {
            RevisionOperationsViewModel ops = viewModel.RevisionOps;
            ops.OpenCherryPickCommand.Execute(null);
            ops.IsCherryPick.Should().BeTrue();
            ops.Revision = "feature";
            ops.AddReference = true;
            await ops.ConfirmAsync();

            ops.IsOpen.Should().BeFalse();
            repository.GitOutput("log", "-1", "--format=%s").Trim().Should().Be("Feature work");
            repository.GitOutput("log", "-1", "--format=%B").Should().Contain("(cherry picked from commit " + sha);
            File.Exists(Path.Combine(repository.Path, "f.txt")).Should().BeTrue();
        }
    }

    [AvaloniaTest]
    public async Task Cherry_pick_without_auto_commit_only_stages_the_change()
    {
        (TestRepository repository, MainWindowViewModel viewModel, _) = await OpenAsync();
        using (repository)
        {
            string head = repository.GitOutput("rev-parse", "HEAD");
            RevisionOperationsViewModel ops = viewModel.RevisionOps;
            ops.OpenCherryPickCommand.Execute(null);
            ops.Revision = "feature";
            ops.AutoCommit = false;
            await ops.ConfirmAsync();

            repository.GitOutput("rev-parse", "HEAD").Should().Be(head);
            repository.GitOutput("status", "--porcelain").Should().Contain("A  f.txt");
        }
    }

    [AvaloniaTest]
    public async Task Cherry_pick_of_a_merge_asks_for_the_mainline_parent()
    {
        (TestRepository repository, MainWindowViewModel viewModel, _) = await OpenAsync();
        using (repository)
        {
            repository.RunGit("-c", "user.name=T", "-c", "user.email=t@example.com", "merge", "--no-ff", "--quiet", "-m", "Merge feature", "feature");
            repository.RunGit("checkout", "--quiet", "-b", "other", "HEAD~1");
            await viewModel.OpenAsync(repository.Path);

            RevisionOperationsViewModel ops = viewModel.RevisionOps;
            ops.OpenCherryPickCommand.Execute(null);
            ops.Revision = "main";
            await ops.ConfirmAsync();
            ops.IsOpen.Should().BeTrue();
            ops.ValidationError.Should().Contain("merge");
            ops.MainlineParent.Should().Be(1);

            await ops.ConfirmAsync();
            ops.IsOpen.Should().BeFalse();
            repository.GitOutput("log", "-1", "--format=%s").Trim().Should().Be("Merge feature");
            File.Exists(Path.Combine(repository.Path, "f.txt")).Should().BeTrue();
        }
    }

    [AvaloniaTest]
    public async Task Checkout_revision_detaches_head_at_any_expression_and_rejects_bad_ones()
    {
        (TestRepository repository, MainWindowViewModel viewModel, _) = await OpenAsync();
        using (repository)
        {
            RevisionOperationsViewModel ops = viewModel.RevisionOps;
            ops.OpenCheckoutRevisionCommand.Execute(null);
            ops.Revision = "no-such-revision";
            await ops.ConfirmAsync();
            ops.IsOpen.Should().BeTrue();
            ops.ValidationError.Should().Contain("not a valid revision");

            ops.Revision = "main~1";
            await ops.ConfirmAsync();
            ops.IsOpen.Should().BeFalse();
            repository.GitOutput("branch", "--show-current").Trim().Should().BeEmpty();
            repository.GitOutput("rev-parse", "HEAD").Should().Be(repository.GitOutput("rev-parse", "main~1"));
        }
    }

    [AvaloniaTest]
    public async Task Checkout_revision_can_throw_away_local_changes()
    {
        (TestRepository repository, MainWindowViewModel viewModel, _) = await OpenAsync();
        using (repository)
        {
            File.WriteAllText(Path.Combine(repository.Path, "b.txt"), "dirty");
            RevisionOperationsViewModel ops = viewModel.RevisionOps;
            ops.OpenCheckoutRevisionCommand.Execute(null);
            ops.Revision = "main~1";
            ops.IsKeepLocalChanges.Should().BeTrue();
            await ops.ConfirmAsync();
            ErrorShown(viewModel).Should().BeTrue("git refuses to overwrite local changes");

            ops.OpenCheckoutRevisionCommand.Execute(null);
            ops.Revision = "main~1";
            ops.IsResetLocalChanges = true;
            ops.HasWarning.Should().BeTrue();
            await ops.ConfirmAsync();
            repository.GitOutput("status", "--porcelain").Should().BeEmpty();
            repository.GitOutput("branch", "--show-current").Trim().Should().BeEmpty();
        }
    }

    private static bool ErrorShown(MainWindowViewModel viewModel) => viewModel.HasError;

    [AvaloniaTest]
    public async Task Delete_branch_deletes_the_selected_branches_but_never_the_current_one()
    {
        (TestRepository repository, MainWindowViewModel viewModel, _) = await OpenAsync();
        using (repository)
        {
            repository.RunGit("branch", "merged");
            RevisionOperationsViewModel ops = viewModel.RevisionOps;
            await ops.OpenDeleteBranchAsync();
            ops.Branches.Select(b => b.Name).Should().Contain(["main", "feature", "merged"]);
            ops.Branches.Single(b => b.Name == "main").CanSelect.Should().BeFalse();

            ops.Branches.Single(b => b.Name == "main").IsSelected = true;
            await ops.ConfirmAsync();
            ops.ValidationError.Should().Contain("Select at least one");

            ops.Branches.Single(b => b.Name == "merged").IsSelected = true;
            await ops.ConfirmAsync();
            repository.GitOutput("branch", "--list", "merged").Should().BeEmpty();
            repository.GitOutput("branch", "--list", "main").Should().NotBeEmpty();
        }
    }

    [AvaloniaTest]
    public async Task Delete_branch_needs_force_for_unmerged_branches()
    {
        (TestRepository repository, MainWindowViewModel viewModel, _) = await OpenAsync();
        using (repository)
        {
            RevisionOperationsViewModel ops = viewModel.RevisionOps;
            await ops.OpenDeleteBranchAsync();
            ops.Branches.Single(b => b.Name == "feature").IsSelected = true;
            await ops.ConfirmAsync();
            repository.GitOutput("branch", "--list", "feature").Should().NotBeEmpty();
            viewModel.HasError.Should().BeTrue();

            await ops.OpenDeleteBranchAsync();
            ops.Branches.Single(b => b.Name == "feature").IsSelected = true;
            ops.ForceDelete = true;
            await ops.ConfirmAsync();
            repository.GitOutput("branch", "--list", "feature").Should().BeEmpty();
        }
    }

    [AvaloniaTest]
    public async Task Delete_branch_can_delete_a_branch_on_the_remote()
    {
        (TestRepository repository, MainWindowViewModel viewModel, _) = await OpenAsync();
        using (repository)
        {
            string remote = Path.Combine(Path.GetTempPath(), "ge-avalonia-remote-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(remote);
                repository.RunGit("init", "--quiet", "--bare", remote);
                repository.RunGit("remote", "add", "origin", remote);
                repository.RunGit("push", "--quiet", "origin", "main", "feature");
                await viewModel.OpenAsync(repository.Path);

                RevisionOperationsViewModel ops = viewModel.RevisionOps;
                await ops.OpenDeleteBranchAsync();
                ops.Branches.Single(b => b.Name == "origin/feature").IsRemote.Should().BeTrue();
                ops.Branches.Single(b => b.Name == "origin/feature").IsSelected = true;
                await ops.ConfirmAsync();
                await ReflogTests.WaitAsync(() => !viewModel.Remote.IsRunning && repository.GitOutput("--git-dir=" + remote, "branch", "--list", "feature").Length == 0);
                repository.GitOutput("--git-dir=" + remote, "branch", "--list", "main").Should().NotBeEmpty();
            }
            finally
            {
                Directory.Delete(remote, recursive: true);
            }
        }
    }

    [AvaloniaTest]
    public async Task Archive_writes_a_zip_with_an_optional_path_filter()
    {
        (TestRepository repository, MainWindowViewModel viewModel, _) = await OpenAsync();
        using (repository)
        {
            string folder = Path.Combine(Path.GetTempPath(), "ge-avalonia-archive-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                RevisionOperationsViewModel ops = viewModel.RevisionOps;
                ops.OpenArchiveCommand.Execute(null);
                ops.Revision = "main";
                ops.SavePicker = new FixedSavePicker(Path.Combine(folder, "all.zip"));
                await ops.ConfirmAsync();
                using (ZipArchive zip = ZipFile.OpenRead(Path.Combine(folder, "all.zip")))
                {
                    zip.Entries.Select(e => e.FullName).Should().BeEquivalentTo(["a.txt", "b.txt"]);
                }

                ops.OpenArchiveCommand.Execute(null);
                ops.Revision = "feature";
                ops.ArchivePath = "f.txt";
                ops.ArchiveFile = Path.Combine(folder, "one.zip");
                await ops.ConfirmAsync();
                using (ZipArchive zip = ZipFile.OpenRead(Path.Combine(folder, "one.zip")))
                {
                    zip.Entries.Select(e => e.FullName).Should().BeEquivalentTo(["f.txt"]);
                }

                ops.OpenArchiveCommand.Execute(null);
                ops.ArchiveFormat = "tar";
                ops.ArchiveFile = Path.Combine(folder, "all.tar");
                await ops.ConfirmAsync();
                new FileInfo(Path.Combine(folder, "all.tar")).Length.Should().BeGreaterThan(0);
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [AvaloniaTest]
    public async Task Reset_soft_keeps_the_index_and_hard_needs_a_second_confirmation()
    {
        (TestRepository repository, MainWindowViewModel viewModel, _) = await OpenAsync();
        using (repository)
        {
            string first = repository.GitOutput("rev-parse", "main~1").Trim();
            RevisionOperationsViewModel ops = viewModel.RevisionOps;
            ops.OpenResetCommand.Execute(null);
            ops.Revision = "main~1";
            ops.IsSoftReset = true;
            await ops.ConfirmAsync();
            repository.GitOutput("rev-parse", "HEAD").Trim().Should().Be(first);
            repository.GitOutput("status", "--porcelain").Should().Contain("A  b.txt");

            File.WriteAllText(Path.Combine(repository.Path, "a.txt"), "dirty");
            ops.OpenResetCommand.Execute(null);
            ops.Revision = "HEAD";
            ops.IsHardReset = true;
            ops.HasWarning.Should().BeTrue();
            await ops.ConfirmAsync();
            ops.IsOpen.Should().BeTrue("a hard reset needs confirmation");
            File.ReadAllText(Path.Combine(repository.Path, "a.txt")).Should().Be("dirty");
            ops.ConfirmText.Should().Contain("Yes");

            await ops.ConfirmAsync();
            ops.IsOpen.Should().BeFalse();
            repository.GitOutput("status", "--porcelain").Should().BeEmpty();
        }
    }

    [AvaloniaTest]
    public async Task Clean_previews_before_deleting_and_honours_the_options()
    {
        (TestRepository repository, MainWindowViewModel viewModel, _) = await OpenAsync();
        using (repository)
        {
            File.WriteAllText(Path.Combine(repository.Path, ".gitignore"), "*.log\n");
            repository.RunGit("add", ".gitignore");
            repository.RunGit("-c", "user.name=T", "-c", "user.email=t@example.com", "commit", "--quiet", "-m", "ignore");
            File.WriteAllText(Path.Combine(repository.Path, "junk.txt"), "x");
            File.WriteAllText(Path.Combine(repository.Path, "debug.log"), "x");
            Directory.CreateDirectory(Path.Combine(repository.Path, "tmpdir"));
            File.WriteAllText(Path.Combine(repository.Path, "tmpdir", "inner.txt"), "x");

            RevisionOperationsViewModel ops = viewModel.RevisionOps;
            ops.OpenCleanCommand.Execute(null);
            await ops.ConfirmAsync();
            ops.Previewed.Should().BeTrue();
            ops.CleanPreview.Should().BeEquivalentTo(["junk.txt", "tmpdir/"]);
            File.Exists(Path.Combine(repository.Path, "junk.txt")).Should().BeTrue("the preview must not delete");

            ops.IsIgnoredInclude = true;
            ops.Previewed.Should().BeFalse("changing an option invalidates the preview");
            await ops.ConfirmAsync();
            ops.CleanPreview.Should().BeEquivalentTo(["debug.log", "junk.txt", "tmpdir/"]);

            await ops.ConfirmAsync();
            ops.IsOpen.Should().BeFalse();
            File.Exists(Path.Combine(repository.Path, "junk.txt")).Should().BeFalse();
            File.Exists(Path.Combine(repository.Path, "debug.log")).Should().BeFalse();
            Directory.Exists(Path.Combine(repository.Path, "tmpdir")).Should().BeFalse();
            File.Exists(Path.Combine(repository.Path, "a.txt")).Should().BeTrue();
        }
    }

    [AvaloniaTest]
    public async Task Clean_without_directories_keeps_untracked_folders()
    {
        (TestRepository repository, MainWindowViewModel viewModel, _) = await OpenAsync();
        using (repository)
        {
            Directory.CreateDirectory(Path.Combine(repository.Path, "tmpdir"));
            File.WriteAllText(Path.Combine(repository.Path, "tmpdir", "inner.txt"), "x");
            File.WriteAllText(Path.Combine(repository.Path, "junk.txt"), "x");
            RevisionOperationsViewModel ops = viewModel.RevisionOps;
            ops.OpenCleanCommand.Execute(null);
            ops.IncludeDirectories = false;
            await ops.ConfirmAsync();
            ops.CleanPreview.Should().BeEquivalentTo(["junk.txt"]);
            await ops.ConfirmAsync();
            Directory.Exists(Path.Combine(repository.Path, "tmpdir")).Should().BeTrue();
        }
    }

    [AvaloniaTest]
    public async Task Menu_entries_open_the_dialogs_and_the_dialog_renders_in_both_themes()
    {
        (TestRepository repository, MainWindowViewModel viewModel, _) = await OpenAsync();
        using (repository)
        {
            MainWindow window = new() { DataContext = viewModel, Width = 1000, Height = 700 };
            window.Show();
            await viewModel.OpenAsync(repository.Path);

            RevisionOperationPromptView view = window.GetVisualDescendants().OfType<RevisionOperationPromptView>().Single();
            view.IsVisible.Should().BeFalse();

            viewModel.ShowCherryPickCommand.Execute(null);
            view.IsVisible.Should().BeTrue();
            Capture(window, "cherry-pick");

            viewModel.ShowResetCommand.Execute(null);
            viewModel.RevisionOps.IsHardReset = true;
            Capture(window, "reset");

            await viewModel.ShowDeleteBranchCommand.ExecuteAsync(null);
            Capture(window, "delete-branch");

            viewModel.ShowCleanCommand.Execute(null);
            Capture(window, "clean");

            viewModel.RevisionOps.CancelCommand.Execute(null);
            view.IsVisible.Should().BeFalse();
            window.Close();
        }
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
            string file = Path.Combine(directory, $"revops-{name}-{variant.Key.ToString()!.ToLowerInvariant()}.png");
            frame!.Save(file);
            TestContext.AddTestAttachment(file);
        }
    }
}
