using Avalonia.Threading;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensionsDesktopTests;

internal static class BranchFilterTestExtensions
{
    /// <summary>The grid opens with "All branches"; tests about the checked out branch's history switch to "Current branch" and wait for the reload.</summary>
    public static async Task UseCurrentBranchAsync(this MainWindowViewModel viewModel)
    {
        viewModel.SelectedBranchChoice = MainWindowViewModel.CurrentBranchChoice;
        await Task.Delay(120);
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (viewModel.IsBusy && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }
}
