using CommunityToolkit.Mvvm.Input;

namespace GitExtensions.Desktop.ViewModels;

// Merge/rebase entries of the branch context menu (kept out of the main file).
public sealed partial class RefsTreeViewModel
{
    /// <summary>Runs the merge/rebase; set by the main view-model.</summary>
    public OperationBarViewModel? Operations { get; set; }

    [RelayCommand]
    private Task MergeIntoCurrentAsync(RefNodeViewModel? node) => MergeAsync(node, MergeMode.Default);

    [RelayCommand]
    private Task MergeNoFastForwardAsync(RefNodeViewModel? node) => MergeAsync(node, MergeMode.NoFastForward);

    [RelayCommand]
    private Task MergeFastForwardOnlyAsync(RefNodeViewModel? node) => MergeAsync(node, MergeMode.FastForwardOnly);

    [RelayCommand]
    private Task RebaseOntoAsync(RefNodeViewModel? node)
        => node is { CanMergeOrRebase: true } && Operations is not null ? Operations.RebaseAsync(node.FullName) : Task.CompletedTask;

    private Task MergeAsync(RefNodeViewModel? node, MergeMode mode)
        => node is { CanMergeOrRebase: true } && Operations is not null ? Operations.MergeAsync(node.FullName, mode) : Task.CompletedTask;
}
