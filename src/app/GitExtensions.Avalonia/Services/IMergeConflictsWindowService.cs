namespace GitExtensions.Desktop.Services;

/// <summary>
///  Opens the "Solve merge conflicts" window. Implemented by <see cref="WindowService"/>; kept separate from
///  <see cref="IWindowService"/> so other implementations of that interface are unaffected.
/// </summary>
public interface IMergeConflictsWindowService
{
    /// <summary>Shows (or activates) the merge conflicts window of the open repository.</summary>
    void ShowMergeConflictsWindow();
}
