namespace GitExtensions.Desktop.Services;

/// <summary>
///  The tab a File History window shows when it opens (the order of the tabs in the window).
/// </summary>
public enum FileHistoryTab
{
    Commit = 0,
    Diff = 1,
    View = 2,
    Blame = 3
}

/// <summary>
///  Opens File History windows on a chosen tab. Implemented by <see cref="WindowService"/>; callers use it when
///  they need more than <see cref="IWindowService.ShowFileHistoryWindow"/> (which opens on the Diff tab).
/// </summary>
public interface IFileHistoryWindowService
{
    void ShowFileHistoryWindow(string path, string? revision, FileHistoryTab tab);
}
