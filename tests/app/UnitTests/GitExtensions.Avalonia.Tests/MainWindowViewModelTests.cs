using Avalonia.Headless.NUnit;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class MainWindowViewModelTests
{
    private sealed class FakeFolderPicker(string? result) : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult(result);
    }

    private static MainWindowViewModel CreateViewModel(string? pickedFolder = null)
        => new(GitEnvironment.Initialize(), new FakeFolderPicker(pickedFolder));

    [AvaloniaTest]
    public async Task OpenAsync_loads_branch_history_and_changes()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.OpenAsync(repository.Path);

        viewModel.Branch.Should().Be("main");
        viewModel.IsBusy.Should().BeFalse();
        viewModel.Commits.Select(c => c.Subject).Should().Equal("Add b.txt", "Add a.txt");
        viewModel.Commits[0].Refs.Should().Contain("v1.0").And.Contain("main");
        viewModel.Commits[0].Author.Should().Be("Test Author");
        viewModel.ChangedFiles.Select(f => (f.Name, f.Status)).Should().BeEquivalentTo(
            [("a.txt", "M"), ("c.txt", "A")]);
        viewModel.Status.Should().Contain("2 commits").And.Contain("2 changed files");
    }

    [AvaloniaTest]
    public async Task OpenAsync_finds_the_repository_from_a_subfolder()
    {
        using TestRepository repository = new();
        string subfolder = Directory.CreateDirectory(Path.Combine(repository.Path, "src", "deep")).FullName;
        MainWindowViewModel viewModel = CreateViewModel();

        await viewModel.OpenAsync(subfolder);

        viewModel.Branch.Should().Be("main");
        Path.TrimEndingDirectorySeparator(viewModel.RepositoryPath).Should().Be(repository.Path);
    }

    [AvaloniaTest]
    public async Task OpenAsync_reports_a_folder_that_is_not_a_repository()
    {
        string folder = Directory.CreateTempSubdirectory("ge-avalonia-empty-").FullName;
        try
        {
            MainWindowViewModel viewModel = CreateViewModel();

            await viewModel.OpenAsync(folder);

            viewModel.Status.Should().Contain("not inside a Git repository");
            viewModel.Commits.Should().BeEmpty();
            viewModel.Branch.Should().BeNull();
            viewModel.IsBusy.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [AvaloniaTest]
    public async Task BrowseCommand_opens_the_picked_folder()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = CreateViewModel(pickedFolder: repository.Path);

        await viewModel.BrowseCommand.ExecuteAsync(null);

        viewModel.Branch.Should().Be("main");
    }

    [AvaloniaTest]
    public async Task BrowseCommand_does_nothing_when_the_picker_is_cancelled()
    {
        MainWindowViewModel viewModel = CreateViewModel(pickedFolder: null);

        await viewModel.BrowseCommand.ExecuteAsync(null);

        viewModel.Branch.Should().BeNull();
        viewModel.Commits.Should().BeEmpty();
    }
}
