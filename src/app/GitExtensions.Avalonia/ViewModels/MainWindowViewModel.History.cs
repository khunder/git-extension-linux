using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using GitExtensions.Desktop.History;

namespace GitExtensions.Desktop.ViewModels;

// History graph and commit context menu wiring (kept out of the main file).
public sealed partial class MainWindowViewModel
{
    private readonly CommitGraphBuilder _graphBuilder = new();
    private int _graphedCount;

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
        Commits.CollectionChanged += OnCommitsChanged;
    }

    // Commits arrive in batches; only the new ones are fed to the graph builder.
    private void OnCommitsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _graphBuilder.Reset();
            _graphedCount = 0;
            GraphLaneCount = 1;
            return;
        }

        for (; _graphedCount < Commits.Count; _graphedCount++)
        {
            CommitItemViewModel commit = Commits[_graphedCount];
            commit.Graph = _graphBuilder.Append(
                commit.ObjectId.ToString(),
                commit.Revision.ParentIds?.Select(id => id.ToString()).ToList() ?? []);
            commit.Actions = Actions;
        }

        GraphLaneCount = Math.Max(1, _graphBuilder.MaxLaneCount);
    }
}
