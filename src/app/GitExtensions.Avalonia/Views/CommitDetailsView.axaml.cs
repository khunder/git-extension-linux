using Avalonia.Controls;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views.ContextMenus;

namespace GitExtensions.Desktop.Views;

public sealed partial class CommitDetailsView : UserControl
{
    public CommitDetailsView()
    {
        InitializeComponent();
        MenuKit.AttachBuilt(FileList, row => row is ChangedFileViewModel file ? FileListContextMenu.Build(MenuContext(file)) : null);
    }

    private FileMenuContext MenuContext(ChangedFileViewModel file)
        => FileMenus.Context(this, this.FindAncestorOfType<Window>()?.DataContext as MainWindowViewModel, file.Name, (DataContext as CommitDetailsViewModel)?.Hash, null, isFolder: false);
}
