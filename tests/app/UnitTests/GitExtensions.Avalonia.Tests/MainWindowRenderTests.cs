using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class MainWindowRenderTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    [AvaloniaTest]
    public async Task Refs_sidebar_shows_the_counts_in_the_group_headers()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.OpenAsync(repository.Path);
        ShellTestSupport.Settle();

        string[] headers = [.. viewModel.Refs.Roots.Select(r => r.Header)];
        int branches = repository.GitOutput("for-each-ref", "refs/heads").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        int tags = repository.GitOutput("tag").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        headers.Should().Contain($"Branches ({branches})").And.Contain($"Tags ({tags})").And.Contain("Remotes");

        // The headers (with the counts) are what the tree shows.
        TreeView tree = window.Find<TreeView>("RefsTree");
        tree.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain($"Branches ({branches})");
        window.Close();
    }

    [AvaloniaTest]
    public async Task MainWindow_renders_the_opened_repository()
    {
        using TestRepository repository = new();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel };
        window.Show();

        await viewModel.OpenAsync(repository.Path);

        // Headless rendering is driven manually: process pending UI work, then force a render pass.
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        // The visual tree really contains the data (not just the view-model).
        window.Find<ListBox>("CommitList").ItemCount.Should().Be(2);

        // The working tree changes are not part of the main window any more (the Commit window shows them).
        viewModel.ChangedFiles.Count.Should().Be(2);
        window.TryFind<ListBox>("ChangeList").Should().BeNull();
        window.TryFind<DiffView>("DiffPanel").Should().BeNull();
        window.TryFind<CommitView>("CommitPanel").Should().BeNull();
        window.FindControl<TextBlock>("StatusText")!.Text.Should().Contain("2 commits");

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();

        // Leave a screenshot next to the test results so the UI can be inspected without a display.
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "main-window.png");
        frame!.Save(file);
        TestContext.AddTestAttachment(file);

        File.Exists(file).Should().BeTrue();
        window.Close();
    }

    [AvaloniaTest]
    public async Task Changes_host_renders_a_selected_diff()
    {
        using TestRepository repository = new();
        File.WriteAllText(Path.Combine(repository.Path, "a.txt"), "changed\nsecond line\n");
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        await viewModel.OpenAsync(repository.Path);
        Window window = ShellTestSupport.CreateChangesHost(viewModel);

        ChangedFileViewModel file = viewModel.ChangedFiles.Single(f => f.Name == "a.txt");
        viewModel.SelectedChangedFile = file;
        await viewModel.Diff!.ShowAsync(file);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        viewModel.Diff.Lines.Should().NotBeEmpty();
        window.Find<ListBox>("ChangeList").SelectedItem.Should().BeSameAs(file);

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "changes-host-diff.png");
        frame!.Save(path);
        TestContext.AddTestAttachment(path);
        window.Close();
    }

    [AvaloniaTest]
    public async Task MainWindow_renders_the_refs_sidebar()
    {
        using TestRepository repository = new();
        repository.AddBranchesRemoteAndTags();
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel };
        window.Show();

        await viewModel.OpenAsync(repository.Path);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        TreeView tree = window.GetVisualDescendants().OfType<TreeView>().Single();
        tree.GetVisualDescendants().OfType<TreeViewItem>().Count().Should().BeGreaterThan(8);

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "main-window-refs.png");
        frame!.Save(file);
        TestContext.AddTestAttachment(file);
        window.Close();
    }
}
