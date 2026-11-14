using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class NotesTests
{
    private static TestRepository CreateRepository()
    {
        TestRepository repo = new();
        repo.RunGit("config", "user.name", "T");
        repo.RunGit("config", "user.email", "t@example.com");
        return repo;
    }

    private static async Task<MainWindowViewModel> OpenAsync(TestRepository repo)
    {
        MainWindowViewModel viewModel = new(GitEnvironment.Initialize(), new NoPicker(), new FakeHistory());
        await viewModel.OpenAsync(repo.Path);
        viewModel.SelectedCommit = viewModel.Commits[0];
        await WaitUntilAsync(() => viewModel.CommitDetails.HasCommit);
        return viewModel;
    }

    [AvaloniaTest]
    public async Task Shows_adds_edits_and_deletes_a_note()
    {
        using TestRepository repo = CreateRepository();
        MainWindowViewModel viewModel = await OpenAsync(repo);
        CommitDetailsViewModel details = viewModel.CommitDetails;
        string head = repo.GitOutput("rev-parse", "HEAD").Trim();
        await Task.Delay(100);
        details.HasNote.Should().BeFalse();
        details.NoteButtonText.Should().Be("Add note…");

        details.EditNoteCommand.Execute(null);
        details.IsEditingNote.Should().BeTrue();
        details.NoteDraft = "Reviewed by me\nsecond line";
        await details.SaveNoteCommand.ExecuteAsync(null);
        details.NoteError.Should().BeNull();
        details.IsEditingNote.Should().BeFalse();
        details.Note.Should().Be("Reviewed by me\nsecond line");
        repo.GitOutput("notes", "show", head).Trim().Should().Be("Reviewed by me\nsecond line");
        details.NoteButtonText.Should().Be("Edit note…");

        details.EditNoteCommand.Execute(null);
        details.NoteDraft.Should().Be("Reviewed by me\nsecond line");
        details.NoteDraft = "changed";
        details.CancelNoteCommand.Execute(null);
        details.Note.Should().Be("Reviewed by me\nsecond line");

        details.EditNoteCommand.Execute(null);
        details.NoteDraft = "changed";
        await details.SaveNoteCommand.ExecuteAsync(null);
        repo.GitOutput("notes", "show", head).Trim().Should().Be("changed");

        details.RequestDeleteNoteCommand.Execute(null);
        details.IsConfirmingNoteDelete.Should().BeTrue();
        details.CancelDeleteNoteCommand.Execute(null);
        details.Note.Should().Be("changed");
        details.RequestDeleteNoteCommand.Execute(null);
        await details.ConfirmDeleteNoteCommand.ExecuteAsync(null);
        details.HasNote.Should().BeFalse();
        repo.GitOutput("notes", "list").Trim().Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task An_existing_note_is_shown_and_an_empty_save_removes_it()
    {
        using TestRepository repo = CreateRepository();
        repo.RunGit("notes", "add", "-m", "pre-existing note", "HEAD");
        MainWindowViewModel viewModel = await OpenAsync(repo);
        CommitDetailsViewModel details = viewModel.CommitDetails;
        await WaitUntilAsync(() => details.HasNote);
        details.Note.Should().Be("pre-existing note");

        // Selecting another commit shows its (missing) note, not the previous one.
        viewModel.SelectedCommit = viewModel.Commits[1];
        await WaitUntilAsync(() => details.HasCommit && !details.HasNote);

        viewModel.SelectedCommit = viewModel.Commits[0];
        await WaitUntilAsync(() => details.HasNote);
        details.EditNoteCommand.Execute(null);
        details.NoteDraft = "   ";
        await details.SaveNoteCommand.ExecuteAsync(null);
        details.HasNote.Should().BeFalse();
        repo.GitOutput("notes", "list").Trim().Should().BeEmpty();
    }

    [AvaloniaTest]
    public async Task Note_panel_renders_in_both_themes()
    {
        using TestRepository repo = CreateRepository();
        repo.RunGit("notes", "add", "-m", "Deploy after the freeze", "HEAD");
        MainWindowViewModel viewModel = await OpenAsync(repo);
        await WaitUntilAsync(() => viewModel.CommitDetails.HasNote);
        MainWindow window = new() { DataContext = viewModel, Width = 1100, Height = 760 };
        window.Show();
        try
        {
            ShellTestSupport.Settle();
            window.Find<SelectableTextBlock>("NoteText").Text.Should().Be("Deploy after the freeze");
            window.Find<Button>("NoteEditButton").Content.Should().Be("Edit note…");

            viewModel.CommitDetails.EditNoteCommand.Execute(null);
            ShellTestSupport.Settle();
            window.Find<TextBox>("NoteEditBox").IsVisible.Should().BeTrue();

            foreach ((ThemeVariant variant, string suffix) in new[] { (ThemeVariant.Light, "light"), (ThemeVariant.Dark, "dark") })
            {
                Application.Current!.RequestedThemeVariant = variant;
                ShellTestSupport.Settle();
                string directory = Path.Combine(AppContext.BaseDirectory, "screenshots");
                Directory.CreateDirectory(directory);
                window.CaptureRenderedFrame()!.Save(Path.Combine(directory, $"notes-{suffix}.png"));
            }
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            window.Close();
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
}
