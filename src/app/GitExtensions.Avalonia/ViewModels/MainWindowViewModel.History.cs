using CommunityToolkit.Mvvm.ComponentModel;

namespace GitExtensions.Desktop.ViewModels;

// History graph and commit context menu wiring (kept out of the main file).
public sealed partial class MainWindowViewModel
{
    /// <summary>Lanes reserved by the graph column of the history list.</summary>
    [ObservableProperty]
    private int _graphLaneCount = 1;

    /// <summary>The commit context menu operations.</summary>
    public CommitActionsViewModel Actions { get; private set; } = null!;

    private void InitializeHistoryExtras()
    {
        Actions = new CommitActionsViewModel(
            () => _module,
            () => OpenAsync(RepositoryPath),
            message => ErrorMessage = message,
            message => Status = message);
    }
}
