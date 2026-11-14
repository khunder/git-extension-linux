using Avalonia.Controls;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views.ContextMenus;

namespace GitExtensions.Desktop.Views;

/// <summary>The File tree tab: the tree at the selected revision on the left, the selected file's content on the right.</summary>
public sealed partial class RevisionFileTreeView : UserControl
{
    public RevisionFileTreeView()
    {
        InitializeComponent();
        MenuKit.AttachBuilt(
            Tree,
            row => row is FileTreeNode { IsPlaceholder: false } node ? FileListContextMenu.Build(MenuContext(node)) : null);
    }

    private string? Revision => (DataContext as RevisionFileTreeViewModel)?.Revision;

    private FileMenuContext MenuContext(FileTreeNode node)
    {
        MainWindowViewModel? main = this.FindAncestorOfType<Window>()?.DataContext as MainWindowViewModel;
        string? revision = Revision;
        return new FileMenuContext
        {
            Paths = [node.FullPath],
            WorkingDir = main?.RepositoryPath ?? string.Empty,
            Revision = revision,
            IsFolder = node.IsFolder,
            OpenHistory = (path, blame) => _ = main?.RevisionTabs.OpenFileHistoryAsync(path, blame, revision),
            Clipboard = new TopLevelClipboardService(TopLevel.GetTopLevel(this)),
        };
    }
}
