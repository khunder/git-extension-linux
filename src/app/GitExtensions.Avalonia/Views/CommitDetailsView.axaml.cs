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
    {
        MainWindowViewModel? main = this.FindAncestorOfType<Window>()?.DataContext as MainWindowViewModel;
        return new FileMenuContext
        {
            Paths = [file.Name],
            WorkingDir = main?.RepositoryPath ?? string.Empty,
            Revision = (DataContext as CommitDetailsViewModel)?.Hash,
            OpenHistory = (_, blame) => (blame ? main?.ShowFileBlameCommand : main?.ShowFileHistoryCommand)?.Execute(file),
            Clipboard = new TopLevelClipboardService(TopLevel.GetTopLevel(this)),
        };
    }
}
