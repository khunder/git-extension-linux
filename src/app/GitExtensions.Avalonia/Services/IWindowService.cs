namespace GitExtensions.Desktop.Services;

/// <summary>
///  Opens the secondary windows of the application (the Commit window, the File History window).
///  The menu, toolbar and context menus talk to this interface; each window provides its own implementation part.
/// </summary>
public interface IWindowService
{
    /// <summary>
    ///  Shows (or activates) the Commit window for the open repository.
    /// </summary>
    void ShowCommitWindow();

    /// <summary>
    ///  Shows a File History window for <paramref name="path"/> (relative to the working directory),
    ///  optionally starting at <paramref name="revision"/>.
    /// </summary>
    void ShowFileHistoryWindow(string path, string? revision = null);
}

/// <summary>
///  Default <see cref="IWindowService"/>. Each secondary window supplies its implementation in its own partial file
///  (<c>WindowService.Commit.cs</c>, <c>WindowService.FileHistory.cs</c>) by implementing the matching partial method.
/// </summary>
public sealed partial class WindowService : IWindowService
{
    public WindowService(ViewModels.MainWindowViewModel main, Avalonia.Controls.Window owner)
    {
        Main = main;
        Owner = owner;
    }

    /// <summary>
    ///  The main window view-model the secondary windows operate on.
    /// </summary>
    public ViewModels.MainWindowViewModel Main { get; }

    /// <summary>
    ///  The main window; owner of the secondary windows.
    /// </summary>
    public Avalonia.Controls.Window Owner { get; }

    public void ShowCommitWindow() => OnShowCommitWindow();

    public void ShowFileHistoryWindow(string path, string? revision = null) => OnShowFileHistoryWindow(path, revision);

    partial void OnShowCommitWindow();

    partial void OnShowFileHistoryWindow(string path, string? revision);
}
