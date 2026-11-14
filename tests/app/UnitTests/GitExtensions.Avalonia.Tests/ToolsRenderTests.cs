using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;
using static GitExtensionsDesktopTests.ToolsTestSupport;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class ToolsRenderTests
{
    private sealed class NoFolderPicker : IFolderPicker
    {
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    [AvaloniaTest]
    public async Task Tools_flyout_renders_each_tab_in_light_and_dark()
    {
        using SubmoduleScenario scenario = new();
        string worktrees = MakeTempFolder();
        try
        {
            TestRepository repository = scenario.Super;
            repository.Commit("c.txt", "3", "Third commit");
            repository.Commit("d.txt", "4", "Fourth commit");
            repository.RunGit("reset", "--hard", "HEAD~1");
            repository.RunGit("branch", "topic");
            repository.RunGit("worktree", "add", "--quiet", "-b", "feature/review", Path.Combine(worktrees, "review"));
            repository.RunGit("worktree", "add", "--quiet", "--detach", Path.Combine(worktrees, "bisect"));
            repository.RunGit("worktree", "add", "--quiet", "-b", "old", Path.Combine(worktrees, "old"));
            DeleteFolder(Path.Combine(worktrees, "old"));
            SubmoduleScenario.Commit(scenario.SubmoduleFolder, "Newer lib commit");

            MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoFolderPicker());
            MainWindow window = new() { DataContext = viewModel, Width = 1000, Height = 640 };
            window.Show();
            await viewModel.OpenAsync(repository.Path);

            // Repository > Show reflog… opens the tools flyout (anchored at the toolbar) on the reflog tab.
            ToolsMenuButton button = (ToolsMenuButton)window.GetVisualDescendants().First(c => c is ToolsMenuButton);
            viewModel.ShowReflogCommand.Execute(null);
            button.Flyout!.IsOpen.Should().BeTrue();
            RepositoryToolsView view = button.View;

            // Reflog tab (the flyout opening loads the selected tab), with an entry selected and the hard reset prompt open.
            await ReflogTests.WaitAsync(() => viewModel.Tools.Reflog.Entries.Count >= 4);
            viewModel.Tools.Reflog.SelectedEntry = viewModel.Tools.Reflog.Entries.First(e => e.Message.Contains("Fourth", StringComparison.Ordinal));
            await ReflogTests.WaitAsync(() => viewModel.Tools.Reflog.Details.HasCommit);
            viewModel.Tools.Reflog.RequestHardResetCommand.Execute(null);
            Capture(window, "reflog");
            view.FindControl<TabControl>("ToolsTabs")!.SelectedIndex.Should().Be(0);

            viewModel.Tools.SelectedTabIndex = RepositoryToolsViewModel.SubmodulesTab;
            await ReflogTests.WaitAsync(() => viewModel.Tools.Submodules.Items.Count == 1);
            viewModel.Tools.Submodules.SelectedSubmodule = viewModel.Tools.Submodules.Items[0];
            Capture(window, "submodules");

            viewModel.Tools.SelectedTabIndex = RepositoryToolsViewModel.WorktreesTab;
            await ReflogTests.WaitAsync(() => viewModel.Tools.Worktrees.Items.Count == 4);
            viewModel.Tools.Worktrees.SelectedWorktree = viewModel.Tools.Worktrees.Items[1];
            viewModel.Tools.Worktrees.RequestRemoveCommand.Execute(null);
            Capture(window, "worktrees");

            viewModel.Tools.Worktrees.CancelPromptCommand.Execute(null);
            viewModel.Tools.Worktrees.RequestAddCommand.Execute(null);
            Capture(window, "worktrees-add");

            window.Close();
        }
        finally
        {
            DeleteFolder(worktrees);
        }
    }

    private static void Capture(MainWindow window, string name)
    {
        foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            window.RequestedThemeVariant = variant;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            WriteableBitmap? frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, $"tools-{name}-{variant.Key.ToString()!.ToLowerInvariant()}.png");
            frame!.Save(file);
            TestContext.AddTestAttachment(file);
        }
    }
}
