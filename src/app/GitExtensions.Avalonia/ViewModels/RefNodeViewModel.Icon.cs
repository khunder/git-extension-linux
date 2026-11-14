namespace GitExtensions.Desktop.ViewModels;

public sealed partial class RefNodeViewModel
{
    /// <summary>The original left panel's icon for this node (file name in Assets/Icons).</summary>
    public string IconName => Kind switch
    {
        RefNodeKind.Group => Name switch
        {
            "Branches" => "BranchLocalRoot",
            "Remotes" => "BranchRemoteRoot",
            "Tags" => "TagHorizontal",
            "Submodules" => "FolderSubmodule",
            "Stashes" => "Stash",
            _ => "BranchFolder",
        },
        RefNodeKind.Folder => IsRemoteRoot ? "Remote" : "BranchFolder",
        RefNodeKind.LocalBranch => "BranchLocal",
        RefNodeKind.RemoteBranch => "BranchRemote",
        RefNodeKind.Tag => "TagHorizontal",
        RefNodeKind.Submodule => "FolderSubmodule",
        RefNodeKind.Stash => "Stash",
        _ => "BranchFolder",
    };
}
