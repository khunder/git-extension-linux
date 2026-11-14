using CommunityToolkit.Mvvm.Input;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>
    ///  Opens the secondary windows. Set by the application once the main window exists.
    /// </summary>
    public IWindowService? WindowService { get; set; }

    [RelayCommand]
    private void ShowCommitWindow() => WindowService?.ShowCommitWindow();

    [RelayCommand]
    private void ShowFileHistoryWindow(string? path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            WindowService?.ShowFileHistoryWindow(path);
        }
    }

    /// <summary>
    ///  Closes the open repository and returns to the dashboard (start page). The behaviour lives in the shell
    ///  implementation (<c>partial void OnCloseRepository</c>); until then this does nothing.
    /// </summary>
    [RelayCommand]
    private void CloseRepository() => OnCloseRepository();

    partial void OnCloseRepository();
}
