using Avalonia.Headless.NUnit;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class FileDiffViewModelTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private static async Task<MainWindowViewModel> OpenAsync(TestRepository repository)
    {
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await viewModel.OpenAsync(repository.Path);
        return viewModel;
    }

    private static ChangedFileViewModel Find(MainWindowViewModel viewModel, string name, bool staged)
        => viewModel.ChangedFiles.Single(f => f.Name == name && f.IsStaged == staged);

    [AvaloniaTest]
    public async Task Unstaged_file_shows_working_tree_diff()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = await OpenAsync(repository);

        await viewModel.Diff!.ShowAsync(Find(viewModel, "a.txt", staged: false));

        viewModel.Diff.Lines.Should().Contain(l => l.Kind == DiffLineKind.Removed && l.Text == "-first");
        viewModel.Diff.Lines.Should().Contain(l => l.Kind == DiffLineKind.Added && l.Text == "+changed");
        viewModel.Diff.Lines.Should().Contain(l => l.Kind == DiffLineKind.Hunk);
        viewModel.Diff.Message.Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Untracked_file_shows_content_as_additions()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = await OpenAsync(repository);

        await viewModel.Diff!.ShowAsync(Find(viewModel, "c.txt", staged: false));

        viewModel.Diff.Lines.Where(l => l.Kind == DiffLineKind.Added).Select(l => l.Text).Should().Equal("+new");
    }

    [AvaloniaTest]
    public async Task Stage_moves_the_file_into_the_index_and_shows_the_staged_diff()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = await OpenAsync(repository);
        viewModel.SelectedChangedFile = Find(viewModel, "a.txt", staged: false);
        await viewModel.Diff!.ShowAsync(viewModel.SelectedChangedFile);

        viewModel.Diff.StageCommand.CanExecute(null).Should().BeTrue();
        viewModel.Diff.UnstageCommand.CanExecute(null).Should().BeFalse();
        await viewModel.Diff.StageCommand.ExecuteAsync(null);

        repository.GitOutput("status", "--porcelain").Should().Contain("M  a.txt");
        viewModel.ChangedFiles.Should().Contain(f => f.Name == "a.txt" && f.IsStaged);
        viewModel.SelectedChangedFile!.Name.Should().Be("a.txt");

        // Selection restoration triggers a background diff load; load the staged diff explicitly.
        await viewModel.Diff.ShowAsync(Find(viewModel, "a.txt", staged: true));
        viewModel.Diff.Lines.Should().Contain(l => l.Kind == DiffLineKind.Added && l.Text == "+changed");
        viewModel.Diff.StageCommand.CanExecute(null).Should().BeFalse();
        viewModel.Diff.UnstageCommand.CanExecute(null).Should().BeTrue();
    }

    [AvaloniaTest]
    public async Task Unstage_moves_the_file_back_to_the_working_tree()
    {
        using TestRepository repository = new();
        repository.GitOutput("add", "a.txt");
        MainWindowViewModel viewModel = await OpenAsync(repository);
        await viewModel.Diff!.ShowAsync(Find(viewModel, "a.txt", staged: true));

        await viewModel.Diff.UnstageCommand.ExecuteAsync(null);

        repository.GitOutput("status", "--porcelain").Should().Contain(" M a.txt").And.NotContain("M  a.txt");
        viewModel.ChangedFiles.Should().Contain(f => f.Name == "a.txt" && !f.IsStaged);
    }

    [AvaloniaTest]
    public async Task Stage_all_and_unstage_all_change_every_file()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = await OpenAsync(repository);

        await viewModel.Diff!.StageAllCommand.ExecuteAsync(null);

        string staged = repository.GitOutput("status", "--porcelain");
        staged.Should().Contain("M  a.txt").And.Contain("A  c.txt");
        viewModel.ChangedFiles.Should().OnlyContain(f => f.IsStaged);

        await viewModel.Diff.UnstageAllCommand.ExecuteAsync(null);

        string unstaged = repository.GitOutput("status", "--porcelain");
        unstaged.Should().Contain(" M a.txt").And.Contain("?? c.txt");
        viewModel.ChangedFiles.Should().OnlyContain(f => !f.IsStaged);
    }

    [AvaloniaTest]
    public async Task Selecting_a_file_in_the_main_view_model_loads_its_diff()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = await OpenAsync(repository);

        viewModel.SelectedChangedFile = Find(viewModel, "a.txt", staged: false);
        for (int i = 0; i < 100 && viewModel.Diff!.Lines.Count == 0; i++)
        {
            await Task.Delay(50);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        viewModel.Diff!.Lines.Should().Contain(l => l.Text == "+changed");
    }
}
