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
        window.FindControl<ListBox>("CommitList")!.ItemCount.Should().Be(2);
        window.FindControl<ListBox>("ChangeList")!.ItemCount.Should().Be(2);
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
    public async Task MainWindow_renders_a_selected_diff()
    {
        using TestRepository repository = new();
        File.WriteAllText(Path.Combine(repository.Path, "a.txt"), "changed\nsecond line\n");
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
        MainWindow window = new() { DataContext = viewModel };
        window.Show();
        await viewModel.OpenAsync(repository.Path);

        ChangedFileViewModel file = viewModel.ChangedFiles.Single(f => f.Name == "a.txt");
        viewModel.SelectedChangedFile = file;
        await viewModel.Diff!.ShowAsync(file);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        viewModel.Diff.Lines.Should().NotBeEmpty();
        window.FindControl<ListBox>("ChangeList")!.SelectedItem.Should().BeSameAs(file);

        WriteableBitmap? frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull();
        string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "main-window-diff.png");
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
