using CommunityToolkit.Mvvm.Input;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

// The View menu: layout of the commit info, shown tabs, filters, remote branches and tags (all persisted).
public sealed partial class MainWindowViewModel
{
    /// <summary>The options of the View menu.</summary>
    public ViewOptionsViewModel ViewOptions { get; private set; } = null!;

    private void InitializeViewOptions()
    {
        ViewOptions = new ViewOptionsViewModel(ViewOptionsStore.Current);
        ShowRemoteBranches = ViewOptions.Settings.ShowRemoteBranches;
        ShowTags = ViewOptions.Settings.ShowTags;
        ViewOptions.OptionChanged += SaveViewOptions;
        ViewOptions.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CommitInfoLeft));
            OnPropertyChanged(nameof(CommitInfoRight));
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsWorkingTreeSelected))
            {
                OnPropertyChanged(nameof(CommitInfoLeft));
                OnPropertyChanged(nameof(CommitInfoRight));
            }

            if (e.PropertyName is nameof(ShowRemoteBranches) or nameof(ShowTags))
            {
                SaveViewOptions();
            }
        };
    }

    /// <summary>The commit details while the commit info is a pane left of the tabs, otherwise <see langword="null"/>.</summary>
    public CommitDetailsViewModel? CommitInfoLeft
        => !IsWorkingTreeSelected && ViewOptions.ShowCommitTab && ViewOptions.CommitInfoPosition == CommitInfoLayout.Left ? CommitDetails : null;

    /// <summary>The commit details while the commit info is a pane right of the tabs, otherwise <see langword="null"/>.</summary>
    public CommitDetailsViewModel? CommitInfoRight
        => !IsWorkingTreeSelected && ViewOptions.ShowCommitTab && ViewOptions.CommitInfoPosition == CommitInfoLayout.Right ? CommitDetails : null;

    private void SaveViewOptions() => ViewOptions.Save(ShowRemoteBranches, ShowTags);

    [RelayCommand]
    private void SetCommitInfoPosition(CommitInfoLayout position) => ViewOptions.CommitInfoPosition = position;

    [RelayCommand]
    private void ToggleSplitLayout() => ViewOptions.SideBySide = !ViewOptions.SideBySide;
}
