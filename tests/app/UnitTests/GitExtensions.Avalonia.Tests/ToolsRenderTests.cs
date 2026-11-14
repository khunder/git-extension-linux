using Avalonia;
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
    public async Task Tools_dialogs_render_in_light_and_dark()
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

            // Repository > Show reflog… opens the Reflog dialog.
            viewModel.ShowReflogCommand.Execute(null);
            ShellTestSupport.Settle();
            DialogWindow reflog = DialogWindow.Open.Should().ContainSingle().Which;
            reflog.Title.Should().Be("Reflog");
            reflog.Find<Button>("CloseButton").Should().NotBeNull();

            // The dialog loads the reflog, with an entry selected and the hard reset prompt open.
            await ReflogTests.WaitAsync(() => viewModel.Tools.Reflog.Entries.Count >= 4);
            viewModel.Tools.Reflog.SelectedEntry = viewModel.Tools.Reflog.Entries.First(e => e.Message.Contains("Fourth", StringComparison.Ordinal));
            await ReflogTests.WaitAsync(() => viewModel.Tools.Reflog.Details.HasCommit);
            viewModel.Tools.Reflog.RequestHardResetCommand.Execute(null);
            Capture(reflog, "reflog");
            viewModel.Tools.Reflog.CloseCommand.Execute(null);
            ShellTestSupport.Settle();
            DialogWindow.Open.Should().BeEmpty();

            viewModel.ShowSubmodulesCommand.Execute(null);
            ShellTestSupport.Settle();
            DialogWindow submodules = DialogWindow.Open.Should().ContainSingle().Which;
            submodules.Title.Should().Be("Submodules");
            await ReflogTests.WaitAsync(() => viewModel.Tools.Submodules.Items.Count == 1);
            viewModel.Tools.Submodules.SelectedSubmodule = viewModel.Tools.Submodules.Items[0];
            Capture(submodules, "submodules");
            viewModel.Tools.Submodules.CloseCommand.Execute(null);
            ShellTestSupport.Settle();

            viewModel.ShowWorktreesCommand.Execute(null);
            ShellTestSupport.Settle();
            DialogWindow worktreesDialog = DialogWindow.Open.Should().ContainSingle().Which;
            worktreesDialog.Title.Should().Be("Worktrees");
            await ReflogTests.WaitAsync(() => viewModel.Tools.Worktrees.Items.Count == 4);
            viewModel.Tools.Worktrees.SelectedWorktree = viewModel.Tools.Worktrees.Items[1];
            viewModel.Tools.Worktrees.RequestRemoveCommand.Execute(null);
            Capture(worktreesDialog, "worktrees");

            viewModel.Tools.Worktrees.CancelPromptCommand.Execute(null);
            viewModel.Tools.Worktrees.RequestAddCommand.Execute(null);
            Capture(worktreesDialog, "worktrees-add");
            viewModel.Tools.Worktrees.CloseCommand.Execute(null);
            ShellTestSupport.Settle();
            DialogWindow.Open.Should().BeEmpty();

            // Tools > Command log.
            viewModel.ShowCommandLogCommand.Execute(null);
            ShellTestSupport.Settle();
            DialogWindow log = DialogWindow.Open.Should().ContainSingle().Which;
            log.Find<ItemsControl>("LogLines").ItemCount.Should().BeGreaterThan(0);
            Capture(log, "command-log");
            viewModel.CommandLogDialog.CloseCommand.Execute(null);
            ShellTestSupport.Settle();

            window.Close();
        }
        finally
        {
            DeleteFolder(worktrees);
        }
    }

    private static void Capture(Window window, string name)
    {
        foreach (ThemeVariant variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Application.Current!.RequestedThemeVariant = variant;
            ShellTestSupport.Settle();

            WriteableBitmap? frame = window.CaptureRenderedFrame();
            frame.Should().NotBeNull();
            string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, $"tools-{name}-{variant.Key.ToString()!.ToLowerInvariant()}.png");
            frame!.Save(file);
            TestContext.AddTestAttachment(file);
        }

        Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
    }
}
