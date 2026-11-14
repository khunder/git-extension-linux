using Avalonia.Headless.NUnit;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class RefsTreeViewModelTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static MainWindowViewModel CreateViewModel() => new(GitEnvironment.Initialize(), new NoFolderPicker());

    private static IEnumerable<RefNodeViewModel> Flatten(IEnumerable<RefNodeViewModel> nodes)
        => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    [AvaloniaTest]
    public async Task Open_populates_branches_remotes_and_tags()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.OpenAsync(repository.Path);

        Flatten(viewModel.Refs.Roots).Where(n => n.IsLeaf).Select(n => (n.Kind, n.FullName)).Should().BeEquivalentTo(
        [
            (RefNodeKind.LocalBranch, "main"),
            (RefNodeKind.LocalBranch, "develop"),
            (RefNodeKind.LocalBranch, "feature/x"),
            (RefNodeKind.RemoteBranch, "origin/main"),
            (RefNodeKind.RemoteBranch, "origin/feature/y"),
            (RefNodeKind.Tag, "v1.0"),
            (RefNodeKind.Tag, "release/2.0"),
        ]);
        Flatten(viewModel.Refs.Roots).Single(n => n.IsCurrent).FullName.Should().Be("main");
    }

    [AvaloniaTest]
    public async Task Filter_narrows_the_tree_live()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        MainWindowViewModel viewModel = CreateViewModel();
        await viewModel.OpenAsync(repository.Path);

        viewModel.Refs.Filter = "feature";

        Flatten(viewModel.Refs.Roots).Where(n => n.IsLeaf).Select(n => n.FullName).Should().BeEquivalentTo("feature/x", "origin/feature/y");

        viewModel.Refs.Filter = string.Empty;
        Flatten(viewModel.Refs.Roots).Count(n => n.IsLeaf).Should().Be(7);
    }

    [AvaloniaTest]
    public async Task Selecting_a_branch_selects_its_commit_in_the_history()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        MainWindowViewModel viewModel = CreateViewModel();
        await viewModel.OpenAsync(repository.Path);

        viewModel.Refs.SelectedNode = Flatten(viewModel.Refs.Roots).Single(n => n.FullName == "feature/x");

        viewModel.SelectedCommit.Should().BeSameAs(viewModel.Commits[0]);
    }

    [AvaloniaTest]
    public async Task Checkout_switches_the_branch_and_refreshes()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        MainWindowViewModel viewModel = CreateViewModel();
        await viewModel.OpenAsync(repository.Path);

        await viewModel.Refs.CheckoutCommand.ExecuteAsync(Flatten(viewModel.Refs.Roots).Single(n => n.FullName == "feature/x"));

        viewModel.Branch.Should().Be("feature/x");
        Flatten(viewModel.Refs.Roots).Single(n => n.IsCurrent).FullName.Should().Be("feature/x");
        viewModel.Commits.Should().NotBeEmpty();
        viewModel.ChangedFiles.Should().HaveCount(2, "uncommitted changes carry over");
    }

    [AvaloniaTest]
    public async Task Checkout_of_a_tag_detaches_head()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        MainWindowViewModel viewModel = CreateViewModel();
        await viewModel.OpenAsync(repository.Path);

        await viewModel.Refs.CheckoutCommand.ExecuteAsync(Flatten(viewModel.Refs.Roots).Single(n => n.FullName == "v1.0"));

        viewModel.Branch.Should().Be("(no branch)");
        Flatten(viewModel.Refs.Roots).Should().NotContain(n => n.IsCurrent);
    }

    [AvaloniaTest]
    public async Task Failed_checkout_reports_the_error_in_the_status()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        MainWindowViewModel viewModel = CreateViewModel();
        await viewModel.OpenAsync(repository.Path);

        // Same file modified on another branch and locally: git refuses to switch.
        repository.RunGit("-c", "user.name=T", "-c", "user.email=t@e", "stash");
        repository.RunGit("checkout", "--quiet", "develop");
        System.IO.File.WriteAllText(System.IO.Path.Combine(repository.Path, "a.txt"), "on develop");
        repository.RunGit("-c", "user.name=T", "-c", "user.email=t@e", "commit", "--quiet", "-am", "develop change");
        repository.RunGit("checkout", "--quiet", "main");
        System.IO.File.WriteAllText(System.IO.Path.Combine(repository.Path, "a.txt"), "dirty on main");
        await viewModel.OpenAsync(repository.Path);

        await viewModel.Refs.CheckoutCommand.ExecuteAsync(Flatten(viewModel.Refs.Roots).Single(n => n.FullName == "develop"));

        viewModel.Status.Should().Contain("failed");
        viewModel.Branch.Should().Be("main");
    }
}
