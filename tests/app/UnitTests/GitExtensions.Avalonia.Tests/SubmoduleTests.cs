using Avalonia.Headless.NUnit;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using static GitExtensionsDesktopTests.ToolsTestSupport;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class SubmoduleTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    [AvaloniaTest]
    public async Task Repository_without_submodules_shows_an_empty_list()
    {
        using TestRepository repository = new();
        Context context = CreateContext(repository);
        SubmodulesViewModel vm = new(context.Tools);

        await vm.LoadAsync();

        vm.Items.Should().BeEmpty();
        vm.EmptyText.Should().Contain("no submodules");
    }

    [AvaloniaTest]
    public async Task States_go_from_up_to_date_to_modified_to_dirty()
    {
        using SubmoduleScenario scenario = new();
        Context context = CreateContext(scenario.Super);
        SubmodulesViewModel vm = new(context.Tools);

        await vm.LoadAsync();

        vm.Items.Should().ContainSingle();
        SubmoduleItemViewModel item = vm.Items[0];
        item.Path.Should().Be("lib");
        item.Name.Should().Be("lib");
        item.Url.Should().Be(scenario.LibPath);
        item.State.Should().Be(SubmoduleState.UpToDate);
        item.StateText.Should().Be("up to date");
        item.Commit.Should().Be(RunIn(scenario.LibPath, "rev-parse", "HEAD").Trim());

        // A new commit inside the submodule: the superproject still records the old one.
        SubmoduleScenario.Commit(scenario.SubmoduleFolder, "Newer lib commit");
        await vm.LoadAsync();
        vm.Items[0].State.Should().Be(SubmoduleState.Modified);

        // Uncommitted changes inside the submodule.
        File.WriteAllText(Path.Combine(scenario.SubmoduleFolder, "lib.txt"), "edited");
        await vm.LoadAsync();
        vm.Items[0].State.Should().Be(SubmoduleState.Dirty);
        vm.Items[0].StateText.Should().Contain("dirty");
    }

    [AvaloniaTest]
    public async Task Missing_folder_and_uninitialized_clone_are_reported()
    {
        using SubmoduleScenario scenario = new();
        string clonePath = scenario.CloneSuper();
        GitModule cloneModule = GitEnvironment.Initialize().OpenRepository(clonePath)!;
        ToolsContext cloneTools = new(() => cloneModule, () => Task.CompletedTask, _ => { }, _ => { }, _ => Task.CompletedTask);
        SubmodulesViewModel vm = new(cloneTools);

        await vm.LoadAsync();
        vm.Items.Should().ContainSingle().Which.State.Should().Be(SubmoduleState.Uninitialized);

        // Initialized in .git/config, then the folder is deleted.
        RunIn(clonePath, "submodule", "update", "--init");
        DeleteFolder(Path.Combine(clonePath, "lib"));
        await vm.LoadAsync();
        vm.Items[0].State.Should().Be(SubmoduleState.Missing);
    }

    [AvaloniaTest]
    public async Task Init_and_update_populate_an_uninitialized_submodule()
    {
        using SubmoduleScenario scenario = new();
        string clonePath = scenario.CloneSuper();
        GitModule cloneModule = GitEnvironment.Initialize().OpenRepository(clonePath)!;
        List<string> errors = [];
        List<string> refreshes = [];
        ToolsContext tools = new(() => cloneModule, () =>
        {
            refreshes.Add("r");
            return Task.CompletedTask;
        }, errors.Add, _ => { }, _ => Task.CompletedTask);
        SubmodulesViewModel vm = new(tools);
        await vm.LoadAsync();
        Directory.Exists(Path.Combine(clonePath, "lib", ".git")).Should().BeFalse();
        File.Exists(Path.Combine(clonePath, "lib", "lib.txt")).Should().BeFalse();

        (await vm.InitAsync(vm.Items[0])).Should().BeTrue();
        RunIn(clonePath, "config", "--get", "submodule.lib.url").Trim().Should().Be(scenario.LibPath);
        vm.Items[0].State.Should().Be(SubmoduleState.Uninitialized, "init only registers it");

        (await vm.UpdateAsync(vm.Items[0])).Should().BeTrue();

        errors.Should().BeEmpty();
        File.Exists(Path.Combine(clonePath, "lib", "lib.txt")).Should().BeTrue();
        vm.Items[0].State.Should().Be(SubmoduleState.UpToDate);
        refreshes.Should().NotBeEmpty();
    }

    [AvaloniaTest]
    public async Task Update_all_and_sync_work_and_failures_reach_the_banner()
    {
        using SubmoduleScenario scenario = new();
        string clonePath = scenario.CloneSuper();
        GitModule cloneModule = GitEnvironment.Initialize().OpenRepository(clonePath)!;
        List<string> errors = [];
        ToolsContext tools = new(() => cloneModule, () => Task.CompletedTask, errors.Add, _ => { }, _ => Task.CompletedTask);
        SubmodulesViewModel vm = new(tools);
        await vm.LoadAsync();

        (await vm.UpdateAllAsync()).Should().BeTrue();
        vm.Items[0].State.Should().Be(SubmoduleState.UpToDate);

        // Point .gitmodules at a new URL and sync it into the repository configuration.
        string moved = Path.Combine(scenario.Root, "lib-moved");
        RunIn(scenario.Root, "clone", "--quiet", scenario.LibPath, moved);
        RunIn(clonePath, "config", "-f", ".gitmodules", "submodule.lib.url", moved);
        (await vm.SyncAsync()).Should().BeTrue();
        RunIn(clonePath, "config", "--get", "submodule.lib.url").Trim().Should().Be(moved);

        // A submodule whose source vanished cannot be updated.
        RunIn(clonePath, "config", "-f", ".gitmodules", "submodule.lib.url", Path.Combine(scenario.Root, "nope"));
        await vm.SyncAsync();
        DeleteFolder(Path.Combine(clonePath, "lib"));
        DeleteFolder(Path.Combine(clonePath, ".git", "modules"));
        (await vm.UpdateAllAsync()).Should().BeFalse();
        errors.Should().NotBeEmpty();
    }

    [AvaloniaTest]
    public async Task Open_as_repository_opens_the_submodule_and_goes_back()
    {
        using SubmoduleScenario scenario = new();
        MainWindowViewModel main = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await main.OpenAsync(scenario.Super.Path);
        RepositoryToolsViewModel tools = main.Tools;
        await tools.Submodules.LoadAsync();
        string superPath = main.RepositoryPath;

        (await tools.Submodules.OpenAsync(tools.Submodules.Items[0])).Should().BeTrue();

        Path.GetFullPath(main.RepositoryPath).TrimEnd('/').Should().Be(Path.GetFullPath(scenario.SubmoduleFolder).TrimEnd('/'));
        main.Commits.Should().NotBeEmpty();
        tools.HasBackPath.Should().BeTrue();
        tools.BackText.Should().StartWith("◂ Back to ");

        await tools.GoBackCommand.ExecuteAsync(null);

        Path.GetFullPath(main.RepositoryPath).TrimEnd('/').Should().Be(Path.GetFullPath(superPath).TrimEnd('/'));
        tools.HasBackPath.Should().BeFalse();
    }

    [AvaloniaTest]
    public async Task Opening_an_uninitialized_submodule_is_refused()
    {
        using SubmoduleScenario scenario = new();
        string clonePath = scenario.CloneSuper();
        GitModule cloneModule = GitEnvironment.Initialize().OpenRepository(clonePath)!;
        List<string> errors = [];
        List<string> opened = [];
        ToolsContext tools = new(() => cloneModule, () => Task.CompletedTask, errors.Add, _ => { }, path =>
        {
            opened.Add(path);
            return Task.CompletedTask;
        });
        SubmodulesViewModel vm = new(tools);
        await vm.LoadAsync();

        (await vm.OpenAsync(vm.Items[0])).Should().BeFalse();

        opened.Should().BeEmpty();
        errors.Should().ContainSingle().Which.Should().Contain("Initialize");
    }
}
