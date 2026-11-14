using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GitCommands;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.FileHistory;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;
using GitExtensions.Extensibility.Git;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class FileHistoryTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 400 && !condition(); i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        condition().Should().BeTrue("the condition should become true in time");
    }

    private static FileHistoryViewModel CreateViewModel(FileHistoryRepository repository, Action<ObjectId>? selectCommit = null)
    {
        GitModule module = GitEnvironment.Initialize().OpenRepository(repository.Path)!;
        return new FileHistoryViewModel(() => module, selectCommit);
    }

    [AvaloniaTest]
    public void BlameParser_reads_porcelain_output()
    {
        const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        string porcelain =
            $"{A} 1 1 2\nauthor Alice\nauthor-mail <a@x>\nauthor-time 1700000000\nauthor-tz +0100\nsummary First\nfilename f.txt\n\tone\n" +
            $"{A} 2 2\n\ttwo\r\n" +
            $"{B} 1 3 1\nauthor Bob\nauthor-time 1700000100\nauthor-tz -0500\nsummary Second\nfilename f.txt\n\t\tindented\n";

        IReadOnlyList<BlameEntry> entries = BlameParser.Parse(porcelain);

        entries.Select(e => (e.LineNumber, e.CommitHash, e.Author, e.Text)).Should().Equal((1, A, "Alice", "one"), (2, A, "Alice", "two"), (3, B, "Bob", "\tindented"));
        entries[0].Summary.Should().Be("First");
        entries[0].Date.Offset.Should().Be(TimeSpan.FromHours(1));
        entries[2].Date.Offset.Should().Be(TimeSpan.FromHours(-5));
        entries[2].Date.ToUnixTimeSeconds().Should().Be(1700000100);
        BlameParser.Parse(string.Empty).Should().BeEmpty();
    }

    [AvaloniaTest]
    public void FileLogParser_reads_records_and_the_path_at_each_commit()
    {
        string output = "\u0001h1\u001fAlice\u001f2024-01-02T03:04:05+01:00\u001fSubject: with colon\n\nnew.txt\n\u0001h2\u001fBob\u001f2024-01-01T00:00:00+00:00\u001fOld\n\nold.txt\n";

        IReadOnlyList<FileHistoryEntry> entries = FileLogParser.Parse(output);

        entries.Select(e => (e.Hash, e.Author, e.Subject, e.Path)).Should().Equal(("h1", "Alice", "Subject: with colon", "new.txt"), ("h2", "Bob", "Old", "old.txt"));
        FileLogParser.Parse(null).Should().BeEmpty();
    }

    [AvaloniaTest]
    public void BlameViewModel_groups_consecutive_lines_of_one_commit()
    {
        DateTimeOffset date = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        BlameEntry Line(int number, string hash) => new(number, hash.PadRight(40, '0'), "A" + hash, date, "s", "t" + number);

        IReadOnlyList<BlameLineViewModel> rows = BlameViewModel.CreateRows([Line(1, "a"), Line(2, "a"), Line(3, "b"), Line(4, "a")]);

        rows.Select(r => r.IsBlockStart).Should().Equal(true, false, true, true);
        rows.Select(r => r.Author).Should().Equal("Aa", string.Empty, "Ab", "Aa");
        rows.Select(r => r.IsAlternate).Should().Equal(false, false, true, false);
    }

    [AvaloniaTest]
    public async Task History_lists_commits_newest_first_and_follows_the_rename()
    {
        using FileHistoryRepository repository = new();
        FileHistoryViewModel history = CreateViewModel(repository);

        await history.OpenAsync("tale.txt", blame: false);

        history.IsOpen.Should().BeTrue();
        history.Message.Should().BeEmpty();
        history.Entries.Select(e => e.Subject).Should().Equal("Edit first line", "Rename story to tale", "Append ending", "Bob edits middle", "Add story");
        history.Entries.Select(e => e.Path).Should().Equal("tale.txt", "tale.txt", "story.txt", "story.txt", "story.txt");
        history.Entries.Select(e => e.Author).Should().Equal("Bob", "Alice", "Alice", "Bob", "Alice");
        history.Entries[0].Hash.Should().Be(repository.Head.Length == 40 ? repository.HashOf("Edit first line") : string.Empty);
        history.Entries[0].ShortHash.Should().HaveLength(8);
        history.SelectedEntry.Should().BeSameAs(history.Entries[0]);
    }

    [AvaloniaTest]
    public async Task Selecting_a_commit_shows_only_that_files_diff()
    {
        using FileHistoryRepository repository = new();
        FileHistoryViewModel history = CreateViewModel(repository);
        await history.OpenAsync("tale.txt", blame: false);

        history.SelectedEntry = history.Entries.Single(e => e.Subject == "Bob edits middle");
        await WaitUntilAsync(() => history.DiffLines.Any(l => l.Kind == DiffLineKind.Added));

        history.DiffLines.Where(l => l.Kind == DiffLineKind.Added).Select(l => l.Text).Should().Equal("+line two (bob)", "+line three (bob)");
        history.DiffLines.Where(l => l.Kind == DiffLineKind.Removed).Select(l => l.Text).Should().Equal("-line 2", "-line 3");
        history.DiffLines.Should().NotContain(l => l.Text.Contains("empty.txt"));

        history.SelectedEntry = history.Entries.Single(e => e.Subject == "Rename story to tale");
        await WaitUntilAsync(() => history.DiffLines.Any(l => l.Text.Contains("rename")));
        history.DiffLines.Select(l => l.Text).Should().Contain("rename from story.txt").And.Contain("rename to tale.txt");

        // The root commit has no parent.
        history.SelectedEntry = history.Entries.Single(e => e.Subject == "Add story");
        await WaitUntilAsync(() => history.DiffLines.Count(l => l.Kind == DiffLineKind.Added) == 5);
    }

    [AvaloniaTest]
    public async Task Blame_attributes_each_line_to_its_author()
    {
        using FileHistoryRepository repository = new();
        FileHistoryViewModel history = CreateViewModel(repository);

        await history.OpenAsync("tale.txt", blame: true);

        history.ShowBlame.Should().BeTrue();
        BlameViewModel blame = history.Blame;
        blame.Message.Should().BeEmpty();
        blame.Lines.Select(l => l.Entry.Author).Should().Equal("Bob", "Bob", "Bob", "Alice", "Alice", "Alice", "Alice");
        blame.Lines.Select(l => l.Entry.Summary).Should().Equal("Edit first line", "Bob edits middle", "Bob edits middle", "Add story", "Add story", "Append ending", "Append ending");
        blame.Lines.Select(l => l.Text).Should().Equal("first (bob)", "line two (bob)", "line three (bob)", "line 4", "line 5", "line 6", "line 7");
        blame.Lines.Select(l => l.IsBlockStart).Should().Equal(true, true, false, true, false, true, false);
        blame.Lines.Select(l => l.Author).Should().Equal("Bob", "Bob", string.Empty, "Alice", string.Empty, "Alice", string.Empty);
        blame.Lines.Select(l => l.IsAlternate).Should().Equal(false, true, true, false, false, true, true);
    }

    [AvaloniaTest]
    public async Task Selecting_an_older_commit_blames_the_file_as_of_that_commit()
    {
        using FileHistoryRepository repository = new();
        FileHistoryViewModel history = CreateViewModel(repository);
        await history.OpenAsync("tale.txt", blame: true);

        history.SelectedEntry = history.Entries.Single(e => e.Subject == "Bob edits middle");
        await WaitUntilAsync(() => history.Blame.Lines.Count == 5);

        history.Blame.Lines.Select(l => l.Entry.Author).Should().Equal("Alice", "Bob", "Bob", "Alice", "Alice");
    }

    [AvaloniaTest]
    public async Task Clicking_a_blame_row_reports_its_commit()
    {
        using FileHistoryRepository repository = new();
        List<ObjectId> selected = [];
        FileHistoryViewModel history = CreateViewModel(repository, selected.Add);
        await history.OpenAsync("tale.txt", blame: true);

        history.Blame.SelectedLine = history.Blame.Lines[3];

        selected.Should().Equal(ObjectId.Parse(repository.HashOf("Add story")));
    }

    [AvaloniaTest]
    public async Task Untracked_file_has_no_history_and_no_blame()
    {
        using FileHistoryRepository repository = new();
        FileHistoryViewModel history = CreateViewModel(repository);

        await history.OpenAsync("untracked.txt", blame: true);

        history.Entries.Should().BeEmpty();
        history.Message.Should().Be("No commits touch this file.");
        history.IsLoading.Should().BeFalse();
        history.Blame.Lines.Should().BeEmpty();
        history.Blame.Message.Should().Contain("not tracked");
    }

    [AvaloniaTest]
    public async Task Empty_and_binary_files_are_handled()
    {
        using FileHistoryRepository repository = new();
        FileHistoryViewModel history = CreateViewModel(repository);

        await history.OpenAsync("empty.txt", blame: true);
        history.Blame.Lines.Should().BeEmpty();
        history.Blame.Message.Should().Contain("empty");

        await history.OpenAsync("blob.bin", blame: true);
        history.Blame.Lines.Should().BeEmpty();
        history.Blame.Message.Should().Be("Binary file: no blame shown.");

        history.ShowBlame = false;
        await WaitUntilAsync(() => history.DiffLines.Any(l => l.Text.Contains("Binary")));
    }

    [AvaloniaTest]
    public async Task Opening_another_file_supersedes_the_previous_load()
    {
        using FileHistoryRepository repository = new();
        FileHistoryViewModel history = CreateViewModel(repository);

        Task first = history.OpenAsync("tale.txt", blame: true);
        await history.OpenAsync("empty.txt", blame: false);
        await first;
        await WaitUntilAsync(() => !history.IsLoading);

        history.FilePath.Should().Be("empty.txt");
        history.Entries.Select(e => e.Subject).Should().Equal("Add empty and binary files");
    }

    [AvaloniaTest]
    public async Task Main_window_menus_open_the_overlay_and_clicking_blame_selects_the_commit()
    {
        using FileHistoryRepository repository = new();
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = main };
        window.Show();
        await main.OpenAsync(repository.Path);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        FileHistoryView overlay = window.FindControl<FileHistoryView>("FileHistoryOverlay")!;
        overlay.IsVisible.Should().BeFalse();

        // The "Changes" list is gone from the main window (untracked.txt is the only working tree change); its menu commands remain.
        main.ChangedFiles.Count.Should().Be(1);
        main.ShowFileHistoryCommand.CanExecute(main.ChangedFiles[0]).Should().BeTrue();
        main.ShowFileBlameCommand.CanExecute(main.ChangedFiles[0]).Should().BeTrue();

        // The same commands drive the overlay.
        ChangedFileViewModel tale = new(new GitItemStatus("tale.txt"));
        main.ShowFileHistoryCommand.Execute(tale);
        await WaitUntilAsync(() => main.FileHistory.Entries.Count == 5);
        overlay.IsVisible.Should().BeTrue();
        main.FileHistory.ShowBlame.Should().BeFalse();

        main.ShowFileBlameCommand.Execute(tale);
        await WaitUntilAsync(() => main.FileHistory.Blame.Lines.Count == 7);
        main.FileHistory.ShowBlame.Should().BeTrue();

        await WaitUntilAsync(() => main.Commits.Count == 6);
        main.FileHistory.Blame.SelectedLine = main.FileHistory.Blame.Lines[3];
        main.SelectedCommit!.Revision.Subject.Should().Be("Add story");

        main.FileHistory.CloseCommand.Execute(null);
        await WaitUntilAsync(() => !overlay.IsVisible);
        window.Close();
    }

    [AvaloniaTest]
    public async Task Commit_details_menu_and_Escape_close()
    {
        using FileHistoryRepository repository = new();
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = main };
        window.Show();
        await main.OpenAsync(repository.Path);
        main.SelectedCommit = main.Commits.Single(c => c.Revision.Subject == "Edit first line");
        main.RevisionTabs.SelectedTabIndex = 1; // the Diff tab lists the files of the selected commit
        await WaitUntilAsync(() => main.RevisionTabs.Diff.Files.Count == 1);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        ListBox files = window.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "FileList");
        Grid row = files.GetVisualDescendants().OfType<Grid>().First(g => g.ContextMenu is not null);
        row.ContextMenu!.Open(row);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        row.ContextMenu.Items.OfType<MenuItem>().Select(i => i.Header?.ToString()).Should().Contain(["File history", "Blame", "Copy path"]);
        row.ContextMenu.Close();

        // Blame from a commit's file list starts at that commit.
        main.ShowFileBlameCommand.Execute(main.RevisionTabs.Diff.Files[0]);
        await WaitUntilAsync(() => main.FileHistory.Blame.Lines.Count == 7);
        main.FileHistory.SelectedEntry!.Subject.Should().Be("Edit first line");

        FileHistoryView overlay = window.FindControl<FileHistoryView>("FileHistoryOverlay")!;
        overlay.Focus();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await WaitUntilAsync(() => !main.FileHistory.IsOpen);
        window.Close();
    }

    [AvaloniaTest]
    [TestCase("Light", "history")]
    [TestCase("Dark", "history")]
    [TestCase("Light", "blame")]
    [TestCase("Dark", "blame")]
    public async Task Overlay_renders_in_both_themes(string theme, string mode)
    {
        using FileHistoryRepository repository = new();
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = main, Width = 1200, Height = 720, RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        await main.OpenAsync(repository.Path);

        ChangedFileViewModel tale = new(new GitItemStatus("tale.txt"));
        if (mode == "history")
        {
            await main.FileHistory.OpenAsync(tale.Name, blame: false);
            main.FileHistory.SelectedEntry = main.FileHistory.Entries.Single(e => e.Subject == "Bob edits middle");
            await WaitUntilAsync(() => main.FileHistory.DiffLines.Any(l => l.Kind == DiffLineKind.Added));
        }
        else
        {
            await main.FileHistory.OpenAsync(tale.Name, blame: true);
            await WaitUntilAsync(() => main.FileHistory.Blame.Lines.Count == 7);
        }

        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, $"file-{mode}-{theme.ToLowerInvariant()}.png");
        frame!.Save(file);
        TestContext.AddTestAttachment(file);
        window.Close();
    }
}
