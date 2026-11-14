using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class HunkOperationTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static async Task<(MainWindowViewModel ViewModel, FileDiffViewModel Diff)> ShowAsync(MultiHunkRepository repo, string name, bool staged)
    {
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await viewModel.OpenAsync(repo.Path);
        ChangedFileViewModel file = viewModel.ChangedFiles.Single(f => f.Name == name && f.IsStaged == staged);
        viewModel.SelectedChangedFile = file;
        await viewModel.Diff!.ShowAsync(file);
        return (viewModel, viewModel.Diff);
    }

    /// <summary>
    ///  After an operation the selection is restored and its diff reloads in the background; wait for that.
    /// </summary>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        condition().Should().BeTrue("the diff should have been reloaded");
    }

    private static Task HunksAsync(FileDiffViewModel diff, int count) => WaitForAsync(() => !diff.IsLoading && diff.Lines.Count(l => l.Kind == DiffLineKind.Hunk) == count);

    /// <summary>The added/removed lines of <c>git diff -U0 ...</c>.</summary>
    private static string[] Changes(MultiHunkRepository repo, params string[] arguments)
        => [.. repo.Git([arguments[0], .. arguments[1..].Where(a => a.StartsWith('-')), "-U0", .. arguments[1..].Where(a => !a.StartsWith('-'))]).Split('\n').Where(l => (l.StartsWith('+') || l.StartsWith('-')) && !l.StartsWith("+++", StringComparison.Ordinal) && !l.StartsWith("---", StringComparison.Ordinal))];

    private static DiffLine Hunk(FileDiffViewModel diff, int index) => diff.Lines.Single(l => l.Kind == DiffLineKind.Hunk && l.HunkIndex == index);

    private static DiffLine Line(FileDiffViewModel diff, string text) => diff.Lines.Single(l => l.Text == text);

    [Test]
    public void Patch_builder_numbers_hunks_in_the_parser()
    {
        IReadOnlyList<DiffLine> lines = UnifiedDiffParser.Parse("diff --git a/x b/x\n--- a/x\n+++ b/x\n@@ -1,2 +1,2 @@\n-a\n+b\n c\n@@ -9 +9 @@\n-x\n+y\n");
        lines.Where(l => l.Kind == DiffLineKind.Hunk).Select(l => l.HunkIndex).Should().Equal(0, 1);
        lines.Where(l => l.Kind == DiffLineKind.Header).Should().OnlyContain(l => l.HunkIndex == -1);
    }

    [AvaloniaTest]
    public async Task Staging_the_middle_hunk_leaves_the_other_hunks_unstaged()
    {
        using MultiHunkRepository repo = new();
        (_, FileDiffViewModel diff) = await ShowAsync(repo, "m.txt", staged: false);
        diff.Lines.Count(l => l.Kind == DiffLineKind.Hunk).Should().Be(3);
        Hunk(diff, 1).CanStageHunk.Should().BeTrue();
        Hunk(diff, 1).CanUnstageHunk.Should().BeFalse();

        await diff.StageHunkCommand.ExecuteAsync(Hunk(diff, 1));

        Changes(repo, "diff", "--cached").Should().Equal("-line 15", "+changed 15");
        Changes(repo, "diff").Should().Equal("-line 3", "+changed 3", "-line 27", "+changed 27");

        // The viewer shows what is left of the working tree diff.
        await HunksAsync(diff, 2);
    }

    [AvaloniaTest]
    public async Task Staging_the_last_hunk_and_then_the_first_works_with_shifted_offsets()
    {
        using MultiHunkRepository repo = new();
        repo.Write("m.txt", string.Join('\n', Enumerable.Range(1, 30).Select(i => i == 3 ? "changed 3\nextra 3" : i == 27 ? "changed 27" : "line " + i)) + "\n");
        (_, FileDiffViewModel diff) = await ShowAsync(repo, "m.txt", staged: false);
        diff.Lines.Count(l => l.Kind == DiffLineKind.Hunk).Should().Be(2);

        await diff.StageHunkCommand.ExecuteAsync(Hunk(diff, 1));
        await HunksAsync(diff, 1);
        await diff.StageHunkCommand.ExecuteAsync(Hunk(diff, 0));

        repo.Git("diff").Should().BeEmpty();
        repo.Git("show", ":m.txt").Should().Contain("extra 3").And.Contain("changed 27");
    }

    [AvaloniaTest]
    public async Task Staging_selected_lines_stages_only_those_lines()
    {
        using MultiHunkRepository repo = new();
        repo.Write("m.txt", string.Join('\n', Enumerable.Range(1, 30).Select(i => i == 3 ? "changed 3" : i == 4 ? "changed 4" : "line " + i)) + "\n");
        (_, FileDiffViewModel diff) = await ShowAsync(repo, "m.txt", staged: false);

        diff.SelectLine(Line(diff, "-line 3"), ctrl: false, shift: false);
        diff.SelectLine(Line(diff, "+changed 3"), ctrl: true, shift: false);
        diff.SelectedLineCount.Should().Be(2);
        diff.CanStageSelected.Should().BeTrue();
        diff.CanUnstageSelected.Should().BeFalse();
        await diff.StageSelectedCommand.ExecuteAsync(null);

        string index = repo.Git("show", ":m.txt");
        index.Should().Contain("changed 3\n").And.Contain("line 4\n").And.NotContain("changed 4");
        Changes(repo, "diff").Should().Equal("-line 4", "+changed 4");

        // Selection is cleared by the operation.
        diff.SelectedLineCount.Should().Be(0);
        diff.Lines.Should().OnlyContain(l => !l.IsSelected);
    }

    [AvaloniaTest]
    public async Task Staging_only_an_added_line_keeps_the_removed_line_in_the_index()
    {
        using MultiHunkRepository repo = new();
        (_, FileDiffViewModel diff) = await ShowAsync(repo, "m.txt", staged: false);

        diff.SelectLine(Line(diff, "+changed 3"), ctrl: false, shift: false);
        await diff.StageSelectedCommand.ExecuteAsync(null);

        repo.Git("show", ":m.txt").Should().Contain("line 3\nchanged 3\nline 4\n");
    }

    [AvaloniaTest]
    public async Task Selection_supports_click_ctrl_toggle_and_shift_range()
    {
        using MultiHunkRepository repo = new();
        (_, FileDiffViewModel diff) = await ShowAsync(repo, "m.txt", staged: false);
        DiffLine first = Line(diff, "-line 3");
        DiffLine second = Line(diff, "+changed 3");
        DiffLine other = Line(diff, "+changed 15");

        diff.SelectLine(first, false, false);
        diff.SelectedLineCount.Should().Be(1);
        diff.SelectLine(other, false, false);
        diff.SelectedLineCount.Should().Be(1);
        other.IsSelected.Should().BeTrue();
        diff.SelectLine(second, true, false);
        diff.SelectedLineCount.Should().Be(2);
        diff.SelectLine(second, true, false);
        diff.SelectedLineCount.Should().Be(1);

        // Shift selects the range of changed lines from the anchor (context lines in between are skipped).
        diff.SelectLine(first, false, false);
        diff.SelectLine(Line(diff, "+changed 15"), false, true);
        diff.Lines.Where(l => l.IsSelected).Select(l => l.Text).Should().Contain(["-line 3", "+changed 3", "-line 15", "+changed 15"]);
        diff.Lines.Where(l => l.IsSelected).Should().OnlyContain(l => l.IsChange);

        diff.SelectLine(diff.Lines.First(l => l.Kind == DiffLineKind.Context), false, false);
        diff.SelectedLineCount.Should().Be(4);
        diff.ClearSelection();
        diff.HasSelection.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Unstage_hunk_removes_only_that_hunk_from_the_index()
    {
        using MultiHunkRepository repo = new();
        repo.Git("add", "m.txt");
        (_, FileDiffViewModel diff) = await ShowAsync(repo, "m.txt", staged: true);
        Hunk(diff, 1).CanUnstageHunk.Should().BeTrue();
        Hunk(diff, 1).CanStageHunk.Should().BeFalse();
        Hunk(diff, 1).CanDiscardHunk.Should().BeFalse();

        await diff.UnstageHunkCommand.ExecuteAsync(Hunk(diff, 1));

        Changes(repo, "diff", "--cached").Should().Equal("-line 3", "+changed 3", "-line 27", "+changed 27");
        Changes(repo, "diff").Should().Equal("-line 15", "+changed 15");
    }

    [AvaloniaTest]
    public async Task Unstage_selected_lines_of_a_staged_hunk()
    {
        using MultiHunkRepository repo = new();
        repo.Git("add", "m.txt");
        (_, FileDiffViewModel diff) = await ShowAsync(repo, "m.txt", staged: true);

        diff.SelectLine(Line(diff, "-line 15"), false, false);
        diff.SelectLine(Line(diff, "+changed 15"), true, false);
        diff.CanUnstageSelected.Should().BeTrue();
        diff.CanStageSelected.Should().BeFalse();
        diff.CanDiscardSelected.Should().BeFalse();
        await diff.UnstageSelectedCommand.ExecuteAsync(null);

        repo.Git("show", ":m.txt").Should().Contain("line 15\n").And.Contain("changed 3\n").And.Contain("changed 27");
        Changes(repo, "diff").Should().Equal("-line 15", "+changed 15");
    }

    [AvaloniaTest]
    public async Task File_with_staged_and_unstaged_changes_keeps_both_entries_consistent()
    {
        using MultiHunkRepository repo = new();
        (MainWindowViewModel viewModel, FileDiffViewModel diff) = await ShowAsync(repo, "m.txt", staged: false);

        await diff.StageHunkCommand.ExecuteAsync(Hunk(diff, 0));
        await HunksAsync(diff, 2);

        viewModel.ChangedFiles.Should().Contain(f => f.Name == "m.txt" && f.IsStaged);
        viewModel.ChangedFiles.Should().Contain(f => f.Name == "m.txt" && !f.IsStaged);
        await diff.ShowAsync(viewModel.ChangedFiles.Single(f => f.Name == "m.txt" && f.IsStaged));
        diff.Lines.Count(l => l.Kind == DiffLineKind.Hunk).Should().Be(1);
        await diff.ShowAsync(viewModel.ChangedFiles.Single(f => f.Name == "m.txt" && !f.IsStaged));
        diff.Lines.Count(l => l.Kind == DiffLineKind.Hunk).Should().Be(2);
    }

    [AvaloniaTest]
    public async Task Discard_hunk_requires_confirmation_and_only_then_changes_the_file()
    {
        using MultiHunkRepository repo = new();
        string before = repo.Read("m.txt");
        (_, FileDiffViewModel diff) = await ShowAsync(repo, "m.txt", staged: false);
        Hunk(diff, 1).CanDiscardHunk.Should().BeTrue();

        diff.DiscardHunkCommand.Execute(Hunk(diff, 1));

        diff.IsConfirmingDiscard.Should().BeTrue();
        diff.ConfirmationText.Should().Be("Discard 2 lines from m.txt? This cannot be undone.");
        repo.Read("m.txt").Should().Be(before, "nothing may change before the confirmation");

        diff.CancelDiscardCommand.Execute(null);
        diff.IsConfirmingDiscard.Should().BeFalse();
        await diff.ConfirmDiscardCommand.ExecuteAsync(null);
        repo.Read("m.txt").Should().Be(before, "a cancelled or absent confirmation discards nothing");

        diff.DiscardHunkCommand.Execute(Hunk(diff, 1));
        await diff.ConfirmDiscardCommand.ExecuteAsync(null);

        repo.Read("m.txt").Should().Be(before.Replace("changed 15", "line 15", StringComparison.Ordinal));
        repo.Git("diff", "--cached").Should().BeEmpty();
        diff.IsConfirmingDiscard.Should().BeFalse();
        await HunksAsync(diff, 2);
    }

    [AvaloniaTest]
    public async Task Discard_selected_lines_reverts_only_the_selection()
    {
        using MultiHunkRepository repo = new();
        repo.Write("m.txt", string.Join('\n', Enumerable.Range(1, 30).Select(i => i == 3 ? "changed 3" : i == 4 ? "changed 4" : "line " + i)) + "\n");
        (_, FileDiffViewModel diff) = await ShowAsync(repo, "m.txt", staged: false);

        diff.SelectLine(Line(diff, "-line 4"), false, false);
        diff.SelectLine(Line(diff, "+changed 4"), true, false);
        diff.DiscardSelectedCommand.Execute(null);
        diff.ConfirmationText.Should().Be("Discard 2 lines from m.txt? This cannot be undone.");
        repo.Read("m.txt").Should().Contain("changed 4");

        await diff.ConfirmDiscardCommand.ExecuteAsync(null);

        string content = repo.Read("m.txt");
        content.Should().Contain("changed 3\nline 4\n").And.NotContain("changed 4");
    }

    [AvaloniaTest]
    public async Task Crlf_files_keep_their_line_endings_when_staging_a_hunk()
    {
        using MultiHunkRepository repo = new();
        repo.Git("config", "core.autocrlf", "false");
        string[] original = [.. Enumerable.Range(1, 30).Select(i => "row " + i)];
        repo.WriteBytes("crlf.txt", System.Text.Encoding.ASCII.GetBytes(string.Join("\r\n", original) + "\r\n"));
        repo.CommitAll("Add crlf.txt");
        string[] edited = [.. original];
        edited[1] = "edited 2";
        edited[25] = "edited 26";
        repo.WriteBytes("crlf.txt", System.Text.Encoding.ASCII.GetBytes(string.Join("\r\n", edited) + "\r\n"));
        (_, FileDiffViewModel diff) = await ShowAsync(repo, "crlf.txt", staged: false);

        await diff.StageHunkCommand.ExecuteAsync(Hunk(diff, 1));

        string index = repo.Git("show", ":crlf.txt");
        index.Should().Contain("edited 26\r\n").And.Contain("row 2\r\n").And.NotContain("edited 2\r\n");
        index.Replace("\r\n", string.Empty, StringComparison.Ordinal).Should().NotContain("\r").And.NotContain("\n");
        Changes(repo, "diff", "crlf.txt").Select(l => l.TrimEnd('\r')).Should().Equal("-row 2", "+edited 2");

        // Select lines in a CRLF file.
        await HunksAsync(diff, 1);
        diff.SelectLine(Line(diff, "+edited 2"), false, false);
        await diff.StageSelectedCommand.ExecuteAsync(null);
        repo.Git("show", ":crlf.txt").Should().Contain("edited 2\r\n");
    }

    [AvaloniaTest]
    public async Task Files_without_a_trailing_newline_can_be_staged_by_hunk_and_by_lines()
    {
        using MultiHunkRepository repo = new();
        string[] original = [.. Enumerable.Range(1, 30).Select(i => "n " + i)];
        repo.Write("nonl.txt", string.Join('\n', original));
        repo.CommitAll("Add nonl.txt");
        string[] edited = [.. original];
        edited[1] = "edited 2";
        edited[29] = "edited 30";
        repo.Write("nonl.txt", string.Join('\n', edited));
        (_, FileDiffViewModel diff) = await ShowAsync(repo, "nonl.txt", staged: false);
        diff.Lines.Should().Contain(l => l.Kind == DiffLineKind.Info);

        await diff.StageHunkCommand.ExecuteAsync(Hunk(diff, 0));
        repo.Git("show", ":nonl.txt").Should().Contain("edited 2\n").And.EndWith("n 30");
        await HunksAsync(diff, 1);

        await diff.StageHunkCommand.ExecuteAsync(Hunk(diff, 0));
        repo.Git("show", ":nonl.txt").Should().Be(string.Join('\n', edited));
        repo.Git("diff").Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Selected_lines_of_a_hunk_at_the_end_of_a_file_without_newline_are_staged()
    {
        using MultiHunkRepository repo = new();
        repo.Write("nonl.txt", "a\nb\nc");
        repo.CommitAll("Add nonl.txt");
        repo.Write("nonl.txt", "a\nb\nc\nd\ne");
        (_, FileDiffViewModel diff) = await ShowAsync(repo, "nonl.txt", staged: false);
        diff.Lines.Select(l => l.Text).Should().Contain("+d");

        // The old last line "c" changes (gains a newline) and d, e are added; stage only the first addition.
        diff.SelectLine(Line(diff, "-c"), false, false);
        diff.SelectLine(Line(diff, "+c"), true, false);
        diff.SelectLine(Line(diff, "+d"), true, false);
        await diff.StageSelectedCommand.ExecuteAsync(null);

        repo.Git("show", ":nonl.txt").Should().Be("a\nb\nc\nd\n");
    }

    [AvaloniaTest]
    public async Task Untracked_files_are_staged_as_a_whole_and_cannot_be_partially_selected()
    {
        using MultiHunkRepository repo = new();
        repo.Write("fresh.txt", "one\ntwo\n");
        (MainWindowViewModel viewModel, FileDiffViewModel diff) = await ShowAsync(repo, "fresh.txt", staged: false);
        DiffLine hunk = Hunk(diff, 0);
        hunk.CanStageHunk.Should().BeTrue();
        hunk.CanDiscardHunk.Should().BeFalse();

        diff.SelectLine(Line(diff, "+one"), false, false);
        diff.HasSelection.Should().BeFalse();

        await diff.StageHunkCommand.ExecuteAsync(hunk);

        repo.Git("status", "--porcelain", "fresh.txt").Should().StartWith("A  fresh.txt");
        viewModel.ChangedFiles.Should().Contain(f => f.Name == "fresh.txt" && f.IsStaged);
    }

    [AvaloniaTest]
    public async Task Deleted_files_can_be_partially_staged_by_lines()
    {
        using MultiHunkRepository repo = new();
        repo.Write("gone.txt", "1\n2\n3\n4\n");
        repo.CommitAll("Add gone.txt");
        File.Delete(repo.FullPath("gone.txt"));
        (_, FileDiffViewModel diff) = await ShowAsync(repo, "gone.txt", staged: false);
        diff.Lines.Should().Contain(l => l.Kind == DiffLineKind.Removed && l.Text == "-2");

        diff.SelectLine(Line(diff, "-2"), false, false);
        await diff.StageSelectedCommand.ExecuteAsync(null);

        repo.Git("show", ":gone.txt").Should().Be("1\n3\n4\n");

        // Staging the whole remaining deletion removes the file from the index.
        (_, FileDiffViewModel next) = await ShowAsync(repo, "gone.txt", staged: false);
        await next.StageHunkCommand.ExecuteAsync(Hunk(next, 0));
        repo.Git("ls-files", "gone.txt").Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Unstaging_selected_lines_of_a_new_file_keeps_the_rest_staged()
    {
        using MultiHunkRepository repo = new();
        repo.Write("added.txt", "1\n2\n3\n");
        repo.Git("add", "added.txt");
        (_, FileDiffViewModel diff) = await ShowAsync(repo, "added.txt", staged: true);

        diff.SelectLine(Line(diff, "+2"), false, false);
        await diff.UnstageSelectedCommand.ExecuteAsync(null);

        repo.Git("show", ":added.txt").Should().Be("1\n3\n");
        repo.Read("added.txt").Should().Be("1\n2\n3\n");
    }

    [AvaloniaTest]
    public async Task Binary_files_offer_no_partial_operations()
    {
        using MultiHunkRepository repo = new();
        repo.WriteBytes("bin.dat", [0, 1, 2, 3, 4]);
        repo.CommitAll("Add bin.dat");
        repo.WriteBytes("bin.dat", [0, 1, 2, 9, 9, 9]);
        (_, FileDiffViewModel diff) = await ShowAsync(repo, "bin.dat", staged: false);

        diff.Lines.Should().OnlyContain(l => !l.CanStageHunk && !l.CanUnstageHunk && !l.CanDiscardHunk);
        diff.Lines.Should().NotContain(l => l.IsChange);
    }

    [AvaloniaTest]
    public async Task Multi_hunk_diff_with_selection_renders_in_light_and_dark()
    {
        using MultiHunkRepository repo = new();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await viewModel.OpenAsync(repo.Path);
        ChangedFileViewModel file = viewModel.ChangedFiles.Single(f => f.Name == "m.txt");
        viewModel.SelectedChangedFile = file;
        await viewModel.Diff!.ShowAsync(file);
        FileDiffViewModel diff = viewModel.Diff;

        // The diff viewer alone, at a size where the whole multi-hunk diff is visible.
        Window window = new() { Content = new DiffView { DataContext = diff }, Width = 760, Height = 640 };
        window.Show();
        diff.SelectLine(Line(diff, "+changed 3"), false, false);
        diff.SelectLine(Line(diff, "+changed 15"), true, false);
        diff.SelectLine(Line(diff, "-line 15"), true, false);

        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        foreach ((ThemeVariant variant, string name) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
        {
            window.RequestedThemeVariant = variant;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            window.GetVisualDescendants().OfType<WrapPanel>().Single(p => p.Name == "SelectionToolbar").IsVisible.Should().BeTrue();
            WriteableBitmap? frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            string path = Path.Combine(directory, $"diff-hunks-{name}.png");
            frame!.Save(path);
            TestContext.AddTestAttachment(path);
        }

        // The discard confirmation bar.
        diff.DiscardSelectedCommand.Execute(null);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        string confirmPath = Path.Combine(directory, "diff-hunks-confirm-dark.png");
        window.CaptureRenderedFrame()!.Save(confirmPath);
        diff.CancelDiscardCommand.Execute(null);
        window.Close();
    }
}
