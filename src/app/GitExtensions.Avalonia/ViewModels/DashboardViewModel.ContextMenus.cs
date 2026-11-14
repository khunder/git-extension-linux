using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

// "Open in terminal" of the repository entry menu (kept out of the main file).
public sealed partial class DashboardViewModel
{
    /// <summary>Opens a terminal in the repository folder; failures are reported like the other dashboard errors.</summary>
    public void OpenInTerminal(DashboardEntry? entry)
    {
        if (entry is not null && ShellLauncher.OpenTerminal(entry.Path) is { } error)
        {
            _reportError(error);
        }
    }
}
