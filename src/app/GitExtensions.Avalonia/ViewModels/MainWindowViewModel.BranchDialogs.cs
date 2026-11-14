namespace GitExtensions.Desktop.ViewModels;

// The merge, rebase and checkout branch dialogs (kept out of the main file).
public sealed partial class MainWindowViewModel
{
    private MergeDialogViewModel? _mergeDialog;
    private RebaseDialogViewModel? _rebaseDialog;
    private CheckoutBranchDialogViewModel? _checkoutDialog;

    /// <summary>The "Merge branches" dialog.</summary>
    public MergeDialogViewModel MergeDialog => _mergeDialog ??= new MergeDialogViewModel(() => _module, RefreshAfterBranchDialogAsync, message => Status = message);

    /// <summary>The "Rebase" dialog.</summary>
    public RebaseDialogViewModel RebaseDialog => _rebaseDialog ??= new RebaseDialogViewModel(() => _module, RefreshAfterBranchDialogAsync, message => Status = message);

    /// <summary>The "Checkout branch" dialog.</summary>
    public CheckoutBranchDialogViewModel CheckoutDialog => _checkoutDialog ??= new CheckoutBranchDialogViewModel(() => _module, RefreshAfterBranchDialogAsync, message => Status = message);

    /// <summary>Opens the merge dialog with <paramref name="target"/> (a branch or commit) prefilled.</summary>
    public Task OpenMergeDialogAsync(string? target = null) => HasOpenedRepository ? MergeDialog.OpenAsync(target) : Task.CompletedTask;

    /// <summary>Opens the rebase dialog with <paramref name="target"/> (a branch or commit) prefilled.</summary>
    public Task OpenRebaseDialogAsync(string? target = null) => HasOpenedRepository ? RebaseDialog.OpenAsync(target) : Task.CompletedTask;

    /// <summary>Opens the checkout dialog with <paramref name="branch"/> preselected.</summary>
    public Task OpenCheckoutDialogAsync(string? branch = null) => HasOpenedRepository ? CheckoutDialog.OpenAsync(branch) : Task.CompletedTask;

    private async Task RefreshAfterBranchDialogAsync()
    {
        await OpenAsync(RepositoryPath);
        await Operation.RefreshAsync();
    }
}
