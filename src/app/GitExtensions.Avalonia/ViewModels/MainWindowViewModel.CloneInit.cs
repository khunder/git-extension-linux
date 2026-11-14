using CommunityToolkit.Mvvm.Input;

namespace GitExtensions.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private CloneViewModel? _clone;
    private InitViewModel? _init;

    /// <summary>
    ///  The "Clone repository" dialog. Progress goes to the output panel; the cloned repository is opened when done.
    /// </summary>
    public CloneViewModel Clone => _clone ??= new CloneViewModel(
        _folderPicker,
        OpenAsync,
        AskPass,
        reportStatus: message => Status = message,
        beginLog: Remote.BeginExternalOutput,
        appendLog: Remote.AppendExternalOutput,
        endLog: Remote.EndExternalOutput);

    /// <summary>
    ///  The "Init repository" dialog.
    /// </summary>
    public InitViewModel Init => _init ??= new InitViewModel(
        _folderPicker,
        OpenAsync,
        reportStatus: message => Status = message,
        reportError: message => ErrorMessage = message);

    [RelayCommand]
    private void ShowClone() => Clone.Open(DefaultParentFolder());

    [RelayCommand]
    private void ShowInit() => Init.Open();

    private void InitializeCloneInit()
    {
        Recent.RequestClone = ShowClone;
        Recent.RequestInit = ShowInit;
    }

    // Clone next to the open repository by default.
    private string? DefaultParentFolder()
    {
        if (!string.IsNullOrWhiteSpace(Clone.ParentFolder))
        {
            return Clone.ParentFolder;
        }

        string? parent = _module is null ? null : Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(_module.WorkingDir));
        return parent ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
