using Avalonia.Controls;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  The stashed changes of the current branch in the Changes tab: the files of the stash and their diff, with Restore
///  (git stash pop) and Discard (git stash drop).
/// </summary>
public sealed partial class StashedChangesView : UserControl
{
    public StashedChangesView() => InitializeComponent();
}
