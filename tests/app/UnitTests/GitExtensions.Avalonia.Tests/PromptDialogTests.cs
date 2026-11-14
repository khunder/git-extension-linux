using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

/// <summary>The prompts of the commit context menu, the refs tree and the notes are modal dialog windows.</summary>
[TestFixture]
public class PromptDialogTests
{
    [SetUp]
    public void CloseLeakedDialogs()
    {
        foreach (DialogWindow leaked in DialogWindow.Open.ToList())
        {
            leaked.Close();
        }
    }

    private static async Task<(MainWindow Window, MainWindowViewModel ViewModel)> OpenAsync(TestRepository repo)
    {
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoPicker(), new FakeHistory());
        MainWindow window = new() { DataContext = viewModel, Width = 1100, Height = 760 };
        window.Show();
        await viewModel.OpenAsync(repo.Path);
        ShellTestSupport.Settle();
        return (window, viewModel);
    }

    private static void Capture(DialogWindow dialog, string name)
    {
        foreach ((ThemeVariant variant, string suffix) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
        {
            Application.Current!.RequestedThemeVariant = variant;
            ShellTestSupport.Settle();
            string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(directory);
            dialog.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"prompt-{name}-{suffix}.png"));
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(20);
            ShellTestSupport.Settle();
        }

        condition().Should().BeTrue();
    }

    [AvaloniaTest]
    public async Task Commit_prompts_open_titled_dialogs_show_errors_inline_and_close_on_success()
    {
        using TestRepository repo = new();
        repo.RunGit("stash", "--include-untracked");
        (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repo);
        try
        {
            CommitItemViewModel head = viewModel.Commits[0];
            CommitActionsViewModel actions = viewModel.Actions;
            actions.RequestCreateBranchCommand.Execute(head);
            ShellTestSupport.Settle();
            DialogWindow dialog = DialogWindow.Open.Should().ContainSingle().Which;
            dialog.Title.Should().Be("Create branch");
            actions.InputText = "bad name";
            await actions.ConfirmPromptAsync();
            ShellTestSupport.Settle();
            actions.IsOpen.Should().BeTrue("a failure stays in the dialog");
            dialog.Find<TextBlock>("PromptError").Text.Should().Contain("not a valid name");
            actions.InputText = "topic";
            Capture(dialog, "create-branch");
            await actions.ConfirmPromptAsync();
            ShellTestSupport.Settle();
            actions.IsOpen.Should().BeFalse();
            DialogWindow.Open.Should().BeEmpty();
            repo.GitOutput("branch", "--list", "topic").Should().Contain("topic");

            actions.RequestCreateTagCommand.Execute(head);
            ShellTestSupport.Settle();
            dialog = DialogWindow.Open.Should().ContainSingle().Which;
            dialog.Title.Should().Be("Create tag");
            actions.InputText = "v1.0";
            await actions.ConfirmPromptAsync();
            actions.IsOpen.Should().BeTrue("git refuses an existing tag; the error is shown in the dialog");
            actions.DialogError.Should().Contain("already exists");
            Capture(dialog, "create-tag-error");
            dialog.Find<Button>("PromptCancel").Command!.Execute(null);
            ShellTestSupport.Settle();
            DialogWindow.Open.Should().BeEmpty();

            actions.RequestHardResetCommand.Execute(viewModel.Commits[1]);
            ShellTestSupport.Settle();
            dialog = DialogWindow.Open.Should().ContainSingle().Which;
            dialog.Title.Should().Be("Reset current branch");
            actions.IsHardResetMode.Should().BeTrue();
            dialog.Find<RadioButton>("PromptResetHard").IsChecked.Should().BeTrue();
            actions.IsSoftResetMode = true;
            actions.ConfirmText.Should().Be("Reset --soft");
            Capture(dialog, "reset");
            await actions.ConfirmPromptAsync();
            ShellTestSupport.Settle();
            DialogWindow.Open.Should().BeEmpty();
            repo.GitOutput("rev-parse", "HEAD").Trim().Should().Be(viewModel.Commits.First(c => c.Subject == "Add a.txt").ObjectId.ToString());

            // Closing the window (title bar / Escape) cancels the prompt.
            actions.RequestRevertCommand.Execute(viewModel.Commits.First());
            ShellTestSupport.Settle();
            dialog = DialogWindow.Open.Should().ContainSingle().Which;
            dialog.Title.Should().Be("Revert commit");
            dialog.Close();
            ShellTestSupport.Settle();
            actions.IsOpen.Should().BeFalse();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Refs_prompts_open_titled_dialogs()
    {
        using TestRepository repo = new();
        repo.AddBranchesRemoteAndTags();
        repo.RunGit("stash", "--include-untracked");
        (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repo);
        try
        {
            RefsTreeViewModel refs = viewModel.Refs;
            refs.RequestNewBranchCommand.Execute(null);
            ShellTestSupport.Settle();
            DialogWindow dialog = DialogWindow.Open.Should().ContainSingle().Which;
            dialog.Title.Should().Be("Create branch");
            refs.InputText = "develop";
            await refs.ConfirmPromptAsync();
            ShellTestSupport.Settle();
            refs.IsOpen.Should().BeTrue("the name is taken");
            dialog.Find<TextBlock>("RefsPromptError").Text.Should().Contain("already exists");
            Capture(dialog, "refs-new-branch");
            refs.CancelPromptCommand.Execute(null);
            ShellTestSupport.Settle();
            DialogWindow.Open.Should().BeEmpty();

            refs.RequestNewTagCommand.Execute(null);
            ShellTestSupport.Settle();
            dialog = DialogWindow.Open.Should().ContainSingle().Which;
            dialog.Title.Should().Be("Create tag");
            refs.InputText = "v2.0";
            refs.MessageText = "Release two";
            Capture(dialog, "refs-new-tag");
            await refs.ConfirmPromptAsync();
            await WaitUntilAsync(() => !refs.IsOpen);
            DialogWindow.Open.Should().BeEmpty();
            repo.GitOutput("tag", "--list", "v2.0").Should().Contain("v2.0");

            refs.RequestRenameCommand.Execute(FindLeaf(refs.Roots, "develop"));
            ShellTestSupport.Settle();
            dialog = DialogWindow.Open.Should().ContainSingle().Which;
            dialog.Title.Should().Be("Rename branch");
            refs.InputText = "dev";
            await refs.ConfirmPromptAsync();
            await WaitUntilAsync(() => !refs.IsOpen);
            repo.GitOutput("branch", "--list", "dev").Should().Contain("dev");

            refs.RequestDeleteTagCommand.Execute(FindLeaf(refs.Roots, "v2.0"));
            ShellTestSupport.Settle();
            dialog = DialogWindow.Open.Should().ContainSingle().Which;
            dialog.Title.Should().Be("Delete tag");
            Capture(dialog, "refs-delete-tag");
            await refs.ConfirmPromptAsync();
            await WaitUntilAsync(() => !refs.IsOpen);
            repo.GitOutput("tag", "--list", "v2.0").Trim().Should().BeEmpty();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task Notes_dialog_adds_and_deletes_a_note()
    {
        using TestRepository repo = new();
        repo.RunGit("stash", "--include-untracked");
        (MainWindow window, MainWindowViewModel viewModel) = await OpenAsync(repo);
        try
        {
            viewModel.SelectedCommit = viewModel.Commits[0];
            CommitDetailsViewModel details = viewModel.CommitDetails;
            await WaitUntilAsync(() => details.HasCommit);
            details.EditNoteCommand.Execute(null);
            ShellTestSupport.Settle();
            DialogWindow dialog = DialogWindow.Open.Should().ContainSingle().Which;
            dialog.Title.Should().Be("Commit notes");
            details.NoteDraft = "Remember this";
            await details.SaveNoteCommand.ExecuteAsync(null);
            ShellTestSupport.Settle();
            DialogWindow.Open.Should().BeEmpty();
            repo.GitOutput("notes", "show", "HEAD").Trim().Should().Be("Remember this");

            details.RequestDeleteNoteCommand.Execute(null);
            ShellTestSupport.Settle();
            dialog = DialogWindow.Open.Should().ContainSingle().Which;
            dialog.Find<TextBlock>("NoteDeleteConfirm").IsVisible.Should().BeTrue();
            Capture(dialog, "notes-delete");
            await details.ConfirmDeleteNoteCommand.ExecuteAsync(null);
            ShellTestSupport.Settle();
            DialogWindow.Open.Should().BeEmpty();
            repo.GitOutput("notes", "list").Trim().Should().BeEmpty();
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            window.Close();
        }
    }

    private static RefNodeViewModel FindLeaf(IEnumerable<RefNodeViewModel> nodes, string fullName)
    {
        foreach (RefNodeViewModel node in nodes)
        {
            if (node.IsLeaf && node.FullName == fullName)
            {
                return node;
            }

            if (node.Children.Count > 0 && FindLeaf(node.Children, fullName) is { } found)
            {
                return found;
            }
        }

        return null!;
    }
}
