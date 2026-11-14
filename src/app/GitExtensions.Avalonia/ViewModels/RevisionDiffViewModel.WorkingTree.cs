using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

// The "S" (stage) and "R" (revert) keys of the Diff tab while it shows the uncommitted changes.
public sealed partial class RevisionDiffViewModel
{
    private GitModule? _workingModule;
    private ChangedFileViewModel? _pendingRevert;

    [ObservableProperty]
    private bool _isConfirmingRevert;

    [ObservableProperty]
    private string _revertConfirmationText = string.Empty;

    /// <summary>Called after the index or the working tree was changed from this tab (the main window refreshes its changes).</summary>
    public Func<Task>? WorkingTreeChanged { get; set; }

    /// <summary>Gets a value indicating whether the tab shows the working tree and a changed file is selected.</summary>
    public bool HasWorkingTreeFile => _workingModule is not null && Revision is null && SelectedFile is { Item.IsStatusOnly: false };

    /// <summary>Stages the selected file (a file that is already staged is left alone).</summary>
    [RelayCommand(CanExecute = nameof(CanStageWorkingFile))]
    private async Task StageSelectedFileAsync()
    {
        if (_workingModule is not { } module || SelectedFile is not { IsStaged: false } file)
        {
            return;
        }

        string? error = null;
        try
        {
            error = await Task.Run(() => module.StageFiles([file.Item], out string output) ? null : (string.IsNullOrWhiteSpace(output) ? "git add failed." : output));
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        await WorkingTreeChangedAsync(module, error is null ? null : $"Failed to stage {file.Name}: {GitErrorMessages.Describe(error, module.WorkingDir)}", file.Name);
    }

    /// <summary>Asks to throw away the changes of the selected file; nothing happens until <see cref="ConfirmRevertCommand"/> runs.</summary>
    [RelayCommand(CanExecute = nameof(CanRevertWorkingFile))]
    private void RevertSelectedFile()
    {
        if (SelectedFile is not { } file)
        {
            return;
        }

        _pendingRevert = file;
        RevertConfirmationText = $"Discard all changes to {file.Name}? This cannot be undone.";
        IsConfirmingRevert = true;
    }

    [RelayCommand]
    private void CancelRevert()
    {
        _pendingRevert = null;
        IsConfirmingRevert = false;
    }

    [RelayCommand]
    private async Task ConfirmRevertAsync()
    {
        ChangedFileViewModel? file = _pendingRevert;
        CancelRevert();
        if (_workingModule is not { } module || file is null)
        {
            return;
        }

        string? error = null;
        try
        {
            error = await Task.Run(() => WorkingTreeReset.ResetFiles(module, [file.Item]));
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        await WorkingTreeChangedAsync(module, error is null ? null : $"Failed to revert {file.Name}: {error}", file.Name);
    }

    partial void OnSelectedFileChanged(ChangedFileViewModel? oldValue, ChangedFileViewModel? newValue)
    {
        if (IsConfirmingRevert && newValue != _pendingRevert)
        {
            CancelRevert();
        }

        StageSelectedFileCommand.NotifyCanExecuteChanged();
        RevertSelectedFileCommand.NotifyCanExecuteChanged();
    }

    private bool CanStageWorkingFile() => HasWorkingTreeFile && SelectedFile is { IsStaged: false };

    private bool CanRevertWorkingFile() => HasWorkingTreeFile;

    private async Task WorkingTreeChangedAsync(GitModule module, string? error, string name)
    {
        if (WorkingTreeChanged is { } changed)
        {
            await changed();
        }

        // Reload the file list of the working tree and keep the neighbourhood of the file that was acted on.
        List<string> before = [.. Files.Select(f => f.Name)];
        int position = before.IndexOf(name);
        await ShowWorkingTreeAsync(module);
        if (Files.FirstOrDefault(f => f.Name == name) is { } same)
        {
            SelectedFile = same;
        }
        else if (Files.Count > 0)
        {
            SelectedFile = Files[Math.Min(Math.Max(position, 0), Files.Count - 1)];
        }

        if (error is not null)
        {
            Notice = error;
        }
    }
}
