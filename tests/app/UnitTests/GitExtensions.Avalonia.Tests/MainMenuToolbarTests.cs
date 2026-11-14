using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class MainMenuToolbarTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class FakeWindows : IWindowService
    {
        public int CommitWindows { get; private set; }

        public void ShowCommitWindow() => CommitWindows++;

        public void ShowFileHistoryWindow(string path, string? revision = null)
        {
        }
    }

    private ThemeService _previousTheme = null!;
    private string _themeFile = null!;

    [SetUp]
    public void IsolateTheme()
    {
        _previousTheme = ThemeService.Current;
        _themeFile = Path.Combine(Path.GetTempPath(), "ge-menu-theme-" + Guid.NewGuid().ToString("N") + ".json");
        ThemeService.Current = new ThemeService(new ThemeStore(_themeFile));
    }

    [TearDown]
    public void RestoreTheme()
    {
        ThemeService.Current = _previousTheme;
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = ThemeVariant.Default;
        }

        File.Delete(_themeFile);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
            Pump();
        }

        condition().Should().BeTrue("the condition was not met in time");
    }

    private static MainWindow CreateWindow(out MainWindowViewModel viewModel, double width = 1200, double height = 720)
    {
        viewModel = new MainWindowViewModel(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel, Width = width, Height = height };
        window.Show();
        Pump();
        return window;
    }

    private static MainMenuView Menu(MainWindow window) => window.FindControl<MainMenuView>("MainMenuBar")!;

    private static MainToolbarView Toolbar(MainWindow window) => window.FindControl<MainToolbarView>("MainToolbar")!;

    private static MenuItem Item(MainWindow window, string name) => Menu(window).FindControl<MenuItem>(name)!;

    private static string Header(MenuItem item) => ((string)item.Header!).Replace("_", string.Empty, StringComparison.Ordinal);

    private static void SaveScreenshot(Window window, string name)
    {
        Pump();
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, name + ".png");
        window.CaptureRenderedFrame()!.Save(file);
        TestContext.AddTestAttachment(file);
    }

    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    [AvaloniaTest]
    public async Task The_menu_bar_has_the_original_top_level_menus_and_the_dashboard_menu_only_on_the_start_page()
    {
        using TestRepository repository = new();
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel);
        Menu bar = Menu(window).Bar;

        // The start page: Start, Dashboard, Plugins, Tools, Help (the repository menus are hidden).
        string[] visible = bar.Items.OfType<MenuItem>().Where(m => m.IsVisible).Select(Header).ToArray();
        visible.Should().Equal("Start", "Dashboard", "Plugins", "Tools", "Help");
        Toolbar(window).IsVisible.Should().BeFalse("the dashboard has no toolbar");
        window.Title.Should().Be("Git Extensions");
        Item(window, "DashboardRefreshMenuItem").Command.Should().BeSameAs(viewModel.RefreshDashboardCommand);

        await viewModel.OpenAsync(repository.Path);
        Pump();
        visible = bar.Items.OfType<MenuItem>().Where(m => m.IsVisible).Select(Header).ToArray();
        visible.Should().Equal("Start", "Repository", "Navigate", "View", "Commands", "Plugins", "Tools", "Help");
        Toolbar(window).IsVisible.Should().BeTrue();
        window.Title.Should().Be($"{Path.GetFileName(repository.Path)} (main) - Git Extensions");
        window.Close();
    }

    [AvaloniaTest]
    public async Task The_menu_items_of_the_specification_exist_and_missing_features_are_disabled_stubs()
    {
        using TestRepository repository = new();
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel);
        await viewModel.OpenAsync(repository.Path);
        Pump();

        List<MenuItem> all = Menu(window).Bar.GetLogicalDescendants().OfType<MenuItem>().ToList();
        string[] expected =
        [
            "Open…", "Open recent", "Clone repository…", "Create new repository…", "Favorite repositories…", "Settings…", "Exit",
            "Refresh", "File Explorer", "Remote repositories…", "Manage submodules…", "Update all submodules", "Synchronize all submodules",
            "Manage worktrees…", "Edit .gitignore", "Edit .git/info/exclude", "Edit .gitattributes", "Edit .mailmap", "Git maintenance",
            "Compress git database", "Recover lost objects…", "Delete index.lock", "Edit .git/config", "Repository settings…", "Close (go to Dashboard)",
            "Go to commit…", "Go to current revision", "Go to parent commit", "Go to child commit", "Next change in list", "Previous change in list", "Navigate backward", "Navigate forward",
            "Show/hide left panel", "Layout of the commit info", "Show tabs", "Filters", "Show remote branches", "Show tags",
            "Commit…", "Undo last commit…", "Pull…", "Fetch", "Push…", "Manage stashes…", "Reset changes…", "Clean working directory…",
            "Create branch…", "Delete branch…", "Checkout branch…", "Merge branches…", "Rebase…", "Solve merge conflicts…", "Create tag…", "Delete tag…",
            "Cherry pick…", "Archive revision…", "Checkout revision…", "Bisect…", "Show reflog…", "Format patch…", "Apply patch…", "View patch file…",
            "Plugins settings…", "Git bash / terminal", "Start authentication agent", "Generate or import key",
            "Documentation", "Report an issue", "Changelog", "Keyboard shortcuts", "About Git Extensions", "Check for updates",
        ];
        string[] headers = all.Select(m => m.Header as string).Where(h => h is not null).Select(h => h!.Replace("_", string.Empty, StringComparison.Ordinal)).ToArray();
        headers.Should().Contain(expected);

        // Features that do not exist yet are shown, disabled, with a tooltip.
        string[] stubs = ["Check for updates", "Generate or import key"];
        foreach (string name in stubs)
        {
            MenuItem item = all.First(m => m.Header is string h && h.Replace("_", string.Empty, StringComparison.Ordinal) == name);
            item.IsEnabled.Should().BeFalse(name);
            ToolTip.GetTip(item).Should().Be("Not available yet", name);
        }

        // Everything with a feature behind it is enabled with a repository open.
        foreach (string name in new[] { "RefreshMenuItem", "FileExplorerMenuItem", "RemotesMenuItem", "FavoritesMenuItem", "SubmodulesMenuItem", "WorktreesMenuItem", "CommitMenuItem", "PushMenuItem", "FetchMenuItem", "ReflogMenuItem", "CloseMenuItem", "GoToCurrentMenuItem", "OpenMenuItem", "CloneMenuItem", "InitMenuItem", "ShortcutsMenuItem", "AboutMenuItem", "CherryPickMenuItem", "ArchiveMenuItem", "CheckoutRevisionMenuItem", "DeleteBranchMenuItem", "ResetChangesMenuItem", "CleanMenuItem" })
        {
            MenuItem item = Item(window, name);
            item.IsEnabled.Should().BeTrue(name);
            item.Command.Should().NotBeNull(name);
            item.Command!.CanExecute(item.CommandParameter).Should().BeTrue(name);
        }

        window.Close();
    }

    [AvaloniaTest]
    public void The_repository_commands_cannot_run_on_the_dashboard()
    {
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel);
        viewModel.RefreshCommand.CanExecute(null).Should().BeFalse();
        Item(window, "CommandsMenu").IsVisible.Should().BeFalse();
        Item(window, "RepositoryMenu").IsVisible.Should().BeFalse();
        Item(window, "ShellMenuItem").IsEnabled.Should().BeFalse("the terminal needs a repository");
        Item(window, "OpenMenuItem").IsEnabled.Should().BeTrue();
        Item(window, "SettingsMenuItem").IsEnabled.Should().BeTrue();
        Toolbar(window).IsVisible.Should().BeFalse("Commit and Push live in the toolbar, which the dashboard hides");
        window.Close();
    }

    [AvaloniaTest]
    public async Task Menu_items_run_the_existing_commands()
    {
        using TestRepository repository = new();
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel);
        FakeWindows windows = new();
        viewModel.WindowService = windows;
        await viewModel.OpenAsync(repository.Path);
        Pump();

        Item(window, "CommitMenuItem").Command.Should().BeSameAs(viewModel.ShowCommitWindowCommand);
        Click(Item(window, "CommitMenuItem"));
        windows.CommitWindows.Should().Be(1);

        Item(window, "PullMenuItem").Command.Should().BeSameAs(viewModel.ShowPullDialogCommand);
        Item(window, "PushMenuItem").Command.Should().BeSameAs(viewModel.ShowPushDialogCommand);
        Item(window, "FetchMenuItem").Command.Should().BeSameAs(viewModel.Remote.FetchCommand);
        Item(window, "SettingsMenuItem").Command.Should().BeSameAs(viewModel.OpenSettingsCommand);
        Item(window, "OpenMenuItem").Command.Should().BeSameAs(viewModel.BrowseCommand);
        Item(window, "CloseMenuItem").Command.Should().BeSameAs(viewModel.CloseRepositoryCommand);

        // Settings… opens the settings overlay.
        Click(Item(window, "SettingsMenuItem"));
        await WaitAsync(() => viewModel.Settings.IsOpen);
        Pump();
        DialogWindow.Open.Should().ContainSingle().Which.Title.Should().Be("Settings");
        viewModel.Settings.CancelCommand.Execute(null);
        Pump();

        // Help > Keyboard shortcuts opens the shortcuts dialog.
        Click(Item(window, "ShortcutsMenuItem"));
        viewModel.ShortcutsHelp.IsOpen.Should().BeTrue();
        Pump();
        DialogWindow.Open.Should().ContainSingle();
        viewModel.ShortcutsHelp.Close();
        Pump();

        // Ctrl+K is the shortcut of Commands > Commit.
        window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.None, null);
        window.KeyRelease(Key.K, RawInputModifiers.Control, PhysicalKey.None, null);
        Pump();
        windows.CommitWindows.Should().Be(2);

        // Show reflog… opens its dialog; Manage submodules… and Manage worktrees… open theirs (separate windows).
        Click(Item(window, "ReflogMenuItem"));
        Pump();
        DialogWindow.Open.Should().ContainSingle().Which.Title.Should().Be("Reflog");
        viewModel.Tools.SelectedTabIndex.Should().Be(RepositoryToolsViewModel.ReflogTab);
        viewModel.Tools.Reflog.CloseCommand.Execute(null);
        Pump();
        Click(Item(window, "SubmodulesMenuItem"));
        Pump();
        DialogWindow.Open.Should().ContainSingle().Which.Title.Should().Be("Submodules");
        viewModel.Tools.SelectedTabIndex.Should().Be(RepositoryToolsViewModel.SubmodulesTab);
        viewModel.Tools.Submodules.CloseCommand.Execute(null);
        Pump();
        Click(Item(window, "WorktreesMenuItem"));
        Pump();
        DialogWindow.Open.Should().ContainSingle().Which.Title.Should().Be("Worktrees");
        viewModel.Tools.SelectedTabIndex.Should().Be(RepositoryToolsViewModel.WorktreesTab);
        viewModel.Tools.Worktrees.CloseCommand.Execute(null);
        Pump();
        DialogWindow.Open.Should().BeEmpty();

        // Tools > Command log opens the log dialog.
        Click(Item(window, "CommandLogMenuItem"));
        Pump();
        DialogWindow.Open.Should().ContainSingle().Which.Title.Should().Be("Command log");
        viewModel.CommandLogDialog.CloseCommand.Execute(null);
        Pump();
        DialogWindow.Open.Should().BeEmpty();
        Pump();
        window.Close();
    }

    [AvaloniaTest]
    public async Task The_commit_button_shows_the_number_of_changed_files_and_opens_the_commit_window()
    {
        using TestRepository repository = new();
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel);
        FakeWindows windows = new();
        viewModel.WindowService = windows;
        await viewModel.OpenAsync(repository.Path);
        Pump();

        TextBlock text = Toolbar(window).FindControl<TextBlock>("CommitText")!;
        text.Text.Should().Be("Commit (2)");

        File.WriteAllText(Path.Combine(repository.Path, "d.txt"), "another");
        await viewModel.RefreshChangesAsync();
        Pump();
        text.Text.Should().Be("Commit (3)");

        repository.RunGit("add", "-A");
        repository.RunGit("commit", "-q", "-m", "All in");
        await viewModel.RefreshChangesAsync();
        Pump();
        text.Text.Should().Be("Commit");

        Toolbar(window).FindControl<Button>("CommitButton")!.Command!.Execute(null);
        windows.CommitWindows.Should().Be(1);
        window.Close();
    }

    [AvaloniaTest]
    public async Task The_branch_list_shows_local_branches_and_selecting_one_checks_it_out()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel);
        await viewModel.OpenAsync(repository.Path);
        Pump();
        viewModel.LocalBranches.Should().Equal("develop", "feature/x", "main");

        MainToolbarView toolbar = Toolbar(window);
        toolbar.FindControl<TextBlock>("BranchText")!.Text.Should().Be("main");
        toolbar.BranchList.Flyout!.ShowAt(toolbar.BranchList);
        Pump();
        MenuFlyout flyout = (MenuFlyout)toolbar.BranchList.Flyout;
        MenuItem[] items = flyout.Items.OfType<MenuItem>().ToArray();
        items.Where(i => i.Tag is string).Select(i => (string)i.Tag!).Should().Equal("develop", "feature/x", "main");
        items.Single(i => i.IsChecked).Tag.Should().Be("main");
        items[^1].Header.Should().Be("Checkout branch…");
        items[^1].Command.Should().BeSameAs(viewModel.ShowCheckoutBranchDialogCommand);

        Click(items[0]);
        await WaitAsync(() => viewModel.Branch == "develop" && !viewModel.IsBusy);
        repository.GitOutput("rev-parse", "--abbrev-ref", "HEAD").Trim().Should().Be("develop");
        window.Title.Should().Contain("(develop)");
        toolbar.FindControl<TextBlock>("BranchText")!.Text.Should().Be("develop");
        flyout.Hide();
        window.Close();
    }

    [AvaloniaTest]
    public async Task The_branch_and_stash_dropdowns_render_their_items_in_both_themes()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel);
        window.Width = 1200;
        window.Height = 720;
        await viewModel.OpenAsync(repository.Path);
        Pump();
        MainToolbarView toolbar = Toolbar(window);
        StashMenuButton stash = toolbar.GetVisualDescendants().OfType<StashMenuButton>().Single();

        foreach ((ThemeVariant variant, string suffix) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
        {
            Application.Current!.RequestedThemeVariant = variant;
            Pump();
            foreach ((string name, Button button) in new[] { ("branch", (Button)toolbar.BranchList), ("stash", (Button)stash) })
            {
                button.Flyout!.ShowAt(button);
                Pump();
                Pump();
                MenuFlyout flyout = (MenuFlyout)button.Flyout;
                MenuItem[] items = [.. flyout.Items.OfType<MenuItem>()];
                string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
                Directory.CreateDirectory(directory);
                window.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"dropdown-{name}-{suffix}.png"));
                items.Should().NotBeEmpty(name);
                foreach (MenuItem item in items.Where(i => i.IsVisible))
                {
                    item.Bounds.Width.Should().BeGreaterThan(40, $"{name} item '{(item.Header as string) ?? item.Tag}' in the {suffix} theme must have a visible width");
                    item.Bounds.Height.Should().BeGreaterThan(10, name);
                }

                flyout.Hide();
                Pump();
            }
        }

        // The combo boxes of the filter bar (branches, text filter) open a list with their entries.
        foreach (ComboBox combo in window.GetVisualDescendants().OfType<ComboBox>().Where(c => c.IsEffectivelyVisible && c.ItemCount > 0))
        {
            combo.IsDropDownOpen = true;
            Pump();
            Pump();
            ComboBoxItem[] entries = [.. window.GetVisualDescendants().OfType<ComboBoxItem>().Where(i => i.IsEffectivelyVisible)];
            entries.Should().NotBeEmpty($"combo box {combo.Name} lists its items");
            entries.Should().OnlyContain(i => i.Bounds.Width > 20 && i.Bounds.Height > 10, $"combo box {combo.Name} items must be visible");
            string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
            window.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"dropdown-combo-{combo.Name}.png"));
            combo.IsDropDownOpen = false;
            Pump();
        }

        Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        window.Close();
    }

    [AvaloniaTest]
    public async Task The_repository_path_lists_the_recent_repositories_and_opens_the_chosen_one()
    {
        using TestRepository first = new();
        using TestRepository second = new();
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel);
        await viewModel.OpenAsync(second.Path);
        await viewModel.OpenAsync(first.Path);
        await WaitAsync(() => viewModel.Recent.Items.Any(i => Path.TrimEndingDirectorySeparator(i.Path) == second.Path)
            && viewModel.Recent.Items.Any(i => Path.TrimEndingDirectorySeparator(i.Path) == first.Path));

        MainToolbarView toolbar = Toolbar(window);
        toolbar.FindControl<TextBlock>("RepoPathText")!.Text.Should().Be(viewModel.RepositoryPath, "the button shows the working directory");

        toolbar.RepoPath.Flyout!.ShowAt(toolbar.RepoPath);
        Pump();
        MenuFlyout flyout = (MenuFlyout)toolbar.RepoPath.Flyout;
        MenuItem entry = flyout.Items.OfType<MenuItem>().First(i => i.Tag is RecentRepositoryItem r && Path.TrimEndingDirectorySeparator(r.Path) == second.Path);
        Click(entry);
        await WaitAsync(() => Path.TrimEndingDirectorySeparator(viewModel.RepositoryPath) == second.Path && !viewModel.IsBusy);
        flyout.Hide();

        // The Start > Open recent submenu lists them as well.
        MenuItem recent = Item(window, "OpenRecentMenu");
        recent.Items.OfType<MenuItem>().Select(i => Path.TrimEndingDirectorySeparator((i.Tag as RecentRepositoryItem)?.Path ?? string.Empty)).Should().Contain(second.Path);
        window.Close();
    }

    [AvaloniaTest]
    public async Task The_pull_button_runs_the_default_action_and_its_drop_down_offers_all_actions()
    {
        using RemoteScenario scenario = new();
        RemoteScenario.Commit(scenario.Beta, "b.txt", "two", "Add b.txt");
        RemoteScenario.Run(scenario.Beta, "push", "--quiet", "origin", "main");
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel);
        await viewModel.OpenAsync(scenario.Alpha);
        await viewModel.Remote.ReloadStateAsync();
        Pump();

        MainToolbarView toolbar = Toolbar(window);
        SplitButton pull = toolbar.FindControl<SplitButton>("PullButton")!;
        pull.Flyout!.ShowAt(pull);
        Pump();
        string[] entries = ((MenuFlyout)pull.Flyout!).Items.OfType<MenuItem>().Select(Header).ToArray();
        entries.Take(6).Should().Equal("Open pull dialog…", "Pull - merge", "Pull - rebase", "Fetch", "Fetch all", "Fetch and prune all");
        entries.Should().Contain("Set default pull action");

        viewModel.PullToolTip.Should().Be("Pull (merge)");
        viewModel.SetDefaultPullActionCommand.Execute(PullAction.FetchAndPruneAll);
        viewModel.PullToolTip.Should().Be("Fetch and prune all");
        toolbar.FindControl<MenuItem>("DefaultFetchPruneMenuItem")!.IsChecked.Should().BeTrue();
        toolbar.FindControl<MenuItem>("DefaultMergeMenuItem")!.IsChecked.Should().BeFalse();

        pull.Flyout.Hide();

        // Clicking the button itself runs the default action.
        await viewModel.RunDefaultPullCommand.ExecuteAsync(null);
        viewModel.Remote.OutputText.Should().Contain("git fetch --progress --all --prune");

        await viewModel.PullRebaseCommand.ExecuteAsync(null);
        viewModel.Remote.OutputText.Should().Contain("--rebase");
        viewModel.Status.Should().Be("Pull (rebase) completed.");
        viewModel.Commits.Should().HaveCount(2);

        await viewModel.FetchAllCommand.ExecuteAsync(null);
        viewModel.Status.Should().Be("Fetch all completed.");
        window.Close();
    }

    [AvaloniaTest]
    public async Task Navigate_and_merge_items_follow_the_history_and_the_selected_branch()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel);
        await viewModel.OpenAsync(repository.Path);
        Pump();

        // Parent / child walk the graph; "current revision" goes back to HEAD.
        viewModel.GoToCurrentRevisionCommand.Execute(null);
        viewModel.SelectedCommit.Should().BeSameAs(viewModel.Commits[0]);
        viewModel.GoToParentCommitCommand.Execute(null);
        viewModel.SelectedCommit.Should().BeSameAs(viewModel.Commits[1]);
        viewModel.GoToChildCommitCommand.Execute(null);
        viewModel.SelectedCommit.Should().BeSameAs(viewModel.Commits[0]);
        viewModel.NextChangeCommand.Execute(null);
        viewModel.SelectedCommit.Should().BeSameAs(viewModel.Commits[1]);
        viewModel.PreviousChangeCommand.Execute(null);
        viewModel.SelectedCommit.Should().BeSameAs(viewModel.Commits[0]);

        // Merge/Rebase are always available (they open a dialog); the branch selected in the left panel is prefilled.
        MenuItem merge = Item(window, "MergeMenuItem");
        viewModel.CanMergeSelectedRef.Should().BeFalse();
        merge.IsEnabled.Should().BeTrue();
        RefNodeViewModel develop = Flatten(viewModel.Refs.Roots).First(n => n.Kind == RefNodeKind.LocalBranch && n.FullName == "develop");
        viewModel.Refs.SelectedNode = develop;
        Pump();
        viewModel.CanMergeSelectedRef.Should().BeTrue();
        merge.IsEnabled.Should().BeTrue();
        Item(window, "RebaseMenuItem").IsEnabled.Should().BeTrue();
        await viewModel.MergeSelectedRefCommand.ExecuteAsync(null);
        viewModel.MergeDialog.IsOpen.Should().BeTrue();
        viewModel.MergeDialog.Target.Should().Be("develop");
        viewModel.MergeDialog.CloseCommand.Execute(null);
        await viewModel.RebaseOntoSelectedRefCommand.ExecuteAsync(null);
        viewModel.RebaseDialog.Target.Should().Be("develop");
        viewModel.RebaseDialog.CloseCommand.Execute(null);
        Item(window, "DeleteTagMenuItem").IsEnabled.Should().BeFalse();

        RefNodeViewModel tag = Flatten(viewModel.Refs.Roots).First(n => n.Kind == RefNodeKind.Tag);
        viewModel.Refs.SelectedNode = tag;
        Pump();
        Item(window, "DeleteTagMenuItem").IsEnabled.Should().BeTrue();
        window.Close();
    }

    private static IEnumerable<RefNodeViewModel> Flatten(IEnumerable<RefNodeViewModel> nodes)
    {
        foreach (RefNodeViewModel node in nodes)
        {
            yield return node;
            foreach (RefNodeViewModel child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }

    [AvaloniaTest]
    public async Task Repository_file_commands_delete_the_index_lock_and_undo_the_last_commit()
    {
        using TestRepository repository = new();
        MainWindow window = CreateWindow(out MainWindowViewModel viewModel);
        await viewModel.OpenAsync(repository.Path);
        await viewModel.UseCurrentBranchAsync();
        string gitDirectory = Path.Combine(repository.Path, ".git");
        File.WriteAllText(Path.Combine(gitDirectory, "index.lock"), string.Empty);
        viewModel.DeleteIndexLockCommand.Execute(null);
        File.Exists(Path.Combine(gitDirectory, "index.lock")).Should().BeFalse();

        await viewModel.UndoLastCommitCommand.ExecuteAsync(null);
        await WaitAsync(() => !viewModel.IsBusy && viewModel.Commits.Count == 1);
        repository.GitOutput("log", "--format=%s").Trim().Should().Be("Add a.txt");
        window.Close();
    }

    [AvaloniaTest]
    public async Task Screenshots_of_the_menu_and_toolbar_in_light_and_dark()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Application.Current!.RequestedThemeVariant = variant;
            string suffix = variant.Key.ToString()!.ToLowerInvariant();
            foreach ((double width, double height) in new[] { (1200.0, 720.0), (900.0, 600.0) })
            {
                MainWindow window = CreateWindow(out MainWindowViewModel viewModel, width, height);
                SaveScreenshot(window, $"menu-dashboard-{width}x{height}-{suffix}");
                await viewModel.OpenAsync(repository.Path);
                Pump();
                SaveScreenshot(window, $"menu-main-{width}x{height}-{suffix}");

                if (width > 1000)
                {
                    Item(window, "CommandsMenu").IsSubMenuOpen = true;
                    SaveScreenshot(window, "menu-commands-" + suffix);
                    Item(window, "CommandsMenu").IsSubMenuOpen = false;
                    Item(window, "RepositoryMenu").IsSubMenuOpen = true;
                    SaveScreenshot(window, "menu-repository-" + suffix);
                    Item(window, "RepositoryMenu").IsSubMenuOpen = false;

                    SplitButton pull = Toolbar(window).FindControl<SplitButton>("PullButton")!;
                    pull.Flyout!.ShowAt(pull);
                    SaveScreenshot(window, "toolbar-pull-" + suffix);
                    pull.Flyout.Hide();

                    Toolbar(window).BranchList.Flyout!.ShowAt(Toolbar(window).BranchList);
                    SaveScreenshot(window, "toolbar-branches-" + suffix);
                    Toolbar(window).BranchList.Flyout!.Hide();
                }

                window.Close();
            }
        }
    }
}
