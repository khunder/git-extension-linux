using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class PatchTests
{
    private string _root = null!;

    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    [SetUp]
    public void CreateRoot() => _root = Directory.CreateTempSubdirectory("ge-avalonia-patch-").FullName;

    [TearDown]
    public void DeleteRoot()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // A repository with commits "one", "two", "three" (file.txt grows by a line each time).
    private string CreateSource(string name = "source")
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        RemoteScenario.Run(dir, "init", "--quiet", "--initial-branch=main");
        Identify(dir);
        RemoteScenario.Commit(dir, "file.txt", "one\n", "one");
        RemoteScenario.Commit(dir, "file.txt", "one\ntwo\n", "two");
        RemoteScenario.Commit(dir, "file.txt", "one\ntwo\nthree\n", "three");
        return dir;
    }

    // A clone of the source, reset to its first commit.
    private string CreateTarget(string source)
    {
        string target = Path.Combine(_root, "target");
        RemoteScenario.Run(_root, "clone", "--quiet", source, target);
        Identify(target);
        RemoteScenario.Run(target, "reset", "--quiet", "--hard", "HEAD~2");
        return target;
    }

    private static void Identify(string dir)
    {
        RemoteScenario.Run(dir, "config", "user.name", "Test Author");
        RemoteScenario.Run(dir, "config", "user.email", "test@example.com");
    }

    private static async Task<MainWindowViewModel> OpenAsync(string path)
    {
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await viewModel.OpenAsync(path);
        return viewModel;
    }

    private async Task<string> FormatSeriesAsync(string source, string folder)
    {
        MainWindowViewModel viewModel = await OpenAsync(source);
        viewModel.ShowFormatPatchCommand.Execute(null);
        viewModel.FormatPatch.OutputFolder = folder;
        viewModel.FormatPatch.OldestRevision = "HEAD~1";
        viewModel.FormatPatch.NewestRevision = "HEAD";
        await viewModel.FormatPatch.CreateCommand.ExecuteAsync(null);
        return folder;
    }

    // ---- Format patch ----

    [AvaloniaTest]
    public async Task Format_patch_writes_one_mail_file_per_commit_of_the_range()
    {
        string source = CreateSource();
        MainWindowViewModel viewModel = await OpenAsync(source);
        string output = Path.Combine(_root, "out");

        viewModel.ShowFormatPatchCommand.Execute(null);
        viewModel.FormatPatch.IsOpen.Should().BeTrue();
        viewModel.FormatPatch.NewestRevision.Should().NotBeNullOrEmpty();
        viewModel.FormatPatch.OutputFolder = output;
        viewModel.FormatPatch.OldestRevision = "HEAD~2";
        viewModel.FormatPatch.NewestRevision = "HEAD";
        await viewModel.FormatPatch.CreateCommand.ExecuteAsync(null);

        viewModel.FormatPatch.ErrorMessage.Should().BeNull();
        viewModel.FormatPatch.CreatedFiles.Should().HaveCount(3);
        Directory.GetFiles(output, "*.patch").Select(Path.GetFileName).Order().Should().Equal("0001-one.patch", "0002-two.patch", "0003-three.patch");
    }

    [AvaloniaTest]
    public async Task Format_patch_single_commit_root_commit_and_diff_format()
    {
        string source = CreateSource();
        MainWindowViewModel viewModel = await OpenAsync(source);
        FormatPatchViewModel format = viewModel.FormatPatch;

        // A single commit, from the context menu entry.
        viewModel.ShowFormatPatchForCommand.Execute(viewModel.Commits.First(c => c.Subject == "two"));
        format.NewestRevision.Should().Be(format.OldestRevision);
        format.OutputFolder = Path.Combine(_root, "single");
        await format.CreateCommand.ExecuteAsync(null);
        format.CreatedFiles.Should().ContainSingle().Which.Should().EndWith("0001-two.patch");
        File.ReadAllText(format.CreatedFiles[0]).Should().Contain("Subject: [PATCH] two").And.Contain("+two");

        // The whole history including the root commit.
        format.OldestRevision = "HEAD~2";
        format.NewestRevision = "HEAD";
        format.OutputFolder = Path.Combine(_root, "all");
        await format.CreateCommand.ExecuteAsync(null);
        format.CreatedFiles.Should().HaveCount(3);

        // Plain diff: one file, no mail header.
        format.MailFormat = false;
        format.OutputFolder = Path.Combine(_root, "plain");
        await format.CreateCommand.ExecuteAsync(null);
        string file = format.CreatedFiles.Should().ContainSingle().Subject;
        string text = File.ReadAllText(file);
        text.Should().StartWith("diff --git").And.Contain("+two").And.Contain("+three").And.NotContain("Subject:");

        // Diff format of a range that starts at the root commit.
        format.OldestRevision = "HEAD~2";
        await format.CreateCommand.ExecuteAsync(null);
        File.ReadAllText(format.CreatedFiles[0]).Should().Contain("+one");
    }

    [AvaloniaTest]
    public async Task Format_patch_reports_invalid_input_inline()
    {
        MainWindowViewModel viewModel = await OpenAsync(CreateSource());
        FormatPatchViewModel format = viewModel.FormatPatch;
        format.Open();

        format.OutputFolder = "relative";
        await format.CreateCommand.ExecuteAsync(null);
        format.HasError.Should().BeTrue();

        format.OutputFolder = Path.Combine(_root, "o");
        format.OldestRevision = "no-such-branch";
        await format.CreateCommand.ExecuteAsync(null);
        format.ErrorMessage.Should().Contain("no-such-branch");

        format.OldestRevision = "--output=x";
        await format.CreateCommand.ExecuteAsync(null);
        format.HasError.Should().BeTrue();
        format.CreatedFiles.Should().BeEmpty();
    }

    // ---- Apply patch ----

    [AvaloniaTest]
    public async Task Apply_patch_folder_with_am_creates_the_commits_and_signs_off()
    {
        string source = CreateSource();
        string patches = await FormatSeriesAsync(source, Path.Combine(_root, "patches"));
        string target = CreateTarget(source);
        MainWindowViewModel viewModel = await OpenAsync(target);

        await viewModel.ShowApplyPatchCommand.ExecuteAsync(null);
        ApplyPatchViewModel apply = viewModel.ApplyPatch;
        apply.IsOpen.Should().BeTrue();
        apply.IsFolder = true;
        apply.PatchPath = patches;
        apply.SignOff = true;
        await apply.ApplyCommand.ExecuteAsync(null);

        apply.ErrorMessage.Should().BeNull(apply.Output);
        apply.IsAmInProgress.Should().BeFalse();
        RemoteScenario.Run(target, "log", "--format=%s", "-3").Split('\n').Should().Equal("three", "two", "one");
        RemoteScenario.Run(target, "log", "-1", "--format=%B").Should().Contain("Signed-off-by: Test Author");
        File.ReadAllText(Path.Combine(target, "file.txt")).Should().Be("one\ntwo\nthree\n");
    }

    [AvaloniaTest]
    public async Task Apply_patch_without_commit_only_changes_the_working_tree()
    {
        string source = CreateSource();
        string patches = await FormatSeriesAsync(source, Path.Combine(_root, "patches"));
        string target = CreateTarget(source);
        MainWindowViewModel viewModel = await OpenAsync(target);

        ApplyPatchViewModel apply = viewModel.ApplyPatch;
        apply.PatchPath = Directory.GetFiles(patches).Order().First();
        apply.Commit = false;
        await apply.ApplyCommand.ExecuteAsync(null);

        apply.ErrorMessage.Should().BeNull(apply.Output);
        RemoteScenario.Run(target, "log", "--format=%s").Split('\n').Should().Equal("one");
        File.ReadAllText(Path.Combine(target, "file.txt")).Should().Be("one\ntwo\n");

        // Applying it again fails and says so.
        await apply.ApplyCommand.ExecuteAsync(null);
        apply.ErrorMessage.Should().Contain("git apply failed");
    }

    [AvaloniaTest]
    public async Task Apply_patch_stops_on_a_conflict_and_can_be_aborted_or_continued()
    {
        string source = CreateSource();
        string patches = await FormatSeriesAsync(source, Path.Combine(_root, "patches"));
        string target = CreateTarget(source);
        RemoteScenario.Commit(target, "file.txt", "one\nTWO\n", "conflicting two");
        MainWindowViewModel viewModel = await OpenAsync(target);

        await viewModel.ShowApplyPatchCommand.ExecuteAsync(null);
        ApplyPatchViewModel apply = viewModel.ApplyPatch;
        apply.IsFolder = true;
        apply.PatchPath = patches;
        apply.ThreeWay = true;
        await apply.ApplyCommand.ExecuteAsync(null);

        apply.IsAmInProgress.Should().BeTrue();
        apply.HasError.Should().BeTrue();
        apply.ConflictedFiles.Should().Equal("file.txt");
        apply.ApplyCommand.CanExecute(null).Should().BeFalse();
        apply.ContinueCommand.CanExecute(null).Should().BeTrue();

        // Abort puts everything back.
        await apply.AbortCommand.ExecuteAsync(null);
        apply.IsAmInProgress.Should().BeFalse();
        apply.HasConflicts.Should().BeFalse();
        RemoteScenario.Run(target, "log", "--format=%s", "-1").Should().Be("conflicting two");

        // Resolve and continue.
        await apply.ApplyCommand.ExecuteAsync(null);
        apply.IsAmInProgress.Should().BeTrue();
        File.WriteAllText(Path.Combine(target, "file.txt"), "one\ntwo\n");
        RemoteScenario.Run(target, "add", "file.txt");
        await apply.ContinueCommand.ExecuteAsync(null);
        apply.IsAmInProgress.Should().BeFalse(apply.Output);
        RemoteScenario.Run(target, "log", "--format=%s", "-2").Split('\n').Should().Equal("three", "two");
    }

    [AvaloniaTest]
    public async Task Apply_patch_validates_the_input()
    {
        MainWindowViewModel viewModel = await OpenAsync(CreateSource());
        ApplyPatchViewModel apply = viewModel.ApplyPatch;

        await apply.ApplyCommand.ExecuteAsync(null);
        apply.HasError.Should().BeTrue();

        apply.PatchPath = Path.Combine(_root, "missing.patch");
        await apply.ApplyCommand.ExecuteAsync(null);
        apply.ErrorMessage.Should().Contain("does not exist");

        string empty = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;
        apply.IsFolder = true;
        apply.PatchPath = empty;
        await apply.ApplyCommand.ExecuteAsync(null);
        apply.ErrorMessage.Should().Contain("no .patch");
    }

    // ---- View patch ----

    [AvaloniaTest]
    public async Task View_patch_shows_a_mail_format_patch_in_the_diff_lines()
    {
        string source = CreateSource();
        string patches = await FormatSeriesAsync(source, Path.Combine(_root, "patches"));
        MainWindowViewModel viewModel = await OpenAsync(source);

        viewModel.ShowViewPatchCommand.Execute(null);
        viewModel.ViewPatch.IsOpen.Should().BeTrue();
        await viewModel.ViewPatch.LoadAsync(Directory.GetFiles(patches).Order().First());

        viewModel.ViewPatch.ErrorMessage.Should().BeNull();
        viewModel.ViewPatch.Lines.Should().Contain(l => l.Kind == DiffLineKind.Added && l.Text == "+two");
        viewModel.ViewPatch.Lines.Should().Contain(l => l.Kind == DiffLineKind.Hunk);
        viewModel.ViewPatch.Summary.Should().Contain("1 file changed").And.Contain("+1");

        await viewModel.ViewPatch.LoadAsync(Path.Combine(_root, "nope.patch"));
        viewModel.ViewPatch.HasError.Should().BeTrue();
        viewModel.ViewPatch.Lines.Should().BeEmpty();
    }

    // ---- Rendering ----

    [AvaloniaTest]
    public async Task Patch_dialogs_render_in_light_and_dark()
    {
        string source = CreateSource();
        string patches = await FormatSeriesAsync(source, Path.Combine(_root, "patches"));
        MainWindowViewModel viewModel = await OpenAsync(source);
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        try
        {
            viewModel.ShowFormatPatchCommand.Execute(null);
            viewModel.FormatPatch.OutputFolder = Path.Combine(_root, "render");
            await viewModel.FormatPatch.CreateCommand.ExecuteAsync(null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            FindOverlay<FormatPatchView>(window, "FormatPatchOverlay").IsVisible.Should().BeTrue();
            Capture(window);

            viewModel.FormatPatch.CloseCommand.Execute(null);
            await viewModel.ShowApplyPatchCommand.ExecuteAsync(null);
            viewModel.ApplyPatch.PatchPath = patches;
            viewModel.ApplyPatch.Output = "Applying: two";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            FindOverlay<ApplyPatchView>(window, "ApplyPatchOverlay").IsVisible.Should().BeTrue();
            FindOverlay<FormatPatchView>(window, "FormatPatchOverlay").IsVisible.Should().BeFalse();
            Capture(window);

            viewModel.ApplyPatch.CloseCommand.Execute(null);
            viewModel.ShowViewPatchCommand.Execute(null);
            await viewModel.ViewPatch.LoadAsync(Directory.GetFiles(patches).Order().First());
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            FindOverlay<ViewPatchView>(window, "ViewPatchOverlay").IsVisible.Should().BeTrue();
            Capture(window);
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            window.Close();
        }
    }

    private static T FindOverlay<T>(MainWindow window, string name)
        where T : Avalonia.Controls.Control
    {
        PatchesHostView host = window.FindControl<PatchesHostView>("PatchesOverlay")!;
        return host.FindControl<T>(name)!;
    }

    private static void Capture(MainWindow window)
    {
        foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Application.Current!.RequestedThemeVariant = variant;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.CaptureRenderedFrame().Should().NotBeNull();
        }
    }
}
