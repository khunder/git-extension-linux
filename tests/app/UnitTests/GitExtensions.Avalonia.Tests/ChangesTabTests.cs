using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

/// <summary>
///  The Changes | History tabs of the sidebar: the Changes tab lists the changed files with a check box each
///  (checked = staged) and commits with a summary and a description, like GitHub Desktop.
/// </summary>
[TestFixture]
public class ChangesTabTests
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

    private static async Task<(MainWindowViewModel Main, WorkingChangesViewModel Tab)> OpenChangesTabAsync(string path)
    {
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(path);
        main.ViewOptions.ShowChangesTab = true;
        main.SidebarTabIndex = MainWindowViewModel.ChangesTabIndex;
        WorkingChangesViewModel tab = main.ChangesTab!;

        // Showing the tab starts its load; wait for it rather than starting a second one that would race it.
        await WaitForAsync(() => tab.Branch is not null && !tab.IsBusy, "the tab loads when it is shown");
        return (main, tab);
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

    // The option lives in the shared (test) view options store: leave it off for the other fixtures.
    [TearDown]
    public void TurnTheOptionOff()
        => ViewOptionsStore.Current.Save(ViewOptionsStore.Current.Load() with { ShowChangesTab = false });

    [AvaloniaTest]
    public async Task The_tabs_are_off_by_default_and_the_changes_tab_is_built_when_shown()
    {
        using TestRepository repository = new();
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(repository.Path);

        main.IsChangesTabEnabled.Should().BeFalse("the classic sidebar is the default");
        main.SidebarTabIndex = MainWindowViewModel.ChangesTabIndex;
        main.IsChangesTabSelected.Should().BeFalse("the tab cannot be shown while the option is off");
        main.ChangesTab.Should().BeNull("git status is not run for it");

        main.ViewOptions.ShowChangesTab = true;
        main.IsChangesTabSelected.Should().BeTrue();
        main.ChangesTab.Should().NotBeNull();
        main.ChangesTabCountText.Should().Be("2");
        ViewOptionsStore.Current.Load().ShowChangesTab.Should().BeTrue("the option is persisted");

        main.ViewOptions.ShowChangesTab = false;
        main.IsHistoryTabSelected.Should().BeTrue("turning the option off goes back to the classic sidebar");
        main.ChangesTab.Should().BeNull();
    }

    [AvaloniaTest]
    public async Task Check_boxes_stage_and_unstage_files()
    {
        using TestRepository repository = new();
        (MainWindowViewModel main, WorkingChangesViewModel tab) = await OpenChangesTabAsync(repository.Path);

        tab.Rows.Select(r => r.Name).Should().Equal("a.txt", "c.txt");
        tab.Rows.Should().OnlyContain(r => r.IsIncluded == false);
        tab.ChangedFilesHeader.Should().Be("2 changed files");
        tab.AllIncluded.Should().BeFalse();

        await tab.Rows[0].ToggleCommand.ExecuteAsync(null);
        tab.Rows.Single(r => r.Name == "a.txt").IsIncluded.Should().BeTrue();
        tab.AllIncluded.Should().BeNull("one of two files is checked");
        repository.GitOutput("diff", "--cached", "--name-only").Trim().Should().Be("a.txt");
        main.ChangedFiles.Count(f => f.IsStaged).Should().Be(1, "the main window follows");

        await tab.Rows.Single(r => r.Name == "a.txt").ToggleCommand.ExecuteAsync(null);
        tab.Rows.Should().OnlyContain(r => r.IsIncluded == false);
        repository.GitOutput("diff", "--cached", "--name-only").Trim().Should().BeEmpty();

        await tab.ToggleAllCommand.ExecuteAsync(null);
        tab.AllIncluded.Should().BeTrue();
        await tab.ToggleAllCommand.ExecuteAsync(null);
        tab.AllIncluded.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task A_partly_staged_file_is_one_row_with_a_mixed_check_box()
    {
        using TestRepository repository = new();
        repository.RunGit("add", "a.txt");
        File.WriteAllText(Path.Combine(repository.Path, "a.txt"), "changed again");
        (_, WorkingChangesViewModel tab) = await OpenChangesTabAsync(repository.Path);

        WorkingFileRowViewModel row = tab.Rows.Single(r => r.Name == "a.txt");
        row.IsIncluded.Should().BeNull();
        row.Unstaged.Should().NotBeNull();
        row.Staged.Should().NotBeNull();

        await row.ToggleCommand.ExecuteAsync(null);
        tab.Rows.Single(r => r.Name == "a.txt").IsIncluded.Should().BeTrue("checking a mixed file stages the rest");
    }

    [AvaloniaTest]
    public async Task Filter_narrows_the_rows_and_selection_shows_the_diff()
    {
        using TestRepository repository = new();
        (_, WorkingChangesViewModel tab) = await OpenChangesTabAsync(repository.Path);

        tab.Filter = "c.";
        await WaitForAsync(() => tab.Rows.Count == 1);
        tab.Rows.Single().Name.Should().Be("c.txt");

        tab.Filter = string.Empty;
        await WaitForAsync(() => tab.Rows.Count == 2);
        tab.SelectOnly(tab.Rows.Single(r => r.Name == "a.txt"));
        await WaitForAsync(() => tab.Diff.Lines.Count > 0, "the diff of the selected file is shown");
        tab.Diff.File!.Name.Should().Be("a.txt");
    }

    [AvaloniaTest]
    public async Task Summary_and_description_make_the_commit_message()
    {
        using TestRepository repository = new();
        ConfigureIdentity(repository);
        (MainWindowViewModel main, WorkingChangesViewModel tab) = await OpenChangesTabAsync(repository.Path);
        tab.CommitButtonText.Should().Be("Commit to main");

        tab.CommitCommand.CanExecute(null).Should().BeFalse("nothing is checked and there is no summary");
        await tab.Rows.Single(r => r.Name == "a.txt").ToggleCommand.ExecuteAsync(null);
        tab.CommitCommand.CanExecute(null).Should().BeFalse("the summary is required");

        tab.Summary = "Change a.txt";
        tab.Description = "With a body.";
        tab.Commit.Message.Should().Be("Change a.txt\n\nWith a body.");
        tab.CommitCommand.CanExecute(null).Should().BeTrue();

        await tab.CommitCommand.ExecuteAsync(null);

        repository.GitOutput("log", "-1", "--format=%B").Trim().Should().Be("Change a.txt\n\nWith a body.");
        await WaitForAsync(() => main.Commits.Count == 3 && main.Commits[0].Subject == "Change a.txt");
        WorkingChangesViewModel after = main.ChangesTab!;
        after.Summary.Should().BeEmpty();
        after.Description.Should().BeEmpty();
        await WaitForAsync(() => after.Rows.Count == 1);
        after.Rows.Single().Name.Should().Be("c.txt");
    }

    [AvaloniaTest]
    public async Task The_check_box_of_a_selected_row_acts_on_the_whole_selection()
    {
        using TestRepository repository = new();
        (_, WorkingChangesViewModel tab) = await OpenChangesTabAsync(repository.Path);

        tab.SelectAll();
        tab.HasMultipleSelection.Should().BeTrue();
        tab.MultipleSelectionText.Should().Be("2 files selected");
        tab.SelectedRow.Should().BeNull("no single diff while several files are selected");

        await tab.Rows[0].ToggleCommand.ExecuteAsync(null);
        tab.Rows.Should().OnlyContain(r => r.IsIncluded == true);
        tab.SelectedRows.Should().HaveCount(2, "the selection survives the refresh");

        tab.SelectOnly(tab.Rows.Single(r => r.Name == "c.txt"));
        await tab.ExcludeSelectedAsync();
        tab.Rows.Single(r => r.Name == "c.txt").IsIncluded.Should().BeFalse();
        tab.Rows.Single(r => r.Name == "a.txt").IsIncluded.Should().BeTrue();
    }

    [AvaloniaTest]
    public async Task Discard_asks_first_and_then_restores_the_files()
    {
        using TestRepository repository = new();
        (_, WorkingChangesViewModel tab) = await OpenChangesTabAsync(repository.Path);

        tab.SelectOnly(tab.Rows.Single(r => r.Name == "a.txt"));
        tab.DiscardSelected();
        tab.IsConfirming.Should().BeTrue();
        File.ReadAllText(Path.Combine(repository.Path, "a.txt")).Should().Be("changed", "nothing happens before the confirmation");

        await tab.ConfirmCommand.ExecuteAsync(null);
        tab.IsConfirming.Should().BeFalse();
        File.ReadAllText(Path.Combine(repository.Path, "a.txt")).Should().Be("first");
        tab.Rows.Select(r => r.Name).Should().Equal("c.txt");

        tab.DiscardAll();
        tab.CancelConfirmationCommand.Execute(null);
        File.Exists(Path.Combine(repository.Path, "c.txt")).Should().BeTrue("cancel keeps the files");
    }

    [AvaloniaTest]
    public async Task Stash_all_shows_the_stashed_changes_which_can_be_restored()
    {
        using TestRepository repository = new();
        ConfigureIdentity(repository);
        (_, WorkingChangesViewModel tab) = await OpenChangesTabAsync(repository.Path);

        await tab.StashAllCommand.ExecuteAsync(null);
        await WaitForAsync(() => tab.HasBranchStash && tab.Rows.Count == 0, "the changes moved into a stash");
        tab.HasNoChanges.Should().BeTrue();

        tab.ShowStashCommand.Execute(null);
        tab.IsShowingStash.Should().BeTrue();
        await WaitForAsync(() => tab.Stashes.Files.Count == 2, "the stashed files are listed");
        tab.Stashes.Files.Select(f => f.Name).Should().BeEquivalentTo("a.txt", "c.txt");

        await tab.RestoreStashCommand.ExecuteAsync(null);
        await WaitForAsync(() => !tab.HasBranchStash && tab.Rows.Count == 2, "restore pops the stash back into the working tree");
        tab.IsShowingStash.Should().BeFalse();
        File.ReadAllText(Path.Combine(repository.Path, "a.txt")).Should().Be("changed");
    }

    [AvaloniaTest]
    public async Task Stash_selected_files_and_discard_the_stash()
    {
        using TestRepository repository = new();
        ConfigureIdentity(repository);
        (_, WorkingChangesViewModel tab) = await OpenChangesTabAsync(repository.Path);

        tab.SelectOnly(tab.Rows.Single(r => r.Name == "a.txt"));
        await tab.StashSelectedCommand.ExecuteAsync(null);
        await WaitForAsync(() => tab.HasBranchStash && tab.Rows.Count == 1, "only the selected file is stashed");
        tab.Rows.Single().Name.Should().Be("c.txt");

        tab.DiscardStashCommand.Execute(null);
        tab.IsConfirming.Should().BeTrue();
        await tab.ConfirmCommand.ExecuteAsync(null);
        await WaitForAsync(() => !tab.HasBranchStash, "the stash is dropped");
        repository.GitOutput("stash", "list").Trim().Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Ignoring_an_extension_hides_the_untracked_files()
    {
        using TestRepository repository = new();
        File.WriteAllText(Path.Combine(repository.Path, "build.log"), "noise");
        (_, WorkingChangesViewModel tab) = await OpenChangesTabAsync(repository.Path);
        tab.Rows.Should().Contain(r => r.Name == "build.log");

        await tab.AddToGitIgnoreAsync(["*.log"]);
        await WaitForAsync(() => tab.Rows.All(r => r.Name != "build.log"));
        File.ReadAllText(Path.Combine(repository.Path, ".gitignore")).Should().Contain("*.log");
    }

    [AvaloniaTest]
    [TestCase(ThemeChoice.Graphite)]
    [TestCase(ThemeChoice.Mist)]
    [TestCase(ThemeChoice.Dusk)]
    [TestCase(ThemeChoice.Sand)]
    public async Task Named_themes_render_the_changes_tab(ThemeChoice theme)
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel, Width = 1200, Height = 720, RequestedThemeVariant = ThemeService.ToVariant(theme) };
        window.Show();
        await viewModel.OpenAsync(repository.Path);
        viewModel.ViewOptions.ShowChangesTab = true;
        viewModel.SidebarTabIndex = MainWindowViewModel.ChangesTabIndex;
        WorkingChangesViewModel tab = viewModel.ChangesTab!;
        await WaitForAsync(() => tab.Rows.Count == 2);
        await tab.Rows.Single(r => r.Name == "a.txt").ToggleCommand.ExecuteAsync(null);
        tab.SelectOnly(tab.Rows.Single(r => r.Name == "a.txt"));
        await WaitForAsync(() => tab.Diff.Lines.Count > 0);
        ShellTestSupport.Settle();

        window.ActualThemeVariant.Should().Be(ThemeService.ToVariant(theme));
        ThemePalettes.IsDark(window.ActualThemeVariant).Should().Be(theme is ThemeChoice.Graphite or ThemeChoice.Dusk);
        window.Background.Should().BeAssignableTo<Avalonia.Media.ISolidColorBrush>()
            .Which.Color.Should().Be(ThemePalettes.All[theme].Palette.Window, "the palette re-skins the window");

        WriteableBitmap frame = window.CaptureRenderedFrame()!;
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, $"changes-tab-{theme.ToString().ToLowerInvariant()}.png");
        frame.Save(file);
        TestContext.AddTestAttachment(file);
        window.Close();
    }

    [AvaloniaTest]
    public async Task Right_click_on_a_file_builds_the_menu_for_the_selection()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.OpenAsync(repository.Path);
        viewModel.ViewOptions.ShowChangesTab = true;
        viewModel.SidebarTabIndex = MainWindowViewModel.ChangesTabIndex;
        WorkingChangesViewModel tab = viewModel.ChangesTab!;
        await WaitForAsync(() => tab.Rows.Count == 2);
        ShellTestSupport.Settle();

        tab.SelectAll();
        ShellTestSupport.Settle();
        ListBox list = window.Find<ListBox>("FileList");
        Control row = list.ContainerFromIndex(0)!;
        row.RaiseEvent(new ContextRequestedEventArgs());

        string[] headers = [.. list.ContextMenu!.Items.OfType<MenuItem>().Select(GitExtensions.Desktop.Views.ContextMenus.MenuKit.TextOf)];
        headers.Should().Contain("Discard 2 selected changes…").And.Contain("Stash 2 selected files").And.Contain("Ignore all .txt files (add to .gitignore)");
        headers.Should().Contain("Show in folder", "the Commit window's file menu follows");
        tab.SelectedRows.Should().HaveCount(2, "a right click inside the selection keeps it");
        window.Close();
    }

    [Test]
    public void Split_takes_the_first_line_as_summary()
    {
        WorkingChangesViewModel.Split("Subject").Should().Be(("Subject", string.Empty));
        WorkingChangesViewModel.Split("Subject\n\nBody\nmore").Should().Be(("Subject", "Body\nmore"));
        WorkingChangesViewModel.Split("Subject\r\n\r\nBody").Should().Be(("Subject", "Body"));
    }

    [AvaloniaTest]
    public async Task Tabs_switch_the_sidebar_and_the_main_area()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.OpenAsync(repository.Path);
        ShellTestSupport.Settle();

        // Option off: the classic sidebar, no tabs.
        window.Find<TabStrip>("SidebarTabs").IsVisible.Should().BeFalse();
        window.Find<RefsTreeView>("SidebarPanel").IsEffectivelyVisible.Should().BeTrue();
        window.Find<Grid>("BrowseRows").IsVisible.Should().BeTrue();
        window.Find<ChangesSidebarView>("ChangesSidebar").IsEffectivelyVisible.Should().BeFalse();

        // View > "Changes tab in the sidebar" shows the tabs; History stays selected until Changes is clicked.
        window.Find<MainMenuView>("MainMenuBar").FindControl<MenuItem>("ShowChangesTabMenuItem")!.IsChecked = true;
        ShellTestSupport.Settle();
        viewModel.IsChangesTabEnabled.Should().BeTrue();
        window.Find<TabStrip>("SidebarTabs").IsVisible.Should().BeTrue();
        window.Find<Grid>("BrowseRows").IsVisible.Should().BeTrue();

        window.Find<TabStrip>("SidebarTabs").SelectedIndex = MainWindowViewModel.ChangesTabIndex;
        ShellTestSupport.Settle();
        viewModel.IsChangesTabSelected.Should().BeTrue();
        window.Find<ChangesSidebarView>("ChangesSidebar").IsEffectivelyVisible.Should().BeTrue();
        window.Find<Grid>("BrowseRows").IsVisible.Should().BeFalse();
        window.Find<Border>("ChangesDiffArea").IsVisible.Should().BeTrue();
        await WaitForAsync(() => window.Find<ListBox>("FileList").ItemCount == 2, "the changed files are listed");

        // Leave a screenshot of the tab next to the test results, with a file checked and its diff selected.
        WorkingChangesViewModel tab = viewModel.ChangesTab!;
        await tab.Rows.Single(r => r.Name == "a.txt").ToggleCommand.ExecuteAsync(null);
        tab.SelectOnly(tab.Rows.Single(r => r.Name == "a.txt"));
        tab.Summary = "Change a.txt";
        await WaitForAsync(() => tab.Diff.Lines.Count > 0);
        ShellTestSupport.Settle();
        WriteableBitmap? frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "changes-tab.png");
        frame!.Save(file);
        TestContext.AddTestAttachment(file);

        viewModel.SidebarTabIndex = MainWindowViewModel.HistoryTabIndex;
        ShellTestSupport.Settle();
        window.Find<TabStrip>("SidebarTabs").SelectedIndex.Should().Be(MainWindowViewModel.HistoryTabIndex);
        window.Find<Grid>("BrowseRows").IsVisible.Should().BeTrue();
        window.Close();
    }
}
