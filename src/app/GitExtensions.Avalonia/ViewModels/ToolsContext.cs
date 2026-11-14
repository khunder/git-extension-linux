using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  What the repository tools need from the main window: the open module, a refresh, the error banner, the status line and "open a repository".
///  Also counts the git operations in flight so the tools can show one busy indicator.
/// </summary>
public sealed class ToolsContext(
    Func<GitModule?> getModule,
    Func<Task> refresh,
    Action<string> reportError,
    Action<string> reportStatus,
    Func<string, Task> openRepository)
{
    private int _busyCount;

    public event Action? BusyChanged;

    public bool IsBusy => _busyCount > 0;

    public IClipboardService? Clipboard { get; set; }

    public GitModule? Module => getModule();

    public Task RefreshAsync() => refresh();

    public void ReportError(string message) => reportError(message);

    public void ReportStatus(string message) => reportStatus(message);

    public Task OpenRepositoryAsync(string path) => openRepository(path);

    /// <summary>Runs git on a background thread (git calls block) and returns when it finished; the busy indicator is on meanwhile.</summary>
    public async Task<ExecutionResult> RunGitAsync(GitModule module, ArgumentString arguments)
    {
        SetBusy(1);
        try
        {
            return await Task.Run(() => module.GitExecutable.Execute(arguments, throwOnErrorExit: false));
        }
        finally
        {
            SetBusy(-1);
        }
    }

    /// <summary>Runs arbitrary blocking work on a background thread, with the busy indicator.</summary>
    public async Task<T> RunBusyAsync<T>(Func<T> work)
    {
        SetBusy(1);
        try
        {
            return await Task.Run(work);
        }
        finally
        {
            SetBusy(-1);
        }
    }

    public static string Describe(ExecutionResult result) => (result.StandardOutput + "\n" + result.StandardError).Trim();

    private void SetBusy(int delta)
    {
        _busyCount += delta;
        BusyChanged?.Invoke();
    }
}
