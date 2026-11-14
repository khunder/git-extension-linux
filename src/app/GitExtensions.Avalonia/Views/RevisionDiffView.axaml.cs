using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Desktop.Views.ContextMenus;

namespace GitExtensions.Desktop.Views;

/// <summary>The Diff tab: changed files on the left, the diff of the selected one on the right.</summary>
public sealed partial class RevisionDiffView : UserControl
{
    public RevisionDiffView()
    {
        InitializeComponent();
        MenuKit.AttachBuilt(FileList, row => row is ChangedFileViewModel file ? FileListContextMenu.Build(MenuContext(file)) : null);
    }

    private string? Revision => (DataContext as RevisionDiffViewModel)?.Revision;

    private FileMenuContext MenuContext(ChangedFileViewModel file)
    {
        MainWindowViewModel? main = this.FindAncestorOfType<Window>()?.DataContext as MainWindowViewModel;
        string? revision = Revision;
        return new FileMenuContext
        {
            Paths = [file.Name],
            WorkingDir = main?.RepositoryPath ?? string.Empty,
            Revision = revision,
            OpenHistory = (path, blame) => _ = main?.RevisionTabs.OpenFileHistoryAsync(path, blame, revision),
            Clipboard = new TopLevelClipboardService(TopLevel.GetTopLevel(this)),
        };
    }
}
