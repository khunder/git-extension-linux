using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

/// <summary>
///  The S (stage), U (unstage) and R (reset) keys of the Commit window and of the Diff tab of the main window, with real keys and real repositories.
/// </summary>
[TestFixture]
public class FileKeyShortcutTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static async Task WaitForAsync(Func<bool> condition, string because = "")
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(25);
            Dispatcher.UIThread.RunJobs();
        }

        condition().Should().BeTrue(because);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(TopLevel window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        Pump();
    }

    private static async Task<(Window Owner, MainWindowViewModel Main, CommitWindow Window, CommitWindowViewModel ViewModel)> ShowAsync(string path)
    {
        CommitWindowStore.Current = new CommitWindowStore(Path.Combine(Path.GetTempPath(), "ge-avalonia-keys-" + Guid.NewGuid().ToString("N") + ".json"));
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        Window owner = new() { Width = 800, Height = 600 };
        owner.Show();
        await main.OpenAsync(path);
        WindowService service = new(main, owner);
        main.WindowService = service;
        main.ShowCommitWindowCommand.Execute(null);
        CommitWindow window = service.CommitWindow!;
        window.Width = 1000;
        window.Height = 700;
        CommitWindowViewModel viewModel = (CommitWindowViewModel)window.DataContext!;
        await WaitForAsync(() => viewModel.Branch is not null && !viewModel.IsBusy);
        Pump();
        return (owner, main, window, viewModel);
    }

    private static async Task SelectAsync(CommitWindow window, CommitWindowViewModel viewModel, string name, bool staged)
    {
        ListBox list = window.FindControl<ListBox>(staged ? "StagedList" : "UnstagedList")!;
        ChangedFileViewModel file = (staged ? viewModel.Staged : viewModel.Unstaged).Single(f => f.Name == name);
        list.SelectedItem = file;
        await WaitForAsync(() => viewModel.Diff.File?.Name == name && viewModel.Diff.File.IsStaged == staged && !viewModel.Diff.IsLoading && viewModel.Diff.Lines.Count > 0);
        Pump();
    }

    private static Control FocusDiff(CommitWindow window)
    {
        ItemsControl lines = window.FindControl<DiffView>("DiffPanel")!.FindControl<ItemsControl>("DiffLines")!;
        lines.Focus().Should().BeTrue();
        return lines;
    }

    private static DiffLine Line(CommitWindowViewModel viewModel, string text) => viewModel.Diff.Lines.Single(l => l.Text == text);

    private static string[] Changes(MultiHunkRepository repo, params string[] arguments)
        => [.. repo.Git([arguments[0], .. arguments[1..].Where(a => a.StartsWith('-')), "-U0", .. arguments[1..].Where(a => !a.StartsWith('-'))]).Split('\n').Where(l => (l.StartsWith('+') || l.StartsWith('-')) && !l.StartsWith("+++", StringComparison.Ordinal) && !l.StartsWith("---", StringComparison.Ordinal))];

    [AvaloniaTest]
    public async Task S_stages_the_selected_lines_of_the_unstaged_diff_and_U_unstages_them()
    {
        using MultiHunkRepository repo = new();
        repo.Write("m.txt", string.Join('\n', Enumerable.Range(1, 30).Select(i => i == 3 ? "changed 3" : i == 4 ? "changed 4" : "line " + i)) + "\n");
        (Window owner, _, CommitWindow window, CommitWindowViewModel viewModel) = await ShowAsync(repo.Path);
        await SelectAsync(window, viewModel, "m.txt", staged: false);
        FocusDiff(window);

        // Select only the first replaced pair (line 3), not line 4.
        viewModel.Diff.SelectLine(Line(viewModel, "-line 3"), ctrl: false, shift: false);
        viewModel.Diff.SelectLine(Line(viewModel, "+changed 3"), ctrl: true, shift: false);
        viewModel.Diff.SelectedLineCount.Should().Be(2);
        Press(window, PhysicalKey.S);

        await WaitForAsync(() => viewModel.Staged.Count == 1);
        Changes(repo, "diff", "--cached").Should().Equal("-line 3", "+changed 3");
        Changes(repo, "diff").Should().Equal("-line 4", "+changed 4");
        repo.Git("show", ":m.txt").Should().Contain("changed 3\n").And.Contain("line 4\n").And.NotContain("changed 4");

        // The file now has staged and unstaged entries; U in the staged diff unstages the selected line only.
        await SelectAsync(window, viewModel, "m.txt", staged: true);
        FocusDiff(window);
        viewModel.Diff.SelectLine(Line(viewModel, "+changed 3"), ctrl: false, shift: false);
        Press(window, PhysicalKey.U);
        await WaitForAsync(() => !repo.Git("diff", "--cached").Contains("changed 3", StringComparison.Ordinal));
        repo.Git("show", ":m.txt").Should().NotContain("changed 3").And.Contain("line 4\n");
        owner.Close();
    }

    [AvaloniaTest]
    public async Task R_resets_the_selected_lines_only_after_confirmation()
    {
        using MultiHunkRepository repo = new();
        (Window owner, _, CommitWindow window, CommitWindowViewModel viewModel) = await ShowAsync(repo.Path);
        await SelectAsync(window, viewModel, "m.txt", staged: false);
        FocusDiff(window);

        viewModel.Diff.SelectLine(Line(viewModel, "+changed 15"), ctrl: false, shift: false);
        Press(window, PhysicalKey.R);
        viewModel.Diff.IsConfirmingDiscard.Should().BeTrue();
        window.FindControl<DiffView>("DiffPanel")!.FindControl<Border>("DiscardConfirmation")!.IsVisible.Should().BeTrue();
        repo.Read("m.txt").Should().Contain("changed 15", "nothing happens before the confirmation");

        viewModel.Diff.ConfirmDiscardCommand.Execute(null);
        await WaitForAsync(() => !repo.Read("m.txt").Contains("changed 15", StringComparison.Ordinal));
        repo.Read("m.txt").Should().Contain("changed 3").And.Contain("changed 27");
        repo.Read("m.txt").Should().NotContain("line 15\n", "only the added line was reset: the removed one is not restored");
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Selected_last_line_of_a_file_without_trailing_newline_is_staged_with_S()
    {
        using MultiHunkRepository repo = new();
        repo.Write("nonl.txt", "a\nb\nc");
        repo.CommitAll("Add nonl.txt");
        repo.Write("nonl.txt", "a\nb\nc\nd\ne");
        (Window owner, _, CommitWindow window, CommitWindowViewModel viewModel) = await ShowAsync(repo.Path);
        await SelectAsync(window, viewModel, "nonl.txt", staged: false);
        FocusDiff(window);

        // Keyboard selection: Down moves to the first changed line (-c), Shift+Down extends over +c and +d.
        Press(window, PhysicalKey.ArrowDown);
        Press(window, PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Press(window, PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        viewModel.Diff.Lines.Where(l => l.IsSelected).Select(l => l.Text).Should().Equal("-c", "+c", "+d");
        Press(window, PhysicalKey.S);
        await WaitForAsync(() => viewModel.Staged.Count == 1);

        repo.Git("show", ":nonl.txt").Should().Be("a\nb\nc\nd\n");
        repo.Git("diff").Should().Contain("+e").And.Contain("\\ No newline at end of file");

        // And the rest, with the last line (no newline) selected on its own.
        await SelectAsync(window, viewModel, "nonl.txt", staged: false);
        FocusDiff(window);
        viewModel.Diff.SelectLine(viewModel.Diff.Lines.Single(l => l.Text == "+e"), ctrl: false, shift: false);
        Press(window, PhysicalKey.S);
        await WaitForAsync(() => repo.Git("diff").Length == 0);
        repo.Git("show", ":nonl.txt").Should().Be("a\nb\nc\nd\ne");
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Shift_arrows_extend_the_selection_and_whole_rows_are_highlighted()
    {
        using MultiHunkRepository repo = new();
        (Window owner, _, CommitWindow window, CommitWindowViewModel viewModel) = await ShowAsync(repo.Path);
        await SelectAsync(window, viewModel, "m.txt", staged: false);
        FocusDiff(window);

        Press(window, PhysicalKey.ArrowDown);
        Press(window, PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        Press(window, PhysicalKey.ArrowDown, RawInputModifiers.Shift);
        viewModel.Diff.Lines.Where(l => l.IsSelected).Select(l => l.Text).Should().Equal("-line 3", "+changed 3", "-line 15");
        Press(window, PhysicalKey.ArrowUp, RawInputModifiers.Shift);
        viewModel.Diff.SelectedLineCount.Should().Be(2);
        Press(window, PhysicalKey.A, RawInputModifiers.Control);
        viewModel.Diff.SelectedLineCount.Should().Be(6);
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Dragging_from_one_line_to_another_selects_the_changed_lines_between()
    {
        using MultiHunkRepository repo = new();
        (Window owner, _, CommitWindow window, CommitWindowViewModel viewModel) = await ShowAsync(repo.Path);
        await SelectAsync(window, viewModel, "m.txt", staged: false);

        viewModel.Diff.SelectLine(Line(viewModel, "+changed 3"), ctrl: false, shift: false);
        viewModel.Diff.ExtendSelectionTo(Line(viewModel, "-line 15"));
        viewModel.Diff.Lines.Where(l => l.IsSelected).Select(l => l.Text).Should().Equal("+changed 3", "-line 15");
        viewModel.Diff.ExtendSelectionTo(Line(viewModel, "-line 3"));
        viewModel.Diff.Lines.Where(l => l.IsSelected).Select(l => l.Text).Should().Equal("-line 3", "+changed 3");
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Global_keys_act_on_the_selected_file_but_not_while_typing()
    {
        using MultiHunkRepository repo = new();
        repo.Write("n.txt", "new\n");
        (Window owner, _, CommitWindow window, CommitWindowViewModel viewModel) = await ShowAsync(repo.Path);
        await SelectAsync(window, viewModel, "n.txt", staged: false);

        // Typing in the message box or the filter: letters are text.
        TextBox message = window.FindControl<TextBox>("MessageBox")!;
        message.Focus();
        Press(window, PhysicalKey.S);
        Press(window, PhysicalKey.R);
        viewModel.Staged.Should().BeEmpty();
        viewModel.IsConfirmingReset.Should().BeFalse();
        window.FindControl<TextBox>("FilterBox")!.Focus();
        Press(window, PhysicalKey.S);
        viewModel.Staged.Should().BeEmpty();

        // The diff view has the focus and no line is selected: the file-level shortcut applies.
        FocusDiff(window);
        Press(window, PhysicalKey.S);
        await WaitForAsync(() => viewModel.Staged.Any(f => f.Name == "n.txt"));

        // Focus on a button, file selected in the staged list: U unstages it.
        await SelectAsync(window, viewModel, "n.txt", staged: true);
        window.FindControl<Button>("RefreshButton")!.Focus();
        Press(window, PhysicalKey.U);
        await WaitForAsync(() => viewModel.Staged.Count == 0 && viewModel.Unstaged.Any(f => f.Name == "n.txt"));

        // R asks inline first, then deletes the untracked file.
        await SelectAsync(window, viewModel, "n.txt", staged: false);
        window.FindControl<Button>("RefreshButton")!.Focus();
        Press(window, PhysicalKey.R);
        viewModel.IsConfirmingReset.Should().BeTrue();
        File.Exists(repo.FullPath("n.txt")).Should().BeTrue();
        window.FindControl<Button>("ConfirmResetButton")!.Command!.Execute(null);
        await WaitForAsync(() => !File.Exists(repo.FullPath("n.txt")));
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Selected_lines_win_over_the_file_shortcut_when_the_diff_has_the_focus()
    {
        using MultiHunkRepository repo = new();
        (Window owner, _, CommitWindow window, CommitWindowViewModel viewModel) = await ShowAsync(repo.Path);
        await SelectAsync(window, viewModel, "m.txt", staged: false);
        FocusDiff(window);
        viewModel.Diff.SelectLine(Line(viewModel, "+changed 27"), ctrl: false, shift: false);

        Press(window, PhysicalKey.S);
        await WaitForAsync(() => viewModel.Staged.Count == 1);

        // Only the one line was staged, not the whole file.
        repo.Git("show", ":m.txt").Should().Contain("changed 27").And.NotContain("changed 3\n");
        repo.Git("diff").Should().Contain("changed 3");
        owner.Close();
    }

    [AvaloniaTest]
    public async Task Context_menu_and_tooltips_show_the_shortcuts()
    {
        using MultiHunkRepository repo = new();
        (Window owner, _, CommitWindow window, CommitWindowViewModel viewModel) = await ShowAsync(repo.Path);
        await SelectAsync(window, viewModel, "m.txt", staged: false);
        DiffView view = window.FindControl<DiffView>("DiffPanel")!;
        ContextMenu menu = view.FindControl<ItemsControl>("DiffLines")!.ContextMenu!;
        menu.Items.OfType<MenuItem>().Select(i => (i.Header, i.InputGesture?.Key)).Should().Equal(
            ("Stage selected line(s)", Key.S), ("Unstage selected line(s)", Key.U), ("Reset selected line(s)", Key.R));
        ToolTip.GetTip(window.FindControl<Button>("StageSelectedButton")!).Should().Be("Stage the selected files (S, or double-click a file)");

        viewModel.Diff.SelectLine(Line(viewModel, "+changed 3"), ctrl: false, shift: false);
        var items = menu.Items.OfType<MenuItem>().ToArray();
        items.Should().HaveCount(3);
        viewModel.Diff.CanStageSelected.Should().BeTrue();
        viewModel.Diff.CanUnstageSelected.Should().BeFalse();
        owner.Close();
    }

    // ---- main window, Diff tab ----------------------------------------------------------------------------------------

    [AvaloniaTest]
    public async Task Diff_tab_of_the_working_tree_stages_with_S_and_reverts_with_R()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel, Width = 1200, Height = 720 };
        window.Show();
        await viewModel.OpenAsync(repository.Path);
        Pump();

        viewModel.IsWorkingTreeSelected = true;
        viewModel.RevisionTabs.SelectedTabIndex = RevisionTabsViewModel.DiffTab;
        RevisionDiffViewModel diff = viewModel.RevisionTabs.Diff;
        await WaitForAsync(() => diff.Files.Count == 2 && diff.SelectedFile is not null && !diff.IsBusy);
        Pump();

        // Typing in the filter box: S is a letter.
        window.Find<RevisionTabsView>("RevisionTabs").GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "FilterBox").Focus();
        Press(window, PhysicalKey.S);
        repository.GitOutput("diff", "--cached", "--name-only").Should().BeEmpty();

        diff.SelectedFile = diff.Files.Single(f => f.Name == "a.txt");
        window.Find<ListBox>("CommitList").ContainerFromIndex(0)!.Focus();
        viewModel.Commands.Single(c => c.Id == "diff.stageFile").CanExecute().Should().BeTrue();
        Press(window, PhysicalKey.S);
        await WaitForAsync(() => repository.GitOutput("diff", "--cached", "--name-only").Trim() == "a.txt");
        await WaitForAsync(() => diff.Files.Any(f => f.Name == "a.txt" && f.IsStaged) && !diff.IsBusy);

        // R on the untracked file asks first, then deletes it.
        diff.SelectedFile = diff.Files.Single(f => f.Name == "c.txt");
        Press(window, PhysicalKey.R);
        diff.IsConfirmingRevert.Should().BeTrue();
        window.Find<RevisionTabsView>("RevisionTabs").GetVisualDescendants().OfType<Border>().Single(b => b.Name == "RevertConfirmation").IsVisible.Should().BeTrue();
        File.Exists(Path.Combine(repository.Path, "c.txt")).Should().BeTrue();
        diff.ConfirmRevertCommand.Execute(null);
        await WaitForAsync(() => !File.Exists(Path.Combine(repository.Path, "c.txt")));
        await WaitForAsync(() => diff.Files.All(f => f.Name != "c.txt") && !diff.IsBusy);
        window.Close();
    }

    [AvaloniaTest]
    public async Task Diff_tab_keys_do_nothing_for_a_commit()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel, Width = 1200, Height = 720 };
        window.Show();
        await viewModel.OpenAsync(repository.Path);
        viewModel.IsWorkingTreeSelected = false;
        viewModel.SelectedCommit = viewModel.Commits[0];
        viewModel.RevisionTabs.SelectedTabIndex = RevisionTabsViewModel.DiffTab;
        await WaitForAsync(() => viewModel.RevisionTabs.Diff.Files.Count > 0 && !viewModel.RevisionTabs.Diff.IsBusy);

        Press(window, PhysicalKey.S);
        Press(window, PhysicalKey.R);

        viewModel.RevisionTabs.Diff.IsConfirmingRevert.Should().BeFalse();
        repository.GitOutput("diff", "--cached", "--name-only").Should().BeEmpty();
        window.Close();
    }
}
