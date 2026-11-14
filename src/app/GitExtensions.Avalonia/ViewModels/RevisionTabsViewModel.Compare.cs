using GitCommands;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

public sealed partial class RevisionTabsViewModel
{
    /// <summary>Shows the differences between a revision and the working directory in the Diff tab.</summary>
    public void ShowDiffToWorkingDirectory(GitModule module, ObjectId from)
    {
        SetMode(workingTree: false);
        SelectedTabIndex = DiffTab;
        _ = Diff.ShowDiffToWorkingDirectoryAsync(module, from);
    }
}
