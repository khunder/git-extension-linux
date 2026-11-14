namespace GitExtensions.Desktop.ViewModels;

public sealed partial class RefNodeViewModel
{
    /// <summary>Local and remote branches other than the current one can be merged or rebased onto.</summary>
    public bool CanMergeOrRebase => Kind is RefNodeKind.LocalBranch or RefNodeKind.RemoteBranch && !IsCurrent;
}
