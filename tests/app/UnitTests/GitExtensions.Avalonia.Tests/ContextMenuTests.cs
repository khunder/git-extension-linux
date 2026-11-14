using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;
using GitExtensions.Desktop.Views.ContextMenus;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class ContextMenuTests
{
    private ViewOptionsStore _previousOptions = null!;
    private string _optionsFile = null!;

    // The View entries change persisted options: keep them away from the other tests (and from the real settings).
    [OneTimeSetUp]
    public void IsolateViewOptions()
    {
        _previousOptions = ViewOptionsStore.Current;
        _optionsFile = Path.Combine(Path.GetTempPath(), "ge-avalonia-ctx-options-" + Guid.NewGuid().ToString("N") + ".json");
        ViewOptionsStore.Current = new ViewOptionsStore(_optionsFile);
    }

    [SetUp]
    public void ResetViewOptions() => File.Delete(_optionsFile);

    [OneTimeTearDown]
    public void RestoreViewOptions()
    {
        ViewOptionsStore.Current = _previousOptions;
        File.Delete(_optionsFile);
    }

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

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }

        condition().Should().BeTrue("the condition should become true");
    }

    private static async Task<(MainWindow Window, MainWindowViewModel ViewModel)> OpenAsync(TestRepository repository, ThemeVariant? theme = null)
    {
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel, Width = 1200, Height = 720 };
        if (theme is not null)
        {
            window.RequestedThemeVariant = theme;
        }

        window.Show();
        await viewModel.OpenAsync(repository.Path);
        Pump();
        return (window, viewModel);
    }

    /// <summary>Opens the menu in the window and saves a screenshot of the popup (the popup is its own top level).</summary>
    internal static void ShowAndCapture(Window window, ContextMenu menu, Control target, string name)
    {
        menu.Open(target);
        Pump();
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, name + ".png");
        if (TopLevel.GetTopLevel(menu) is { } popup && popup != window)
        {
            popup.CaptureRenderedFrame()!.Save(file);
        }
        else
        {
            window.CaptureRenderedFrame()!.Save(file);
        }

        TestContext.Out.WriteLine(file);
        menu.Close();
        Pump();
    }

    private static ContextMenu MenuOf(IEnumerable<Control> items)
    {
        ContextMenu menu = MenuKit.NewMenu();
        foreach (Control item in items)
        {
            menu.Items.Add(item);
        }

        return menu;
    }

    private static MenuItem Find(IEnumerable<Control> items, string header)
        => items.OfType<MenuItem>().First(i => MenuKit.TextOf(i) == header);

    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static IEnumerable<MenuItem> AllItems(IEnumerable<Control> items)
    {
        foreach (MenuItem item in items.OfType<MenuItem>())
        {
            yield return item;
            foreach (MenuItem child in AllItems(item.Items.OfType<Control>()))
            {
                yield return child;
            }
        }
    }

    private static string Outline(IEnumerable<Control> items) => string.Join("|", items.Select(MenuKit.TextOf));

    private static string[] Texts(MenuItem item) => [.. item.Items.OfType<Control>().Select(MenuKit.TextOf)];

    private static async Task<TestRepository> RichRepositoryAsync()
    {
        TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        await Task.CompletedTask;
        return repository;
    }

    [AvaloniaTest]
    public async Task Revision_grid_menu_is_structured_like_the_original()
    {
        using TestRepository repository = await RichRepositoryAsync();
        (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repository);
        List<Control> menu = RevisionGridContextMenu.Build(viewModel, viewModel.Commits[0], new FakeClipboard());

        Outline(menu).Should().Be(
            "Copy to clipboard|-|Checkout branch…|Merge into current branch…|Rebase current branch on…|Reset current branch to here…|-|"
            + "Create new branch here…|Rename branch…|Delete branch…|Push branch…|Reset another branch here…|-|"
            + "Create new tag here…|Delete tag…|-|"
            + "Checkout this commit…|Revert this commit…|Cherry pick this commit…|Archive this commit…|Format patch…|Advanced|-|"
            + "Compare|-|Navigate|Select in left panel|View|-|Open on GitHub|View pull request in a browser|View build report in a browser");

        Find(menu, "Create new branch here…").InputGesture!.ToString().Should().Be("Ctrl+B");
        Find(menu, "Create new tag here…").InputGesture!.ToString().Should().Be("Ctrl+T");
        Texts(Find(menu, "Advanced")).Should().Equal(
            "Edit commit", "Edit commit by creating a fixup commit and squash-rebase…", "Reword commit", "Create a fixup commit…",
            "Create a squash commit…", "Create an amend commit…", "-", "Autosquash fixup commits into this commit's parent…", "Interactive rebase editor…", "-",
            "Get help on how to use these features");
        Find(Find(menu, "Advanced").Items.OfType<Control>(), "Interactive rebase editor…").IsEnabled.Should().BeFalse();
        ToolTip.GetTip(Find(Find(menu, "Advanced").Items.OfType<Control>(), "Interactive rebase editor…")).Should().Be("Needs the interactive rebase editor");
        Texts(Find(menu, "Compare")).Should().Contain(["Compare to branch…", "Compare with current branch", "Compare to working directory", "Compare selected commits"]);
        Texts(Find(menu, "Navigate")).Should().Contain(["Go to current revision", "Go to child commit", "Go to parent commit", "Navigate backward", "Navigate forward"]);
        window.Close();
    }

    [AvaloniaTest]
    public async Task Every_entry_has_an_original_icon_and_stubs_are_disabled_with_a_tooltip()
    {
        using TestRepository repository = await RichRepositoryAsync();
        (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repository);
        List<Control> menu = RevisionGridContextMenu.Build(viewModel, viewModel.Commits[0], new FakeClipboard());

        foreach (MenuItem item in AllItems(menu))
        {
            string text = MenuKit.TextOf(item);
            if (MenuKit.IsSection(item) || item.Parent is MenuItem { Header: TextBlock { Text: "Advanced" or "Compare" } } || text.Contains("difftool", StringComparison.Ordinal))
            {
                continue;
            }

            string? icon = (item.Icon as AppIcon)?.IconName;
            if (item.Parent is ContextMenu or null || menu.Contains(item))
            {
                icon.Should().NotBeNullOrEmpty(text);
            }

            if (!string.IsNullOrEmpty(icon))
            {
                IconCache.Get(icon, dark: false).Should().NotBeNull($"{text} uses icon {icon}");
            }
        }

        foreach (MenuItem stub in AllItems(menu).Where(i => !i.IsEnabled && i.Items.Count == 0 && ToolTip.GetTip(i) is "Not available yet"))
        {
            ToolTip.GetShowOnDisabled(stub).Should().BeTrue(MenuKit.TextOf(stub));
        }

        AllItems(menu).Count(i => ToolTip.GetTip(i) is "Not available yet").Should().BeGreaterThan(10);
        Find(menu, "Reset another branch here…").IsEnabled.Should().BeTrue();
        window.Close();
    }

    [AvaloniaTest]
    public async Task View_submenu_has_the_original_sections_and_shortcuts_and_is_wired_to_the_filter_state()
    {
        using TestRepository repository = await RichRepositoryAsync();
        (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repository);
        CommitItemViewModel head = viewModel.Commits[0];
        MenuItem view = Find(RevisionGridContextMenu.Build(viewModel, head, null), "View");

        view.Items.OfType<Control>().Where(MenuKit.IsSection).Select(MenuKit.TextOf).Should().Equal("Branches", "Commits", "Grid labels", "Grid info");
        MenuItem Entry(string header) => Find(view.Items.OfType<Control>(), header);
        Entry("Show all branches").InputGesture!.ToString().Should().Be("Ctrl+Shift+A");
        Entry("Show current branch only").InputGesture!.ToString().Should().Be("Ctrl+Shift+U");
        Entry("Show filtered branches").InputGesture!.ToString().Should().Be("Ctrl+Shift+T");
        Entry("Show reflog references").InputGesture!.ToString().Should().Be("Ctrl+Shift+L");
        Entry("Advanced filter…").InputGesture!.ToString().Should().Be("Ctrl+I");
        Entry("Show remote branches").InputGesture!.ToString().Should().Be("Ctrl+Shift+R");
        Entry("Show tags").InputGesture!.ToString().Should().Be("Ctrl+Alt+T");
        Entry("Show all branches").IsChecked.Should().BeTrue();

        Click(Entry("Show current branch only"));
        viewModel.SelectedBranchChoice.Should().Be(MainWindowViewModel.CurrentBranchChoice);
        view = Find(RevisionGridContextMenu.Build(viewModel, head, null), "View");
        Entry("Show current branch only").IsChecked.Should().BeTrue();
        Entry("Show all branches").IsChecked.Should().BeFalse();

        Click(Entry("Show all branches"));
        viewModel.SelectedBranchChoice.Should().Be(MainWindowViewModel.AllBranchesChoice);

        view = Find(RevisionGridContextMenu.Build(viewModel, head, null), "View");
        Entry("Show tags").IsChecked.Should().BeTrue();
        Click(Entry("Show tags"));
        viewModel.ShowTags.Should().BeFalse();
        view = Find(RevisionGridContextMenu.Build(viewModel, head, null), "View");
        Click(Entry("Show remote branches"));
        viewModel.ShowRemoteBranches.Should().BeFalse();

        viewModel.ViewOptions.ShowFilterBar = false;
        view = Find(RevisionGridContextMenu.Build(viewModel, head, null), "View");
        bool focusRequested = false;
        viewModel.RevisionFilterFocusRequested += () => focusRequested = true;
        Click(Entry("Advanced filter…"));
        viewModel.ViewOptions.ShowFilterBar.Should().BeTrue();
        focusRequested.Should().BeTrue();

        // The View shortcuts also work with the grid focused.
        viewModel.ShowTags = true;
        RevisionGridContextMenu.TryHandleKey(viewModel, new Avalonia.Input.KeyEventArgs { Key = Avalonia.Input.Key.T, KeyModifiers = Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Alt }).Should().BeTrue();
        viewModel.ShowTags.Should().BeFalse();
        window.Close();
    }

    [AvaloniaTest]
    public async Task Copy_to_clipboard_lists_hash_message_author_dates_and_the_refs()
    {
        using TestRepository repository = await RichRepositoryAsync();
        (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repository);
        FakeClipboard clipboard = new();
        CommitItemViewModel head = viewModel.Commits[0];
        MenuItem copy = Find(RevisionGridContextMenu.Build(viewModel, head, clipboard), "Copy to clipboard");

        string[] texts = Texts(copy);
        texts.Should().Contain(t => t.StartsWith("Commit hash:   ", StringComparison.Ordinal));
        texts.Should().Contain(t => t.StartsWith("Message:   Add b.txt", StringComparison.Ordinal));
        texts.Should().Contain(t => t.StartsWith("Author:   Test Author <test@example.com>", StringComparison.Ordinal));
        texts.Should().Contain(["Branches", "Tags", "develop", "feature/x", "v1.0", "release/2.0", "origin/main"]);

        Click(copy.Items.OfType<MenuItem>().First(i => MenuKit.TextOf(i).StartsWith("Commit hash", StringComparison.Ordinal)));
        clipboard.Text.Should().Be(head.ObjectId.ToString());
        Click(copy.Items.OfType<MenuItem>().First(i => MenuKit.TextOf(i) == "develop"));
        clipboard.Text.Should().Be("develop");
        window.Close();
    }

    [AvaloniaTest]
    public async Task Branch_entries_list_only_the_branches_of_the_commit_and_run_real_git()
    {
        using TestRepository repository = await RichRepositoryAsync();
        repository.RunGit("branch", "old", "HEAD~1");
        (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repository);
        CommitItemViewModel head = viewModel.Commits[0];
        CommitItemViewModel first = viewModel.Commits[1];

        List<Control> menu = RevisionGridContextMenu.Build(viewModel, head, null);
        Texts(Find(menu, "Checkout branch…")).Should().Equal("develop", "feature/x", "origin/feature/y", "origin/main");
        Texts(Find(menu, "Delete branch…")).Should().Equal("develop", "feature/x");
        Texts(Find(menu, "Rename branch…")).Should().Equal("main", "develop", "feature/x");
        Texts(Find(menu, "Delete tag…")).Should().Equal("release/2.0", "v1.0");

        menu = RevisionGridContextMenu.Build(viewModel, first, null);
        Texts(Find(menu, "Checkout branch…")).Should().Equal("old");
        Find(menu, "Delete tag…").IsEnabled.Should().BeFalse();
        ToolTip.GetTip(Find(menu, "Delete tag…")).Should().Be("No tag points at this commit");
        Find(Find(menu, "Navigate").Items.OfType<Control>(), "Go to parent commit").IsEnabled.Should().BeFalse();

        // Checkout branch ▸ old
        Click(Find(Find(menu, "Checkout branch…").Items.OfType<Control>(), "old"));
        await WaitUntilAsync(() => repository.GitOutput("branch", "--show-current").Trim() == "old");
        await WaitUntilAsync(() => viewModel.Branch == "old");
        repository.GitOutput("branch", "--show-current").Trim().Should().Be("old");

        window.Close();
    }

    [AvaloniaTest]
    public async Task Merge_and_rebase_entries_open_their_dialogs()
    {
        using TestRepository repository = new();
        repository.RunGit("config", "user.name", "Test Author");
        repository.RunGit("config", "user.email", "test@example.com");
        repository.RunGit("stash", "--include-untracked");
        repository.RunGit("checkout", "--quiet", "-b", "side");
        repository.CommitFile("side.txt", "s", "Side work");
        repository.RunGit("checkout", "--quiet", "main");
        (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repository);
        CommitItemViewModel side = viewModel.Commits.Single(c => c.Subject == "Side work");

        // Merge opens its dialog with the branch at the commit prefilled; the merge runs from there.
        Click(Find(RevisionGridContextMenu.Build(viewModel, side, null), "Merge into current branch…"));
        await WaitUntilAsync(() => viewModel.MergeDialog.IsOpen && viewModel.MergeDialog.AllBranches.Count > 0);
        viewModel.MergeDialog.Target.Should().Be("side");
        await viewModel.MergeDialog.MergeCommand.ExecuteAsync(null);
        viewModel.MergeDialog.IsOpen.Should().BeFalse();
        File.Exists(Path.Combine(repository.Path, "side.txt")).Should().BeTrue();
        repository.GitOutput("log", "--format=%s", "-1").Trim().Should().Be("Side work");

        // Rebase opens its dialog prefilled with the commit (no other branch points at it): main is at the same commit now, so it is a no-op that still succeeds.
        await WaitUntilAsync(() => viewModel.Commits.Count > 0 && !viewModel.IsBusy);
        CommitItemViewModel head = viewModel.Commits[0];
        MenuItem rebase = Find(RevisionGridContextMenu.Build(viewModel, head, null), "Rebase current branch on…");
        rebase.Items.OfType<Control>().Should().BeEmpty();
        Click(rebase);
        await WaitUntilAsync(() => viewModel.RebaseDialog.IsOpen && viewModel.RebaseDialog.AllBranches.Count > 0);
        viewModel.RebaseDialog.Target.Should().NotBeNullOrEmpty();
        await viewModel.RebaseDialog.RebaseCommand.ExecuteAsync(null);
        viewModel.RebaseDialog.IsOpen.Should().BeFalse();
        window.Close();
    }

    [AvaloniaTest]
    public async Task Tag_branch_and_commit_entries_open_the_existing_prompts_and_operations()
    {
        using TestRepository repository = await RichRepositoryAsync();
        (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repository);
        CommitItemViewModel head = viewModel.Commits[0];
        List<Control> menu = RevisionGridContextMenu.Build(viewModel, head, null);

        Click(Find(menu, "Create new branch here…"));
        head.Actions!.IsPromptVisible.Should().BeTrue();
        head.Actions.PromptKind.Should().Be(CommitPromptKind.CreateBranch);
        head.Actions.CancelPromptCommand.Execute(null);

        Click(Find(menu, "Create new tag here…"));
        head.Actions.PromptKind.Should().Be(CommitPromptKind.CreateTag);
        head.Actions.InputText = "from-menu";
        await head.Actions.ConfirmPromptAsync();
        repository.GitOutput("rev-parse", "from-menu^{commit}").Trim().Should().Be(head.ObjectId.ToString());

        menu = RevisionGridContextMenu.Build(viewModel, head, null);
        Click(Find(Find(menu, "Rename branch…").Items.OfType<Control>(), "develop"));
        viewModel.Refs.IsPromptVisible.Should().BeTrue();
        viewModel.Refs.PromptKind.Should().Be(RefPromptKind.RenameBranch);
        viewModel.Refs.CancelPromptCommand.Execute(null);

        Click(Find(Find(menu, "Delete tag…").Items.OfType<Control>(), "release/2.0"));
        viewModel.Refs.PromptKind.Should().Be(RefPromptKind.DeleteTag);
        viewModel.Refs.CancelPromptCommand.Execute(null);

        Click(Find(menu, "Reset current branch to here…"));
        viewModel.RevisionOps.IsReset.Should().BeTrue();
        viewModel.RevisionOps.CancelCommand.Execute(null);

        Click(Find(menu, "Archive this commit…"));
        viewModel.RevisionOps.IsArchive.Should().BeTrue();
        viewModel.RevisionOps.CancelCommand.Execute(null);

        Click(Find(menu, "Select in left panel"));
        viewModel.Refs.SelectedNode.Should().NotBeNull();
        window.Close();
    }

    [AvaloniaTest]
    public async Task Revert_and_checkout_entries_reuse_the_commit_actions()
    {
        using TestRepository repository = new();
        repository.RunGit("stash", "--include-untracked");
        (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repository);
        CommitItemViewModel head = viewModel.Commits[0];

        Click(Find(RevisionGridContextMenu.Build(viewModel, head, null), "Revert this commit…"));
        head.Actions!.PromptKind.Should().Be(CommitPromptKind.Revert);
        await head.Actions.ConfirmPromptAsync();
        head.Actions.IsOpen.Should().BeFalse(head.Actions.DialogError);
        await WaitUntilAsync(() => repository.GitOutput("log", "--format=%s", "-1").StartsWith("Revert", StringComparison.Ordinal));
        await WaitUntilAsync(() => viewModel.Commits.Any(c => c.Subject.StartsWith("Revert", StringComparison.Ordinal)));

        CommitItemViewModel first = viewModel.Commits.Single(c => c.Subject == "Add a.txt");
        Click(Find(RevisionGridContextMenu.Build(viewModel, first, null), "Checkout this commit…"));
        viewModel.RevisionOps.IsCheckout.Should().BeTrue();
        viewModel.RevisionOps.Revision.Should().Be(first.ObjectId.ToString());
        await viewModel.RevisionOps.ConfirmAsync();
        await WaitUntilAsync(() => repository.GitOutput("rev-parse", "HEAD").Trim() == first.ObjectId.ToString());
        window.Close();
    }

    [AvaloniaTest]
    public async Task Navigate_entries_move_the_selection()
    {
        using TestRepository repository = await RichRepositoryAsync();
        (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repository);
        CommitItemViewModel head = viewModel.Commits[0];
        viewModel.SelectedCommit = head;

        Click(Find(Find(RevisionGridContextMenu.Build(viewModel, head, null), "Navigate").Items.OfType<Control>(), "Go to parent commit"));
        viewModel.SelectedCommit.Should().Be(viewModel.Commits[1]);
        Click(Find(Find(RevisionGridContextMenu.Build(viewModel, viewModel.Commits[1], null), "Navigate").Items.OfType<Control>(), "Go to child commit"));
        viewModel.SelectedCommit.Should().Be(head);
        window.Close();
    }

    [AvaloniaTest]
    public async Task Open_on_GitHub_is_enabled_only_for_a_GitHub_remote()
    {
        using TestRepository repository = await RichRepositoryAsync();
        (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repository);
        MenuItem github = Find(RevisionGridContextMenu.Build(viewModel, viewModel.Commits[0], null), "Open on GitHub");
        github.IsEnabled.Should().BeFalse();
        viewModel.GitHubWebUrl.Should().BeNull();
        window.Close();

        repository.RunGit("remote", "set-url", "origin", "git@github.com:gitextensions/gitextensions.git");
        (window, viewModel) = await OpenAsync(repository);
        viewModel.GitHubWebUrl.Should().Be("https://github.com/gitextensions/gitextensions");
        Find(RevisionGridContextMenu.Build(viewModel, viewModel.Commits[0], null), "Open on GitHub").IsEnabled.Should().BeTrue();
        window.Close();
    }

    [AvaloniaTest]
    public async Task Right_clicking_a_row_builds_the_menu_for_that_row()
    {
        using TestRepository repository = await RichRepositoryAsync();
        (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repository);
        ListBox list = window.FindControl<RevisionGridView>("RevisionGrid")!.List;
        ListBoxItem row = list.GetVisualDescendants().OfType<ListBoxItem>().First();

        list.ContextMenu.Should().NotBeNull();
        row.RaiseEvent(new ContextRequestedEventArgs());
        Outline(list.ContextMenu!.Items.OfType<Control>()).Should().StartWith("Copy to clipboard|-|Checkout branch…");
        window.Close();
    }

    [AvaloniaTest]
    public async Task Refs_tree_menus_have_icons_and_the_actions_of_each_node_kind()
    {
        using TestRepository repository = await RichRepositoryAsync();
        (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repository);
        RefsTreeViewModel refs = viewModel.Refs;
        FakeClipboard clipboard = new();

        List<Control> local = RefsContextMenu.Build(refs, viewModel, refs.FindNode(RefNodeKind.LocalBranch, "develop")!, clipboard);
        Outline(local).Should().Be(
            "Checkout|Merge into current branch…|Rebase current branch on this…|-|Create branch from here…|Create tag here…|-|Rename…|Delete branch|-|Push|Set upstream…|Unset upstream|-|Copy name");
        Click(Find(local, "Merge into current branch…"));
        await WaitUntilAsync(() => viewModel.MergeDialog.IsOpen);
        viewModel.MergeDialog.Target.Should().Be("develop");
        viewModel.MergeDialog.CloseCommand.Execute(null);
        Find(RefsContextMenu.Build(refs, viewModel, refs.FindNode(RefNodeKind.LocalBranch, "main")!, clipboard), "Checkout").IsEnabled.Should().BeFalse();
        Find(RefsContextMenu.Build(refs, viewModel, refs.FindNode(RefNodeKind.LocalBranch, "main")!, clipboard), "Delete branch").IsEnabled.Should().BeFalse();

        Click(Find(local, "Copy name"));
        clipboard.Text.Should().Be("develop");
        Click(Find(local, "Rename…"));
        refs.PromptKind.Should().Be(RefPromptKind.RenameBranch);
        refs.CancelPromptCommand.Execute(null);

        Click(Find(local, "Checkout"));
        await WaitUntilAsync(() => repository.GitOutput("branch", "--show-current").Trim() == "develop");
        await WaitUntilAsync(() => viewModel.Branch == "develop" && refs.FindNode(RefNodeKind.RemoteBranch, "origin/main") is not null);

        List<Control> remote = RefsContextMenu.Build(refs, viewModel, refs.FindNode(RefNodeKind.RemoteBranch, "origin/main")!, clipboard);
        Outline(remote).Should().Be(
            "Checkout as local tracking branch|Merge into current branch…|Rebase current branch on this…|-|Create branch from here…|Create tag here…|-|Fetch this remote|Delete remote branch…|-|Copy name");

        List<Control> tag = RefsContextMenu.Build(refs, viewModel, refs.FindNode(RefNodeKind.Tag, "v1.0")!, clipboard);
        Outline(tag).Should().Be("Checkout|Create branch from here…|-|Push tag|Delete tag…|-|Copy name");
        Click(Find(tag, "Delete tag…"));
        refs.PromptKind.Should().Be(RefPromptKind.DeleteTag);
        refs.CancelPromptCommand.Execute(null);

        RefNodeViewModel tagsGroup = refs.Roots.Single(r => r.Name == "Tags");
        Outline(RefsContextMenu.Build(refs, viewModel, tagsGroup, clipboard)).Should().Be("Create tag…|Push all tags…");
        RefNodeViewModel remotes = refs.Roots.Single(r => r.Name == "Remotes");
        Outline(RefsContextMenu.Build(refs, viewModel, remotes, clipboard)).Should().Be("Manage remote repositories…|Fetch all");
        Outline(RefsContextMenu.Build(refs, viewModel, remotes.Children[0], clipboard)).Should().Contain("Fetch this remote");

        foreach (MenuItem item in AllItems(local.Concat(remote).Concat(tag)))
        {
            ((item.Icon as AppIcon)?.IconName).Should().NotBeNullOrEmpty(MenuKit.TextOf(item));
        }

        window.Close();
    }

    [AvaloniaTest]
    public void File_list_menu_has_the_original_entries_and_runs_the_list_actions()
    {
        using TestRepository repository = new();
        FakeClipboard clipboard = new();
        List<string> log = [];
        FileMenuContext context = new()
        {
            Paths = ["a.txt"],
            WorkingDir = repository.Path,
            Clipboard = clipboard,
            OpenHistory = (path, blame) => log.Add((blame ? "blame " : "history ") + path),
            Stage = () => log.Add("stage"),
            Reset = () => log.Add("reset"),
            AddToGitIgnore = () => log.Add("ignore"),
        };
        List<Control> menu = FileListContextMenu.Build(context);

        Outline(menu).Should().Be(
            "Open|Open with…|Edit file|Show in folder|-|File history|Blame|-|Stage|Reset file or directory changes…|-|Copy path|-|Add to .gitignore|Stop tracking the file|Delete…");
        Find(menu, "Open").IsEnabled.Should().BeTrue();
        Find(menu, "Open with…").IsEnabled.Should().BeTrue();
        Find(menu, "Edit file").IsEnabled.Should().BeTrue();
        Find(menu, "Delete…").IsEnabled.Should().BeFalse("the list cannot refresh itself after a deletion");
        ToolTip.GetTip(Find(menu, "Delete…")).Should().Be("Only files of the working directory can be deleted");
        Find(menu, "Stage").InputGesture!.ToString().Should().Be("S");

        Click(Find(menu, "File history"));
        Click(Find(menu, "Blame"));
        Click(Find(menu, "Stage"));
        Click(Find(menu, "Reset file or directory changes…"));
        Click(Find(menu, "Add to .gitignore"));
        log.Should().Equal("history a.txt", "blame a.txt", "stage", "reset", "ignore");

        MenuItem copy = Find(menu, "Copy path");
        Click(copy.Items.OfType<MenuItem>().First(i => MenuKit.TextOf(i) == "Full path"));
        clipboard.Text.Should().Be(Path.Combine(repository.Path, "a.txt"));
        Click(copy.Items.OfType<MenuItem>().First(i => MenuKit.TextOf(i) == "Relative path"));
        clipboard.Text.Should().Be("a.txt");

        // A path that is gone from the working directory (a deleted file of an old revision) cannot be opened.
        List<Control> gone = FileListContextMenu.Build(new FileMenuContext { Paths = ["nope/missing.txt"], WorkingDir = repository.Path, OpenHistory = (_, _) => { } });
        Find(gone, "Open").IsEnabled.Should().BeFalse();
        Find(gone, "Show in folder").IsEnabled.Should().BeFalse();
        Find(gone, "Add to .gitignore").IsEnabled.Should().BeFalse();

        // Several files: single-file entries are disabled.
        List<Control> several = FileListContextMenu.Build(new FileMenuContext { Paths = ["a.txt", "b.txt"], WorkingDir = repository.Path, OpenHistory = (_, _) => { } });
        Find(several, "File history").IsEnabled.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Diff_tab_file_list_shows_the_file_menu_for_the_clicked_file()
    {
        using TestRepository repository = await RichRepositoryAsync();
        (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repository);
        viewModel.SelectedCommit = viewModel.Commits[0];
        viewModel.RevisionTabs.SelectedTabIndex = RevisionTabsViewModel.DiffTab;
        await WaitUntilAsync(() => viewModel.RevisionTabs.Diff.Files.Count > 0);
        Pump();
        RevisionDiffView diff = window.GetVisualDescendants().OfType<RevisionDiffView>().First();
        ListBox files = diff.FindControl<ListBox>("FileList")!;
        await WaitUntilAsync(() => files.GetVisualDescendants().OfType<ListBoxItem>().Any());
        ListBoxItem row = files.GetVisualDescendants().OfType<ListBoxItem>().First();

        row.RaiseEvent(new ContextRequestedEventArgs());
        Outline(files.ContextMenu!.Items.OfType<Control>()).Should().StartWith("Open|Open with…|Edit file|Show in folder|-|File history|Blame");
        window.Close();
    }

    [AvaloniaTest]
    public async Task Dashboard_entry_menu_reuses_the_dashboard_commands()
    {
        FakeHistory history = new();
        using TestRepository repository = new();
        history.Recent.Add(new GitCommands.UserRepositoryHistory.Repository(repository.Path));
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker(), history);
        MainWindow window = new() { DataContext = viewModel, Width = 1000, Height = 700 };
        window.Show();
        await viewModel.Dashboard.LoadAsync();
        Pump();
        DashboardViewModel dashboard = viewModel.Dashboard;
        DashboardEntry entry = dashboard.Categories.SelectMany(c => c.Entries).First();
        FakeClipboard clipboard = new();
        dashboard.Clipboard = clipboard;

        List<Control> menu = DashboardContextMenu.Build(dashboard, entry);
        Outline(menu).Should().Be("Open|Open in file explorer|Open in terminal|-|Category…|Pin to top|Add to favorites|-|Remove from list|Copy path");
        foreach (MenuItem item in AllItems(menu))
        {
            ((item.Icon as AppIcon)?.IconName).Should().NotBeNullOrEmpty(MenuKit.TextOf(item));
        }

        Click(Find(menu, "Copy path"));
        await WaitUntilAsync(() => clipboard.Text is not null);
        clipboard.Text.Should().Be(entry.Path);
        Click(Find(menu, "Category…"));
        dashboard.IsEditorOpen.Should().BeTrue();
        window.Close();
    }

    private static void CaptureWindow(Window window, string name)
    {
        Pump();
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, name + ".png");
        window.CaptureRenderedFrame()!.Save(file);
        TestContext.Out.WriteLine(file);
    }

    [AvaloniaTest]
    public async Task Screenshots_of_the_opened_menus_in_both_themes()
    {
        using TestRepository repository = await RichRepositoryAsync();
        foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            string suffix = theme == ThemeVariant.Light ? "light" : "dark";
            (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repository, theme);
            window.Height = 900;
            CommitItemViewModel head = viewModel.Commits[0];
            Control grid = window.FindControl<RevisionGridView>("RevisionGrid")!.List;

            // The revision grid menu with its View and Copy to clipboard submenus open, built through the real right-click path.
            ListBoxItem row = grid.GetVisualDescendants().OfType<ListBoxItem>().First();
            row.RaiseEvent(new ContextRequestedEventArgs());
            Pump();
            ContextMenu menu = grid.ContextMenu!;
            CaptureWindow(window, "ctx-grid-" + suffix);
            MenuItem view = menu.Items.OfType<MenuItem>().First(i => MenuKit.TextOf(i) == "View");
            view.IsSubMenuOpen = true;
            Pump();
            CaptureWindow(window, "ctx-grid-view-" + suffix);
            view.IsSubMenuOpen = false;
            MenuItem copy = menu.Items.OfType<MenuItem>().First(i => MenuKit.TextOf(i) == "Copy to clipboard");
            copy.IsSubMenuOpen = true;
            Pump();
            CaptureWindow(window, "ctx-grid-copy-" + suffix);
            menu.Close();

            // The left refs tree: a local branch.
            TreeView tree = window.GetVisualDescendants().OfType<TreeView>().First(t => t.Name == "RefsTree");
            RefNodeViewModel node = viewModel.Refs.FindNode(RefNodeKind.LocalBranch, "develop")!;
            viewModel.Refs.SelectedNode = node;
            Pump();
            TreeViewItem item = tree.GetVisualDescendants().OfType<TreeViewItem>().First(t => ReferenceEquals(t.DataContext, node));
            item.RaiseEvent(new ContextRequestedEventArgs());
            Pump();
            CaptureWindow(window, "ctx-refs-" + suffix);
            tree.ContextMenu!.Close();

            // The Diff tab file list.
            viewModel.SelectedCommit = head;
            viewModel.RevisionTabs.SelectedTabIndex = RevisionTabsViewModel.DiffTab;
            await WaitUntilAsync(() => viewModel.RevisionTabs.Diff.Files.Count > 0);
            Pump();
            ListBox files = window.GetVisualDescendants().OfType<RevisionDiffView>().First().FindControl<ListBox>("FileList")!;
            await WaitUntilAsync(() => files.GetVisualDescendants().OfType<ListBoxItem>().Any());
            files.GetVisualDescendants().OfType<ListBoxItem>().First().RaiseEvent(new ContextRequestedEventArgs());
            Pump();
            CaptureWindow(window, "ctx-files-" + suffix);
            files.ContextMenu!.Close();
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Screenshots_of_the_commit_window_menus()
    {
        using TestRepository repository = new();
        repository.RunGit("config", "user.name", "Committer");
        repository.RunGit("config", "user.email", "committer@example.com");
        foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            string suffix = theme == ThemeVariant.Light ? "light" : "dark";
            MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
            await main.OpenAsync(repository.Path);
            CommitWindowViewModel vm = main.CreateCommitWindowViewModel(new CommitWindowStore(Path.Combine(Path.GetTempPath(), "ge-ctx-" + Guid.NewGuid().ToString("N") + ".json")))!;
            await vm.LoadAsync();
            CommitWindow window = new() { DataContext = vm, Width = 1000, Height = 700, RequestedThemeVariant = theme };
            window.Show();
            Pump();

            ListBox unstaged = window.FindControl<ListBox>("UnstagedList")!;
            unstaged.SelectedIndex = 0;
            Pump();
            unstaged.GetVisualDescendants().OfType<ListBoxItem>().First().RaiseEvent(new ContextRequestedEventArgs());
            Pump();
            Outline(unstaged.ContextMenu!.Items.OfType<Control>()).Should().Contain("Stage").And.Contain("Add to .gitignore").And.Contain("File history");
            CaptureWindow(window, "ctx-commitfiles-" + suffix);
            unstaged.ContextMenu.Close();

            TextBox message = window.FindControl<TextBox>("MessageBox")!;
            message.Text = "Draft message";
            message.RaiseEvent(new ContextRequestedEventArgs());
            Pump();
            Outline(message.ContextMenu!.Items.OfType<Control>()).Should().Contain("Commit message templates").And.Contain("Clear message");
            CaptureWindow(window, "ctx-commitmessage-" + suffix);
            message.ContextMenu.Close();
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Commit_window_file_menu_stages_and_ignores_through_the_existing_commands()
    {
        using TestRepository repository = new();
        repository.RunGit("config", "user.name", "Committer");
        repository.RunGit("config", "user.email", "committer@example.com");
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(repository.Path);
        CommitWindowViewModel vm = main.CreateCommitWindowViewModel(new CommitWindowStore(Path.Combine(Path.GetTempPath(), "ge-ctx-" + Guid.NewGuid().ToString("N") + ".json")))!;
        await vm.LoadAsync();
        CommitWindow window = new() { DataContext = vm, Width = 1000, Height = 700 };
        window.Show();
        Pump();

        ListBox unstaged = window.FindControl<ListBox>("UnstagedList")!;
        unstaged.SelectedItem = vm.Unstaged.Single(f => f.Name == "c.txt");
        Pump();
        unstaged.GetVisualDescendants().OfType<ListBoxItem>().First(i => ((ChangedFileViewModel)i.DataContext!).Name == "c.txt").RaiseEvent(new ContextRequestedEventArgs());
        Pump();
        List<Control> menu = [.. unstaged.ContextMenu!.Items.OfType<Control>()];
        Find(menu, "Add to .gitignore").IsEnabled.Should().BeTrue();
        Click(Find(menu, "Add to .gitignore"));
        await WaitUntilAsync(() => File.Exists(Path.Combine(repository.Path, ".gitignore")));
        File.ReadAllText(Path.Combine(repository.Path, ".gitignore")).Should().Contain("/c.txt");
        unstaged.ContextMenu.Close();

        await WaitUntilAsync(() => vm.Unstaged.All(f => f.Name != "c.txt"));
        unstaged.SelectedItem = vm.Unstaged.Single(f => f.Name == "a.txt");
        Pump();
        unstaged.GetVisualDescendants().OfType<ListBoxItem>().First(i => ((ChangedFileViewModel)i.DataContext!).Name == "a.txt").RaiseEvent(new ContextRequestedEventArgs());
        menu = [.. unstaged.ContextMenu.Items.OfType<Control>()];
        Find(menu, "Add to .gitignore").IsEnabled.Should().BeFalse("a.txt is tracked");
        Click(Find(menu, "Stage"));
        await WaitUntilAsync(() => vm.Staged.Any(f => f.Name == "a.txt"));
        repository.GitOutput("diff", "--cached", "--name-only").Trim().Should().Contain("a.txt");
        unstaged.ContextMenu.Close();
        window.Close();
    }

    [AvaloniaTest]
    public async Task Diff_menu_stages_the_selected_lines_and_the_file()
    {
        using TestRepository repository = new();
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(repository.Path);
        CommitWindowViewModel vm = main.CreateCommitWindowViewModel(new CommitWindowStore(Path.Combine(Path.GetTempPath(), "ge-ctx-" + Guid.NewGuid().ToString("N") + ".json")))!;
        await vm.LoadAsync();
        vm.SelectedUnstaged = vm.Unstaged.Single(f => f.Name == "a.txt");
        await WaitUntilAsync(() => vm.Diff.Lines.Any(l => l.IsChange));

        FakeClipboard clipboard = new();
        List<Control> menu = DiffContextMenu.Build(vm.Diff, null, clipboard);
        Outline(menu).Should().Be(
            "Copy|Copy the whole diff|-|Stage selected line(s)|Unstage selected line(s)|Reset selected line(s)|-|Stage hunk|Unstage hunk|Discard hunk…|-|Stage file|Unstage file|-|Copy file path|-|Next change|Previous change|Show entire file|Ignore whitespace changes");
        Find(menu, "Copy").IsEnabled.Should().BeFalse();
        Find(menu, "Stage file").IsEnabled.Should().BeTrue();
        Find(menu, "Unstage file").IsEnabled.Should().BeFalse();
        Find(menu, "Next change").IsEnabled.Should().BeFalse();

        Click(Find(menu, "Copy the whole diff"));
        clipboard.Text.Should().Contain("changed");
        Click(Find(menu, "Copy file path"));
        clipboard.Text.Should().Be("a.txt");

        GitExtensions.Desktop.Diff.DiffLine hunkRow = vm.Diff.Lines.First(l => l.HunkIndex >= 0 && l.Kind != GitExtensions.Desktop.Diff.DiffLineKind.Hunk);
        List<Control> hunkMenu = DiffContextMenu.Build(vm.Diff, hunkRow, clipboard);
        Find(hunkMenu, "Stage hunk").IsEnabled.Should().BeTrue();
        Click(Find(hunkMenu, "Stage hunk"));
        await WaitUntilAsync(() => repository.GitOutput("diff", "--cached", "--name-only").Contains("a.txt", StringComparison.Ordinal));
    }

}

[TestFixture]
public class GitHubRemoteTests
{
    [TestCase("https://github.com/gitextensions/gitextensions.git", "https://github.com/gitextensions/gitextensions")]
    [TestCase("https://github.com/gitextensions/gitextensions", "https://github.com/gitextensions/gitextensions")]
    [TestCase("git@github.com:owner/repo.git", "https://github.com/owner/repo")]
    [TestCase("ssh://git@github.com/owner/repo.git", "https://github.com/owner/repo")]
    [TestCase("https://user@github.com/owner/repo/", "https://github.com/owner/repo")]
    [TestCase("https://gitlab.com/owner/repo.git", null)]
    [TestCase("https://example.invalid/repo.git", null)]
    [TestCase("https://notgithub.com/owner/repo.git", null)]
    [TestCase(null, null)]
    public void Web_url_is_found_only_for_GitHub(string? remote, string? expected)
        => GitExtensions.Desktop.Services.GitHubRemote.WebUrl(remote).Should().Be(expected);

    [Test]
    public void Origin_is_preferred_and_other_remotes_are_used_when_origin_is_not_on_GitHub()
    {
        string config = "[core]\n\tbare = false\n[remote \"upstream\"]\n\turl = git@github.com:up/stream.git\n[remote \"origin\"]\n\turl = https://github.com/me/fork.git\n";
        GitExtensions.Desktop.Services.GitHubRemote.WebUrlFromConfig(config).Should().Be("https://github.com/me/fork");
        GitExtensions.Desktop.Services.GitHubRemote.WebUrlFromConfig("[remote \"origin\"]\n\turl = https://gitlab.com/a/b.git\n[remote \"gh\"]\n\turl = https://github.com/a/b\n")
            .Should().Be("https://github.com/a/b");
        GitExtensions.Desktop.Services.GitHubRemote.WebUrlFromConfig("[remote \"origin\"]\n\turl = https://gitlab.com/a/b.git\n").Should().BeNull();
        GitExtensions.Desktop.Services.GitHubRemote.CommitUrl("https://github.com/a/b", "abc").Should().Be("https://github.com/a/b/commit/abc");
    }
}
