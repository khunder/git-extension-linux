using System.Collections.Specialized;
using Avalonia.Threading;

namespace GitExtensions.Desktop.ViewModels;

// Merge, rebase and conflict resolution (kept out of the main file).
public sealed partial class MainWindowViewModel
{
    private bool _operationRefreshQueued;

    /// <summary>The "operation in progress" bar and merge/rebase starter.</summary>
    public OperationBarViewModel Operation { get; private set; } = null!;

    /// <summary>Resolves the selected unmerged file.</summary>
    public ConflictResolverViewModel Resolver { get; private set; } = null!;

    private void InitializeOperations()
    {
        Operation = new OperationBarViewModel(
            () => _module,
            () => OpenAsync(RepositoryPath),
            message => ErrorMessage = message,
            message => Status = message);
        Resolver = new ConflictResolverViewModel(
            () => _module,
            async () =>
            {
                await RefreshChangesAsync();
                await Operation.RefreshAsync();
            },
            message => ErrorMessage = message);
        Refs.Operations = Operation;

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectedChangedFile))
            {
                _ = Resolver.ShowAsync(SelectedChangedFile, ChangedFiles);
            }
            else if (e.PropertyName == nameof(IsBusy) && !IsBusy)
            {
                _ = Operation.RefreshAsync();
            }
        };

        // Staging or resolving files changes the number of unmerged files; coalesce the burst of Clear/Add events.
        ChangedFiles.CollectionChanged += (_, _) => QueueOperationRefresh();
    }

    private void QueueOperationRefresh()
    {
        if (_operationRefreshQueued)
        {
            return;
        }

        _operationRefreshQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _operationRefreshQueued = false;
                _ = Operation.RefreshAsync();
            },
            DispatcherPriority.Background);
    }
}
