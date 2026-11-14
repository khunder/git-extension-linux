using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

public sealed class ChangedFileViewModel
{
    public ChangedFileViewModel(GitItemStatus item)
    {
        Item = item;
        Name = item.Name;
        Status = item switch
        {
            { IsNew: true } => "A",
            { IsDeleted: true } => "D",
            { IsRenamed: true } => "R",
            { IsCopied: true } => "C",
            { IsUnmerged: true } => "U",
            _ => "M"
        };
        IsStaged = item.Staged == StagedStatus.Index;
    }

    public GitItemStatus Item { get; }

    public string Name { get; }

    /// <summary>
    ///  Single-letter status, like in <c>git status --short</c>.
    /// </summary>
    public string Status { get; }

    public bool IsStaged { get; }
}
