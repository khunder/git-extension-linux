using Avalonia.Controls;
using GitExtensions.Desktop.Views;

namespace GitExtensions.Desktop.Services;

// The File History window: one non-modal window per file, activated again when it is already open.
public sealed partial class WindowService : IFileHistoryWindowService
{
    private readonly Dictionary<string, FileHistoryWindow> _fileHistoryWindows = new(StringComparer.Ordinal);

    /// <summary>
    ///  Where the File History window size and position are remembered; tests point it at a temp file.
    /// </summary>
    public static string? FileHistoryStatePath { get; set; }

    /// <summary>
    ///  Gets the open File History window of <paramref name="path"/>, if any.
    /// </summary>
    public FileHistoryWindow? GetFileHistoryWindow(string path)
        => _fileHistoryWindows.TryGetValue(NormalizeHistoryPath(path), out FileHistoryWindow? window) ? window : null;

    public void ShowFileHistoryWindow(string path, string? revision, FileHistoryTab tab)
    {
        string key = NormalizeHistoryPath(path);
        if (_fileHistoryWindows.TryGetValue(key, out FileHistoryWindow? existing))
        {
            existing.Activate();
            _ = existing.ViewModel.NavigateAsync(tab, revision);
            return;
        }

        ViewModels.FileHistoryWindowViewModel viewModel = new(() => Main.CurrentModule, key, Main.RepositoryPath ?? string.Empty);
        FileHistoryWindow window = new(viewModel, new WindowStateStore(FileHistoryStatePath ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitExtensions", "avalonia-filehistory-window.json")));
        _fileHistoryWindows[key] = window;
        window.Closed += (_, _) => _fileHistoryWindows.Remove(key);
        window.Show(Owner);
        _ = viewModel.OpenAsync(tab, revision);
    }

    partial void OnShowFileHistoryWindow(string path, string? revision)
        => ShowFileHistoryWindow(path, revision, FileHistoryTab.Diff);

    private static string NormalizeHistoryPath(string path) => path.Replace('\\', '/').TrimStart('/');
}
