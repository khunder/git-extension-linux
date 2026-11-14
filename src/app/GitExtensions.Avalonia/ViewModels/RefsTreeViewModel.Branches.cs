using System.Collections.ObjectModel;
using System.Globalization;

namespace GitExtensions.Desktop.ViewModels;

// The flat list of local branches behind the toolbar's branch drop-down (kept out of the main file).
public sealed partial class RefsTreeViewModel
{
    /// <summary>
    ///  The names of all local branches (not affected by the tree filter), sorted.
    /// </summary>
    public ObservableCollection<string> LocalBranchNames { get; } = [];

    private void UpdateLocalBranchNames()
    {
        LocalBranchNames.Clear();
        foreach (string name in _entries.Where(e => e.Kind == RefNodeKind.LocalBranch).Select(e => e.Name).OrderBy(n => n, StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true)))
        {
            LocalBranchNames.Add(name);
        }
    }
}
