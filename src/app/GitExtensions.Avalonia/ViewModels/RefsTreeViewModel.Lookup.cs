namespace GitExtensions.Desktop.ViewModels;

// Finding a node by name, for the menus that act on a ref picked by name (e.g. the branches of a commit).
public sealed partial class RefsTreeViewModel
{
    /// <summary>The node of the given kind with the given full name, or null (e.g. hidden by the filter).</summary>
    public RefNodeViewModel? FindNode(RefNodeKind kind, string fullName)
    {
        Stack<RefNodeViewModel> pending = new(Roots);
        while (pending.Count > 0)
        {
            RefNodeViewModel node = pending.Pop();
            if (node.Kind == kind && node.FullName == fullName)
            {
                return node;
            }

            foreach (RefNodeViewModel child in node.Children)
            {
                pending.Push(child);
            }
        }

        return null;
    }
}
