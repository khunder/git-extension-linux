using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

// Git notes of the shown commit: show, add/edit and delete (kept out of the main file).
public sealed partial class CommitDetailsViewModel
{
    private string? _noteDir;
    private ObjectId? _noteCommit;
    private int _noteVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    [NotifyPropertyChangedFor(nameof(NoteButtonText))]
    private string _note = string.Empty;

    [ObservableProperty]
    private bool _isEditingNote;

    [ObservableProperty]
    private string _noteDraft = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoteError))]
    private string? _noteError;

    [ObservableProperty]
    private bool _isConfirmingNoteDelete;

    public bool HasNote => Note.Length > 0;

    public bool HasNoteError => !string.IsNullOrEmpty(NoteError);

    public string NoteButtonText => HasNote ? "Edit note…" : "Add note…";

    private void ResetNote()
    {
        _noteVersion++;
        _noteDir = null;
        _noteCommit = null;
        Note = string.Empty;
        NoteDraft = string.Empty;
        NoteError = null;
        IsEditingNote = false;
        IsConfirmingNoteDelete = false;
    }

    // Reads the note in the background (a commit without one is the normal case) and shows it when still current.
    private async Task LoadNoteAsync(string workingDir, ObjectId id)
    {
        int version = ++_noteVersion;
        _noteDir = workingDir;
        _noteCommit = id;
        try
        {
            GitCli.Result result = await GitCli.RunAsync(GitCliSettings.GitPath, workingDir, "notes", "show", id.ToString());
            if (version == _noteVersion)
            {
                Note = result.Succeeded ? result.Output : string.Empty;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // No git to ask: the commit simply shows no note.
        }
    }

    [RelayCommand]
    private void EditNote()
    {
        if (_noteCommit is null)
        {
            return;
        }

        NoteDraft = Note;
        NoteError = null;
        IsConfirmingNoteDelete = false;
        IsEditingNote = true;
    }

    [RelayCommand]
    private void CancelNote()
    {
        IsEditingNote = false;
        NoteError = null;
    }

    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        if (_noteDir is not { } dir || _noteCommit is not { } id)
        {
            return;
        }

        string text = NoteDraft.Trim();
        if (text.Length == 0)
        {
            // An empty note is no note.
            await RemoveNoteAsync(dir, id);
            return;
        }

        string file = Path.Combine(Path.GetTempPath(), $"ge-note-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(file, text + "\n");
            GitCli.Result result = await GitCli.RunAsync(GitCliSettings.GitPath, dir, "notes", "add", "-f", "-F", file, id.ToString());
            if (!result.Succeeded)
            {
                NoteError = $"Could not save the note: {result.Error}";
                return;
            }

            if (_noteCommit == id)
            {
                Note = text;
                IsEditingNote = false;
                NoteError = null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            NoteError = $"Could not save the note: {ex.Message}";
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Best effort cleanup of a temp file.
            }
        }
    }

    [RelayCommand]
    private void RequestDeleteNote() => IsConfirmingNoteDelete = HasNote;

    [RelayCommand]
    private void CancelDeleteNote() => IsConfirmingNoteDelete = false;

    [RelayCommand]
    private async Task ConfirmDeleteNoteAsync()
    {
        IsConfirmingNoteDelete = false;
        if (_noteDir is { } dir && _noteCommit is { } id)
        {
            await RemoveNoteAsync(dir, id);
        }
    }

    private async Task RemoveNoteAsync(string dir, ObjectId id)
    {
        try
        {
            GitCli.Result result = await GitCli.RunAsync(GitCliSettings.GitPath, dir, "notes", "remove", "--ignore-missing", id.ToString());
            if (!result.Succeeded)
            {
                NoteError = $"Could not remove the note: {result.Error}";
                return;
            }

            if (_noteCommit == id)
            {
                Note = string.Empty;
                IsEditingNote = false;
                NoteError = null;
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            NoteError = $"Could not remove the note: {ex.Message}";
        }
    }
}
