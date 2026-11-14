using Avalonia.Headless.NUnit;
using GitExtensions.Desktop.ViewModels;
using static GitExtensionsDesktopTests.ToolsTestSupport;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class WorktreeTests
{
    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder() => _folder = MakeTempFolder();

    [TearDown]
    public void DeleteFolderAfter() => DeleteFolder(_folder);

    private string Wt(string name) => Path.Combine(_folder, name);

    private static WorktreeInfo Find(WorktreesViewModel vm, string folderName) => vm.Items.Single(w => Path.GetFileName(w.Path) == folderName);

    [AvaloniaTest]
    public async Task Lists_only_the_main_worktree_at_first()
    {
        using TestRepository repository = new();
        Context context = CreateContext(repository);
        WorktreesViewModel vm = new(context.Tools);

        await vm.LoadAsync();

        vm.Items.Should().ContainSingle();
        vm.Items[0].IsMain.Should().BeTrue();
        vm.Items[0].Branch.Should().Be("main");
        vm.Items[0].Flags.Should().Be("main");
        vm.CanRemoveSelected.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Add_worktree_on_a_new_branch_and_on_an_existing_one()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        repository.RunGit("branch", "existing");
        Context context = CreateContext(repository);
        WorktreesViewModel vm = new(context.Tools);
        await vm.LoadAsync();

        (await vm.AddAsync(Wt("wt-new"), "feature/new", newBranch: true)).Should().BeTrue();
        (await vm.AddAsync(Wt("wt-existing"), "existing", newBranch: false)).Should().BeTrue();

        context.Errors.Should().BeEmpty();
        vm.Items.Should().HaveCount(3);
        Find(vm, "wt-new").Branch.Should().Be("feature/new");
        Find(vm, "wt-existing").Branch.Should().Be("existing");
        Find(vm, "wt-new").IsMain.Should().BeFalse();
        File.Exists(Path.Combine(Wt("wt-new"), "a.txt")).Should().BeTrue();
        repository.GitOutput("rev-parse", "--verify", "feature/new").Trim().Should().Be(repository.HeadId());
    }

    [AvaloniaTest]
    public async Task Add_worktree_reports_errors_and_validates_input()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        Context context = CreateContext(repository);
        WorktreesViewModel vm = new(context.Tools);

        (await vm.AddAsync(string.Empty, "x", true)).Should().BeFalse();
        (await vm.AddAsync(Wt("a"), "bad name", true)).Should().BeFalse();
        context.Errors.Should().HaveCount(2);

        // main is checked out in the primary worktree: git refuses a second checkout of it.
        (await vm.AddAsync(Wt("dup"), "main", newBranch: false)).Should().BeFalse();
        context.Errors.Should().HaveCount(3);
        context.Errors[2].Should().Contain("git worktree failed");
    }

    [AvaloniaTest]
    public async Task Relative_path_is_resolved_next_to_the_repository()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        Context context = CreateContext(repository);
        WorktreesViewModel vm = new(context.Tools);
        string name = "ge-rel-" + Guid.NewGuid().ToString("N")[..6];
        string expected = Path.Combine(Path.GetDirectoryName(repository.Path)!, name);
        try
        {
            (await vm.AddAsync(name, "rel-branch", true)).Should().BeTrue();
            Directory.Exists(expected).Should().BeTrue();
        }
        finally
        {
            DeleteFolder(expected);
        }
    }

    [AvaloniaTest]
    public async Task Detached_locked_and_prunable_flags_are_listed()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        repository.RunGit("worktree", "add", "--quiet", "--detach", Wt("det"));
        repository.RunGit("worktree", "add", "--quiet", "-b", "lockme", Wt("locked"));
        repository.RunGit("worktree", "lock", "--reason", "in use", Wt("locked"));
        repository.RunGit("worktree", "add", "--quiet", "-b", "stale", Wt("stale"));
        DeleteFolder(Wt("stale"));
        Context context = CreateContext(repository);
        WorktreesViewModel vm = new(context.Tools);

        await vm.LoadAsync();

        WorktreeInfo detached = Find(vm, "det");
        detached.IsDetached.Should().BeTrue();
        detached.BranchText.Should().Be("(detached)");
        WorktreeInfo locked = Find(vm, "locked");
        locked.IsLocked.Should().BeTrue();
        locked.LockReason.Should().Be("in use");
        locked.Flags.Should().Be("locked");
        WorktreeInfo stale = Find(vm, "stale");
        stale.IsPrunable.Should().BeTrue();
        stale.Flags.Should().Be("prunable");
    }

    [AvaloniaTest]
    public async Task Prune_removes_stale_worktrees()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        repository.RunGit("worktree", "add", "--quiet", "-b", "stale", Wt("stale"));
        DeleteFolder(Wt("stale"));
        Context context = CreateContext(repository);
        WorktreesViewModel vm = new(context.Tools);
        await vm.LoadAsync();
        vm.Items.Should().HaveCount(2);

        (await vm.PruneAsync()).Should().BeTrue();

        vm.Items.Should().ContainSingle().Which.IsMain.Should().BeTrue();
    }

    [AvaloniaTest]
    public async Task Remove_clean_worktree_after_the_inline_confirmation()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        Context context = CreateContext(repository);
        WorktreesViewModel vm = new(context.Tools);
        await vm.AddAsync(Wt("clean"), "clean-branch", true);
        WorktreeInfo worktree = Find(vm, "clean");

        vm.RequestRemoveCommand.Execute(worktree);
        vm.IsRemovePromptVisible.Should().BeTrue();
        vm.RemovePromptText.Should().Contain(worktree.Path);
        Directory.Exists(Wt("clean")).Should().BeTrue("nothing is removed before the confirmation");

        await vm.ConfirmRemoveCommand.ExecuteAsync(null);

        Directory.Exists(Wt("clean")).Should().BeFalse();
        vm.Items.Should().ContainSingle();
        vm.IsRemovePromptVisible.Should().BeFalse();
        repository.GitOutput("branch", "--list", "clean-branch").Should().Contain("clean-branch", "the branch is kept");
    }

    [AvaloniaTest]
    public async Task Remove_dirty_worktree_needs_a_second_force_confirmation()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        Context context = CreateContext(repository);
        WorktreesViewModel vm = new(context.Tools);
        await vm.AddAsync(Wt("dirty"), "dirty-branch", true);
        File.WriteAllText(Path.Combine(Wt("dirty"), "a.txt"), "uncommitted");
        File.WriteAllText(Path.Combine(Wt("dirty"), "new.txt"), "untracked");
        WorktreeInfo worktree = Find(vm, "dirty");

        vm.RequestRemoveCommand.Execute(worktree);
        await vm.ConfirmRemoveCommand.ExecuteAsync(null);

        Directory.Exists(Wt("dirty")).Should().BeTrue("git refuses without --force");
        vm.PromptKind.Should().Be(WorktreePromptKind.ConfirmForceRemove);
        vm.RemovePromptText.Should().Contain("LOST").And.Contain("1 modified/staged").And.Contain("1 untracked");
        vm.RemoveConfirmText.Should().Be("Force remove");
        context.Errors.Should().BeEmpty();

        await vm.ConfirmRemoveCommand.ExecuteAsync(null);

        Directory.Exists(Wt("dirty")).Should().BeFalse();
        vm.Items.Should().ContainSingle();
    }

    [AvaloniaTest]
    public async Task Cancel_keeps_the_worktree_and_main_cannot_be_removed()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        Context context = CreateContext(repository);
        WorktreesViewModel vm = new(context.Tools);
        await vm.AddAsync(Wt("keep"), "keep-branch", true);

        vm.RequestRemoveCommand.Execute(Find(vm, "keep"));
        vm.CancelPromptCommand.Execute(null);
        vm.IsRemovePromptVisible.Should().BeFalse();
        Directory.Exists(Wt("keep")).Should().BeTrue();

        vm.RequestRemoveCommand.Execute(vm.Items.Single(w => w.IsMain));
        vm.IsRemovePromptVisible.Should().BeFalse();
        context.Errors.Should().ContainSingle().Which.Should().Contain("main worktree");
        (await vm.RemoveAsync(vm.Items.Single(w => w.IsMain), force: true)).Should().BeFalse();
        Directory.Exists(repository.Path).Should().BeTrue();
    }

    [AvaloniaTest]
    public async Task Open_calls_back_with_the_worktree_path_and_refuses_a_missing_folder()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        Context context = CreateContext(repository);
        WorktreesViewModel vm = new(context.Tools);
        await vm.AddAsync(Wt("open"), "open-branch", true);
        repository.RunGit("worktree", "add", "--quiet", "-b", "gone", Wt("gone"));
        await vm.LoadAsync();
        DeleteFolder(Wt("gone"));

        (await vm.OpenAsync(Find(vm, "open"))).Should().BeTrue();
        (await vm.OpenAsync(Find(vm, "gone"))).Should().BeFalse();

        context.Opened.Should().ContainSingle().Which.Should().EndWith("open");
        context.Errors.Should().ContainSingle().Which.Should().Contain("does not exist");
    }

    [AvaloniaTest]
    public async Task Host_reloads_the_visible_tab_on_repository_change_and_remembers_the_way_back()
    {
        using TestRepository repository = new();
        repository.CleanWorkingTree();
        Context context = CreateContext(repository);
        List<string> opened = [];
        RepositoryToolsViewModel tools = new(context.Tools, path =>
        {
            opened.Add(path);
            return Task.CompletedTask;
        });
        await tools.Worktrees.AddAsync(Wt("w"), "w-branch", true);
        tools.SelectedTabIndex = RepositoryToolsViewModel.WorktreesTab;
        tools.Open(RepositoryToolsViewModel.WorktreesTab);
        await ReflogTests.WaitAsync(() => tools.Worktrees.Items.Count == 2);

        await tools.OpenFromToolsAsync(Wt("w"));

        opened.Should().Equal(Wt("w"));
        tools.BackPath!.TrimEnd('/').Should().Be(repository.Path.TrimEnd('/'));
        await tools.GoBackCommand.ExecuteAsync(null);
        opened.Select(o => o.TrimEnd('/')).Should().Equal(Wt("w"), repository.Path.TrimEnd('/'));
        tools.HasBackPath.Should().BeFalse();
    }
}
