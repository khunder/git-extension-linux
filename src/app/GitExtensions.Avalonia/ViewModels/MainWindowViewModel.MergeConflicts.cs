using CommunityToolkit.Mvvm.Input;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

// Commands > "Solve merge conflicts…".
public sealed partial class MainWindowViewModel
{
    [RelayCommand]
    private void ShowMergeConflicts()
    {
        if (WindowService is IMergeConflictsWindowService service)
        {
            service.ShowMergeConflictsWindow();
        }
    }

    /// <summary>Creates the view-model of the merge conflicts window, or <see langword="null"/> when no repository is open.</summary>
    public MergeConflictsViewModel? CreateMergeConflictsViewModel()
        => _module is { } module ? new MergeConflictsViewModel(this, module) : null;
}
