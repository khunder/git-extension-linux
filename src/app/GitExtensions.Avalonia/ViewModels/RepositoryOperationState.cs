namespace GitExtensions.Desktop.ViewModels;

public enum OperationKind
{
    None,
    Merge,
    Rebase,
    CherryPick,
    Revert,
}

/// <summary>
///  Detects a merge, rebase, cherry-pick or revert that is in progress from git's state files.
/// </summary>
public static class RepositoryOperationState
{
    public static OperationKind Detect(string gitDir)
    {
        if (Directory.Exists(Path.Combine(gitDir, "rebase-merge"))
            || (Directory.Exists(Path.Combine(gitDir, "rebase-apply")) && !File.Exists(Path.Combine(gitDir, "rebase-apply", "applying"))))
        {
            return OperationKind.Rebase;
        }

        if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD")))
        {
            return OperationKind.CherryPick;
        }

        if (File.Exists(Path.Combine(gitDir, "REVERT_HEAD")))
        {
            return OperationKind.Revert;
        }

        return File.Exists(Path.Combine(gitDir, "MERGE_HEAD")) ? OperationKind.Merge : OperationKind.None;
    }

    /// <summary>The git sub-command of the operation, e.g. <c>cherry-pick</c>.</summary>
    public static string CommandName(OperationKind kind) => kind switch
    {
        OperationKind.Merge => "merge",
        OperationKind.Rebase => "rebase",
        OperationKind.CherryPick => "cherry-pick",
        OperationKind.Revert => "revert",
        _ => string.Empty,
    };
}
