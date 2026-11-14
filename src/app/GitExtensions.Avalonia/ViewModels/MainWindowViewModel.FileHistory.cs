using CommunityToolkit.Mvvm.Input;

namespace GitExtensions.Desktop.ViewModels;

// File history / blame overlay entry points (kept out of the main file).
public sealed partial class MainWindowViewModel
{
    private FileHistoryViewModel? _fileHistory;

    /// <summary>The file history and blame overlay.</summary>
    public FileHistoryViewModel FileHistory => _fileHistory ??= new FileHistoryViewModel(() => _module, SelectCommit);

    [RelayCommand]
    private Task ShowFileHistoryAsync(ChangedFileViewModel? file)
        => file is null ? Task.CompletedTask : FileHistory.OpenAsync(file.Name, blame: false);

    // A file listed under the selected commit is blamed as of that commit; a working tree file as of HEAD.
    [RelayCommand]
    private Task ShowFileBlameAsync(ChangedFileViewModel? file)
        => file is null
            ? Task.CompletedTask
            : FileHistory.OpenAsync(file.Name, blame: true, CommitDetails.Files.Contains(file) && CommitDetails.HasCommit ? CommitDetails.Hash : null);
}
