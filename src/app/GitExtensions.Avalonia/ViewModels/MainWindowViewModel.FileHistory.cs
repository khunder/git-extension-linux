using CommunityToolkit.Mvvm.Input;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

// File history / blame overlay entry points (kept out of the main file).
public sealed partial class MainWindowViewModel
{
    private FileHistoryViewModel? _fileHistory;

    /// <summary>The file history and blame overlay.</summary>
    public FileHistoryViewModel FileHistory => _fileHistory ??= new FileHistoryViewModel(() => _module, SelectCommit);

    /// <summary>The open repository, for the secondary windows.</summary>
    internal GitCommands.GitModule? CurrentModule => _module;

    // The new File History window when the application provides one, the overlay otherwise.
    [RelayCommand]
    private Task ShowFileHistoryAsync(ChangedFileViewModel? file)
    {
        if (file is null)
        {
            return Task.CompletedTask;
        }

        if (WindowService is IFileHistoryWindowService windows)
        {
            windows.ShowFileHistoryWindow(file.Name, revision: null, FileHistoryTab.Diff);
            return Task.CompletedTask;
        }

        return FileHistory.OpenAsync(file.Name, blame: false);
    }

    // A file listed under the selected commit is blamed as of that commit; a working tree file as of HEAD.
    [RelayCommand]
    private Task ShowFileBlameAsync(ChangedFileViewModel? file)
    {
        if (file is null)
        {
            return Task.CompletedTask;
        }

        string? revision = CommitDetails.Files.Contains(file) && CommitDetails.HasCommit ? CommitDetails.Hash : null;
        if (WindowService is IFileHistoryWindowService windows)
        {
            windows.ShowFileHistoryWindow(file.Name, revision, FileHistoryTab.Blame);
            return Task.CompletedTask;
        }

        return FileHistory.OpenAsync(file.Name, blame: true, revision);
    }
}
