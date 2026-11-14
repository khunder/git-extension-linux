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
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;
using GitExtensions.Extensibility.Git;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class FileHistoryWindowTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; private set; }

        public Task SetTextAsync(string text)
        {
            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingWindowService : IWindowService, IFileHistoryWindowService
    {
        public List<(string Path, string? Revision, FileHistoryTab Tab)> Calls { get; } = [];

        public void ShowCommitWindow()
        {
        }

        public void ShowFileHistoryWindow(string path, string? revision = null) => Calls.Add((path, revision, FileHistoryTab.Diff));

        public void ShowFileHistoryWindow(string path, string? revision, FileHistoryTab tab) => Calls.Add((path, revision, tab));
    }

    private static FileHistoryWindowViewModel CreateViewModel(FileHistoryRepository repository, string path = "tale.txt")
    {
        GitModule module = GitEnvironment.Initialize().OpenRepository(repository.Path)!;
        return new FileHistoryWindowViewModel(() => module, path, repository.Path);
    }

    private static void Pump()
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 400 && !condition(); i++)
        {
            Pump();
            await Task.Delay(25);
        }

        Pump();
        condition().Should().BeTrue("the condition should become true in time");
    }

    private static void Press(Window window, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        window.KeyPress(key, (RawInputModifiers)(int)modifiers, PhysicalKey.None, null);
        if (window.IsVisible)
        {
            window.KeyRelease(key, (RawInputModifiers)(int)modifiers, PhysicalKey.None, null);
        }

        Pump();
    }

    private static async Task<(WindowService Service, Window Owner, MainWindowViewModel Main, string StatePath)> CreateServiceAsync(FileHistoryRepository repository)
    {
        string statePath = Path.Combine(Directory.CreateTempSubdirectory("ge-avalonia-fhstate-").FullName, "state.json");
        WindowService.FileHistoryStatePath = statePath;
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        Window owner = new();
        owner.Show();
        await main.OpenAsync(repository.Path);
        return (new WindowService(main, owner), owner, main, statePath);
    }

    [Test]
    public async Task History_lists_the_commits_newest_first_and_follows_renames()
    {
        using FileHistoryRepository repository = new();
        FileHistoryWindowViewModel viewModel = CreateViewModel(repository);

        await viewModel.OpenAsync(FileHistoryTab.Diff);

        viewModel.Rows.Select(r => r.Subject).Should().Equal("Edit first line", "Rename story to tale", "Append ending", "Bob edits middle", "Add story");
        viewModel.Rows.Select(r => r.Entry.Path).Should().Equal("tale.txt", "tale.txt", "story.txt", "story.txt", "story.txt");
        viewModel.Rows[2].RenamedFrom.Should().Be("story.txt");
        viewModel.Rows[0].HasRenamedFrom.Should().BeFalse();
        viewModel.SelectedRow.Should().BeSameAs(viewModel.Rows[0]);
        viewModel.Title.Should().Be($"File History - tale.txt - {repository.Path}");
        viewModel.BranchOptions.Select(o => o.Display).Should().Equal("All branches", "main");
        viewModel.SelectedBranch!.Display.Should().Be("main");
        viewModel.Columns.AuthorWidth.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task Following_renames_can_be_switched_off()
    {
        using FileHistoryRepository repository = new();
        FileHistoryWindowViewModel viewModel = CreateViewModel(repository);
        await viewModel.OpenAsync(FileHistoryTab.Diff);

        viewModel.FollowRenames = false;
        await WaitUntilAsync(() => viewModel.Rows.Count == 2);
        await viewModel.WhenIdleAsync();

        viewModel.Rows.Select(r => r.Subject).Should().Equal("Edit first line", "Rename story to tale");
    }

    [Test]
    public async Task Filter_matches_message_author_and_hash_and_honours_the_options()
    {
        using FileHistoryRepository repository = new();
        FileHistoryWindowViewModel viewModel = CreateViewModel(repository);
        await viewModel.OpenAsync(FileHistoryTab.Diff);

        viewModel.FilterText = "append";
        viewModel.Rows.Select(r => r.Subject).Should().Equal("Append ending");
        viewModel.SelectedRow!.Subject.Should().Be("Append ending");

        viewModel.FilterText = "bob";
        viewModel.Rows.Select(r => r.Subject).Should().Equal("Edit first line", "Bob edits middle");

        viewModel.FilterByAuthor = false;
        viewModel.Rows.Select(r => r.Subject).Should().Equal("Bob edits middle");

        viewModel.FilterByAuthor = true;
        viewModel.FilterText = repository.HashOf("Add story")[..8];
        viewModel.Rows.Select(r => r.Subject).Should().Equal("Add story");

        viewModel.FilterText = "no such thing";
        viewModel.Rows.Should().BeEmpty();
        viewModel.Message.Should().Contain("filter");

        viewModel.FilterText = string.Empty;
        viewModel.Rows.Should().HaveCount(5);
        await viewModel.WhenIdleAsync();
    }

    [Test]
    public async Task Branch_combo_switches_between_the_current_and_all_branches()
    {
        using FileHistoryRepository repository = new();
        string main = repository.Head;
        repository.GitOutput("checkout", "--quiet", "-b", "side");
        File.AppendAllText(Path.Combine(repository.Path, "tale.txt"), "side line\n");
        repository.GitOutput("-c", "user.name=Carol", "-c", "user.email=c@example.com", "commit", "--quiet", "-am", "Side edit");
        repository.GitOutput("checkout", "--quiet", "main");
        main.Should().Be(repository.Head);

        FileHistoryWindowViewModel viewModel = CreateViewModel(repository);
        await viewModel.OpenAsync(FileHistoryTab.Diff);

        viewModel.Rows.Select(r => r.Subject).Should().NotContain("Side edit");
        viewModel.BranchOptions.Select(o => o.Display).Should().Equal("All branches", "main", "side");

        viewModel.SelectedBranch = viewModel.BranchOptions.Single(o => o.Display == "All branches");
        await WaitUntilAsync(() => viewModel.Rows.Any(r => r.Subject == "Side edit"));
        await viewModel.WhenIdleAsync();
        viewModel.Rows[0].Subject.Should().Be("Side edit");
        viewModel.Rows[0].Refs.Should().Contain(r => r.Name == "side" && r.IsBranch);

        viewModel.SelectedBranch = viewModel.BranchOptions.Single(o => o.Display == "side");
        await WaitUntilAsync(() => viewModel.Rows.Count == 6 && viewModel.SelectedBranch.Display == "side");
        await viewModel.WhenIdleAsync();
        viewModel.Rows[0].Subject.Should().Be("Side edit");
    }

    [Test]
    public async Task A_revision_that_is_not_on_the_current_branch_falls_back_to_all_branches()
    {
        using FileHistoryRepository repository = new();
        repository.GitOutput("checkout", "--quiet", "-b", "side");
        File.AppendAllText(Path.Combine(repository.Path, "tale.txt"), "side line\n");
        repository.GitOutput("-c", "user.name=Carol", "-c", "user.email=c@example.com", "commit", "--quiet", "-am", "Side edit");
        string side = repository.Head;
        repository.GitOutput("checkout", "--quiet", "main");

        FileHistoryWindowViewModel viewModel = CreateViewModel(repository);
        await viewModel.OpenAsync(FileHistoryTab.Blame, side);

        viewModel.SelectedBranch!.Display.Should().Be("All branches");
        viewModel.SelectedRow!.Hash.Should().Be(side);
        viewModel.SelectedTabIndex.Should().Be((int)FileHistoryTab.Blame);
        viewModel.Blame.Lines.Last().Text.Should().Be("side line");
    }

    [Test]
    public async Task Diff_and_View_show_the_file_as_of_the_selected_revision()
    {
        using FileHistoryRepository repository = new();
        FileHistoryWindowViewModel viewModel = CreateViewModel(repository);
        await viewModel.OpenAsync(FileHistoryTab.Diff);

        viewModel.SelectedRow = viewModel.Rows.Single(r => r.Subject == "Append ending");
        viewModel.SelectedTabIndex = (int)FileHistoryTab.View;
        await viewModel.WhenIdleAsync();

        viewModel.DiffLines.Where(l => l.Kind == DiffLineKind.Added).Select(l => l.Text).Should().Contain(["+line 6", "+line 7"]);
        viewModel.ViewLines.Select(l => l.Text).Should().Equal("line 1", "line two (bob)", "line three (bob)", "line 4", "line 5", "line 6", "line 7");
        viewModel.ViewLines[6].Number.Should().Be(7);
        viewModel.ViewMessage.Should().BeEmpty();

        // An older revision under the file's old name.
        viewModel.SelectedRow = viewModel.Rows.Single(r => r.Subject == "Add story");
        await viewModel.WhenIdleAsync();
        viewModel.ViewLines.Select(l => l.Text).Should().Equal("line 1", "line 2", "line 3", "line 4", "line 5");

        // The newest one under the new name.
        viewModel.SelectedRow = viewModel.Rows[0];
        await viewModel.WhenIdleAsync();
        viewModel.ViewLines[0].Text.Should().Be("first (bob)");
        viewModel.DiffLines.Should().Contain(l => l.Kind == DiffLineKind.Removed && l.Text.Contains("line 1", StringComparison.Ordinal));
    }

    [Test]
    public async Task Rename_commit_diff_names_both_paths_and_binary_or_empty_files_are_guarded()
    {
        using FileHistoryRepository repository = new();
        FileHistoryWindowViewModel viewModel = CreateViewModel(repository);
        await viewModel.OpenAsync(FileHistoryTab.Diff);

        viewModel.SelectedRow = viewModel.Rows.Single(r => r.Subject == "Rename story to tale");
        await viewModel.WhenIdleAsync();
        string.Join("\n", viewModel.DiffLines.Select(l => l.Text)).Should().Contain("rename from story.txt");

        FileHistoryWindowViewModel binary = CreateViewModel(repository, "blob.bin");
        await binary.OpenAsync(FileHistoryTab.View);
        binary.ViewLines.Should().BeEmpty();
        binary.ViewMessage.Should().Contain("Binary");

        FileHistoryWindowViewModel empty = CreateViewModel(repository, "empty.txt");
        await empty.OpenAsync(FileHistoryTab.View);
        empty.ViewMessage.Should().Contain("empty");

        FileHistoryWindowViewModel untracked = CreateViewModel(repository, "untracked.txt");
        await untracked.OpenAsync(FileHistoryTab.Diff);
        untracked.Rows.Should().BeEmpty();
        untracked.Message.Should().Contain("No commits");
    }

    [Test]
    public async Task Commit_tab_shows_the_selected_commit()
    {
        using FileHistoryRepository repository = new();
        FileHistoryWindowViewModel viewModel = CreateViewModel(repository);
        await viewModel.OpenAsync(FileHistoryTab.Commit);

        viewModel.SelectedTabIndex.Should().Be((int)FileHistoryTab.Commit);
        viewModel.CommitDetails.Hash.Should().Be(repository.HashOf("Edit first line"));
        viewModel.CommitDetails.Message.Should().Be("Edit first line");

        viewModel.SelectedRow = viewModel.Rows.Single(r => r.Subject == "Add story");
        await viewModel.WhenIdleAsync();
        viewModel.CommitDetails.Hash.Should().Be(repository.HashOf("Add story"));
        viewModel.CommitDetails.Author.Should().Contain("Alice");
    }

    [Test]
    public async Task Blame_groups_lines_into_blocks_and_clicking_a_block_selects_its_commit_without_reloading()
    {
        using FileHistoryRepository repository = new();
        FileHistoryWindowViewModel viewModel = CreateViewModel(repository);
        await viewModel.OpenAsync(FileHistoryTab.Blame);

        viewModel.SelectedTabIndex.Should().Be((int)FileHistoryTab.Blame);
        IReadOnlyList<BlameLineViewModel> lines = viewModel.Blame.Lines;
        lines.Should().HaveCount(7);
        lines[0].Annotation.Should().StartWith("Bob - ");
        lines[1].IsBlockStart.Should().BeTrue();
        lines[2].Annotation.Should().BeEmpty();
        lines[3].Annotation.Should().StartWith("Alice - ");
        lines.Select(l => l.IsAlternate).Distinct().Should().HaveCount(2);

        BlameLineViewModel middle = lines[1];
        viewModel.Blame.SelectedLine = middle;
        await viewModel.WhenIdleAsync();

        viewModel.SelectedRow!.Subject.Should().Be("Bob edits middle");
        viewModel.CommitDetails.Hash.Should().Be(repository.HashOf("Bob edits middle"));
        viewModel.Blame.Lines.Should().BeSameAs(lines, "selecting a block must not replace the shown blame");

        // Picking a commit in the grid blames at that revision.
        viewModel.SelectedRow = viewModel.Rows.Single(r => r.Subject == "Add story");
        await viewModel.WhenIdleAsync();
        viewModel.Blame.Lines.Should().HaveCount(5);
        viewModel.Blame.Lines.Select(l => l.Entry.Author).Distinct().Should().Equal("Alice");
    }

    [Test]
    public async Task Blame_starts_at_the_requested_revision()
    {
        using FileHistoryRepository repository = new();
        FileHistoryWindowViewModel viewModel = CreateViewModel(repository);

        await viewModel.OpenAsync(FileHistoryTab.Blame, repository.HashOf("Append ending"));

        viewModel.SelectedRow!.Subject.Should().Be("Append ending");
        viewModel.Blame.Lines.Should().HaveCount(7);
        viewModel.Blame.Lines[0].Entry.Author.Should().Be("Alice");
    }

    [Test]
    public async Task Tab_selection_follows_the_open_request()
    {
        using FileHistoryRepository repository = new();
        FileHistoryWindowViewModel diff = CreateViewModel(repository);
        await diff.OpenAsync(FileHistoryTab.Diff);
        diff.SelectedTabIndex.Should().Be(1);
        diff.Blame.Lines.Should().BeEmpty("the blame is only loaded when its tab is shown");

        diff.SelectedTabIndex = (int)FileHistoryTab.Blame;
        await diff.WhenIdleAsync();
        diff.Blame.Lines.Should().HaveCount(7);

        await diff.NavigateAsync(FileHistoryTab.View, repository.HashOf("Add story"));
        diff.SelectedTabIndex.Should().Be((int)FileHistoryTab.View);
        diff.SelectedRow!.Subject.Should().Be("Add story");
        diff.ViewLines.Should().HaveCount(5);
    }

    [Test]
    public void Relative_dates_and_ref_decorations_are_formatted_like_the_original_grid()
    {
        DateTimeOffset now = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        FileHistoryRowViewModel.FormatRelative(now.AddSeconds(-1), now).Should().Be("1 second ago");
        FileHistoryRowViewModel.FormatRelative(now.AddMinutes(-5), now).Should().Be("5 minutes ago");
        FileHistoryRowViewModel.FormatRelative(now.AddHours(-21), now).Should().Be("21 hours ago");
        FileHistoryRowViewModel.FormatRelative(now.AddDays(-10), now).Should().Be("1 week ago");
        FileHistoryRowViewModel.FormatRelative(now.AddDays(-27), now).Should().Be("3 weeks ago");
        FileHistoryRowViewModel.FormatRelative(now.AddDays(-65), now).Should().Be("2 months ago");
        FileHistoryRowViewModel.FormatRelative(now.AddDays(-800), now).Should().Be("2 years ago");
        FileHistoryRowViewModel.GetInitials("Philippe Miossec").Should().Be("PM");
        FileHistoryRowViewModel.GetInitials("russkie").Should().Be("R");

        IReadOnlyList<FileHistoryRef> refs = FileHistoryRef.Parse("HEAD -> refs/heads/main, tag: refs/tags/v1.0, refs/remotes/origin/main, refs/remotes/origin/HEAD, refs/heads/x/y");
        refs.Select(r => (r.Name, r.Kind, r.IsCurrent)).Should().Equal(
            ("main", FileHistoryRefKind.Branch, true),
            ("v1.0", FileHistoryRefKind.Tag, false),
            ("origin/main", FileHistoryRefKind.Remote, false),
            ("x/y", FileHistoryRefKind.Branch, false));
        FileHistoryRef.Parse(string.Empty).Should().BeEmpty();
    }

    [Test]
    public void Opening_a_file_from_the_main_window_uses_the_window_service_when_there_is_one()
    {
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        RecordingWindowService service = new();
        main.WindowService = service;
        ChangedFileViewModel file = new(new GitItemStatus("a.txt"));

        main.ShowFileHistoryCommand.Execute(file);
        main.ShowFileBlameCommand.Execute(file);

        service.Calls.Should().Equal(("a.txt", null, FileHistoryTab.Diff), ("a.txt", null, FileHistoryTab.Blame));
        main.FileHistory.IsOpen.Should().BeFalse("the overlay is only the fallback");
    }

    [AvaloniaTest]
    public async Task WindowService_opens_one_non_modal_window_per_file()
    {
        using FileHistoryRepository repository = new();
        (WindowService service, Window owner, MainWindowViewModel main, _) = await CreateServiceAsync(repository);

        service.ShowFileHistoryWindow("tale.txt");
        FileHistoryWindow first = service.GetFileHistoryWindow("tale.txt")!;
        first.Should().NotBeNull();
        await WaitUntilAsync(() => first.ViewModel.Rows.Count == 5);

        first.Title.Should().Be($"File History - tale.txt - {main.RepositoryPath}");
        first.ViewModel.SelectedTabIndex.Should().Be((int)FileHistoryTab.Diff);
        first.Owner.Should().BeSameAs(owner);

        // The same file again activates the window, a revision selects that commit and a tab switches to it.
        service.ShowFileHistoryWindow("tale.txt", repository.HashOf("Add story"));
        await WaitUntilAsync(() => first.ViewModel.SelectedRow?.Subject == "Add story");
        service.GetFileHistoryWindow("tale.txt").Should().BeSameAs(first);

        ((IFileHistoryWindowService)service).ShowFileHistoryWindow("tale.txt", null, FileHistoryTab.Blame);
        first.ViewModel.SelectedTabIndex.Should().Be((int)FileHistoryTab.Blame);

        // Another file gets its own window, both stay open.
        service.ShowFileHistoryWindow("empty.txt");
        FileHistoryWindow second = service.GetFileHistoryWindow("empty.txt")!;
        second.Should().NotBeSameAs(first);
        first.IsVisible.Should().BeTrue();

        // Backslashes name the same file.
        service.ShowFileHistoryWindow("empty.txt");
        service.GetFileHistoryWindow("empty.txt").Should().BeSameAs(second);

        first.Close();
        service.GetFileHistoryWindow("tale.txt").Should().BeNull();
        service.ShowFileHistoryWindow("tale.txt");
        service.GetFileHistoryWindow("tale.txt").Should().NotBeSameAs(first);

        service.GetFileHistoryWindow("tale.txt")!.Close();
        second.Close();
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Window_keyboard_shortcuts_work()
    {
        using FileHistoryRepository repository = new();
        (WindowService service, Window owner, _, _) = await CreateServiceAsync(repository);
        service.ShowFileHistoryWindow("tale.txt");
        FileHistoryWindow window = service.GetFileHistoryWindow("tale.txt")!;
        FakeClipboard clipboard = new();
        window.ViewModel.Clipboard = clipboard;
        await WaitUntilAsync(() => window.ViewModel.Rows.Count == 5);

        // Ctrl+C copies the selected hash.
        window.FindControl<ListBox>("RevisionList")!.Focus();
        Press(window, Key.C, KeyModifiers.Control);
        await WaitUntilAsync(() => clipboard.Text is not null);
        clipboard.Text.Should().Be(repository.HashOf("Edit first line"));

        // Ctrl+F focuses the filter.
        Press(window, Key.F, KeyModifiers.Control);
        window.FindControl<TextBox>("FilterBox")!.IsFocused.Should().BeTrue();

        // F5 reloads the history (new row objects) and keeps the selection.
        window.ViewModel.SelectedRow = window.ViewModel.Rows[2];
        await window.ViewModel.WhenIdleAsync();
        FileHistoryRowViewModel before = window.ViewModel.Rows[2];
        window.FindControl<ListBox>("RevisionList")!.Focus();
        Press(window, Key.F5);
        await WaitUntilAsync(() => window.ViewModel.Rows.Count == 5 && !ReferenceEquals(window.ViewModel.Rows[2], before));
        window.ViewModel.SelectedRow!.Subject.Should().Be("Append ending");

        // Escape closes the window.
        Press(window, Key.Escape);
        service.GetFileHistoryWindow("tale.txt").Should().BeNull();
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Window_size_is_remembered()
    {
        using FileHistoryRepository repository = new();
        (WindowService service, Window owner, _, string statePath) = await CreateServiceAsync(repository);
        service.ShowFileHistoryWindow("tale.txt");
        FileHistoryWindow window = service.GetFileHistoryWindow("tale.txt")!;
        window.Width = 900;
        window.Height = 600;
        Pump();
        window.Close();

        File.Exists(statePath).Should().BeTrue();
        service.ShowFileHistoryWindow("tale.txt");
        FileHistoryWindow reopened = service.GetFileHistoryWindow("tale.txt")!;
        reopened.Width.Should().BeApproximately(900, 20);
        reopened.Height.Should().BeApproximately(600, 40);
        reopened.Close();
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Blame_panes_scroll_together_and_clicking_a_block_selects_the_commit()
    {
        using FileHistoryRepository repository = new();
        string big = string.Join('\n', Enumerable.Range(1, 400).Select(i => "row " + i)) + "\n";
        File.WriteAllText(Path.Combine(repository.Path, "big.txt"), big);
        repository.GitOutput("add", "big.txt");
        repository.GitOutput("-c", "user.name=Dave", "-c", "user.email=d@example.com", "commit", "--quiet", "-m", "Add big");

        (WindowService service, Window owner, _, _) = await CreateServiceAsync(repository);
        ((IFileHistoryWindowService)service).ShowFileHistoryWindow("big.txt", null, FileHistoryTab.Blame);
        FileHistoryWindow window = service.GetFileHistoryWindow("big.txt")!;
        window.Width = 1000;
        window.Height = 700;
        await WaitUntilAsync(() => window.ViewModel.Blame.Lines.Count == 400);
        Pump();

        ItemsControl text = window.FindControl<ItemsControl>("BlameText")!;
        ItemsControl annotations = window.FindControl<ItemsControl>("BlameAnnotations")!;
        ScrollViewer right = text.GetVisualDescendants().OfType<ScrollViewer>().First();
        ScrollViewer left = annotations.GetVisualDescendants().OfType<ScrollViewer>().First();
        right.Extent.Height.Should().BeGreaterThan(right.Viewport.Height);

        right.Offset = new Vector(0, 900);
        Pump();
        left.Offset.Y.Should().BeApproximately(900, 1);

        left.Offset = new Vector(0, 300);
        Pump();
        right.Offset.Y.Should().BeApproximately(300, 1);

        // Only the visible rows are realised.
        text.GetVisualDescendants().OfType<TextBlock>().Count().Should().BeLessThan(400);

        // Clicking a block selects its commit.
        BlameLineViewModel line = window.ViewModel.Blame.Lines[0];
        window.ViewModel.Blame.SelectedLine = null;
        window.ViewModel.Blame.SelectedLine = line;
        await window.ViewModel.WhenIdleAsync();
        window.ViewModel.SelectedRow!.Subject.Should().Be("Add big");

        window.Close();
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Window_renders_history_and_blame_in_both_themes()
    {
        using FileHistoryRepository repository = new();
        repository.GitOutput("tag", "v1.0", repository.HashOf("Append ending"));
        repository.GitOutput("branch", "feature/show-it", repository.HashOf("Bob edits middle"));
        (WindowService service, Window owner, _, _) = await CreateServiceAsync(repository);
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);

        foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Application.Current!.RequestedThemeVariant = theme;
            string name = theme == ThemeVariant.Light ? "light" : "dark";

            service.ShowFileHistoryWindow("tale.txt");
            FileHistoryWindow window = service.GetFileHistoryWindow("tale.txt")!;
            window.Width = 1200;
            window.Height = 800;
            await WaitUntilAsync(() => window.ViewModel.Rows.Count == 5 && window.ViewModel.DiffLines.Count > 0);
            window.ViewModel.SelectedRow = window.ViewModel.Rows[2];
            await window.ViewModel.WhenIdleAsync();
            Pump();
            Save(window, Path.Combine(directory, $"file-history-diff-{name}.png"));

            window.ViewModel.SelectedTabIndex = (int)FileHistoryTab.Blame;
            await window.ViewModel.WhenIdleAsync();
            Pump();
            window.ViewModel.Blame.Lines.Should().NotBeEmpty();
            Save(window, Path.Combine(directory, $"file-history-blame-{name}.png"));

            window.ViewModel.SelectedTabIndex = (int)FileHistoryTab.View;
            await window.ViewModel.WhenIdleAsync();
            Pump();
            Save(window, Path.Combine(directory, $"file-history-view-{name}.png"));

            window.ViewModel.SelectedTabIndex = (int)FileHistoryTab.Commit;
            Pump();
            Save(window, Path.Combine(directory, $"file-history-commit-{name}.png"));
            window.Close();
        }

        Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        owner.Close();
    }

    private static void Save(Window window, string file)
    {
        WriteableBitmap? frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();
        frame!.Save(file);
        TestContext.AddTestAttachment(file);
    }
}
