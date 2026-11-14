using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

// Commands > Bisect…: the dialog and the marks in the revision grid (kept out of the main file).
public sealed partial class MainWindowViewModel
{
    private IReadOnlyDictionary<ObjectId, BisectMark> _bisectMarks = new Dictionary<ObjectId, BisectMark>();

    /// <summary>The bisect session of the open repository.</summary>
    public BisectViewModel Bisect { get; private set; } = null!;

    private void InitializeBisect()
    {
        ToolsContext context = new(() => _module, () => OpenAsync(RepositoryPath), message => ErrorMessage = message, message => Status = message, path => OpenAsync(path));
        Bisect = new BisectViewModel(context, () => SelectedCommit?.ObjectId, ApplyBisectMarks);

        // Rows are created by the history loads: give new ones their mark.
        Commits.CollectionChanged += (_, e) =>
        {
            if (_bisectMarks.Count > 0 && e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
            {
                foreach (CommitItemViewModel item in e.NewItems)
                {
                    item.BisectMark = _bisectMarks.GetValueOrDefault(item.ObjectId);
                }
            }
        };

        // A repository that is in the middle of a bisect (started elsewhere) shows its marks as soon as it is open.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Branch) && Branch is not null)
            {
                _ = Bisect.ReloadStateAsync();
            }
        };
    }

    private void ApplyBisectMarks(BisectState state)
    {
        _bisectMarks = state.Marks;
        foreach (CommitItemViewModel item in Commits)
        {
            item.BisectMark = _bisectMarks.GetValueOrDefault(item.ObjectId);
        }
    }

    [RelayCommand]
    private void ShowBisect()
    {
        if (!HasOpenedRepository)
        {
            return;
        }

        Bisect.UpdateSelection();
        Dialogs.Show(Bisect);
        _ = Bisect.ReloadStateAsync();
    }
}
