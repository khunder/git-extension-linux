using CommunityToolkit.Mvvm.Input;

namespace GitExtensions.Desktop.ViewModels;

// The Pull and Push dialogs (Commands menu, Ctrl+Shift+L / Ctrl+Shift+K); fetch and the toolbar's pull button stay direct.
public sealed partial class MainWindowViewModel
{
    private PullDialogViewModel? _pull;
    private PushDialogViewModel? _push;

    public PullDialogViewModel Pull => _pull ??= new PullDialogViewModel(
        Remote,
        () => _module,
        async () =>
        {
            await OpenAsync(RepositoryPath);
            await Operation.RefreshAsync();
        },
        () => Operation.IsActive);

    public PushDialogViewModel Push => _push ??= new PushDialogViewModel(Remote, () => _module);

    [RelayCommand]
    private async Task ShowPullDialogAsync()
    {
        if (!HasOpenedRepository)
        {
            return;
        }

        PullMode mode = DefaultPullAction switch
        {
            PullAction.Rebase => PullMode.Rebase,
            PullAction.Fetch or PullAction.FetchAll or PullAction.FetchAndPruneAll => PullMode.Fetch,
            _ => PullMode.Merge,
        };
        await Pull.OpenAsync(mode);
        Pull.FetchAll = DefaultPullAction is PullAction.FetchAll or PullAction.FetchAndPruneAll;
        Pull.Prune = DefaultPullAction == PullAction.FetchAndPruneAll;
    }

    [RelayCommand]
    private async Task ShowPushDialogAsync()
    {
        if (HasOpenedRepository)
        {
            await Push.OpenAsync();
        }
    }
}
