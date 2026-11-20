using Avalonia.Controls;
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
        MenuKit.AttachBuilt(FileTree, row => row is ChangeTreeNode { File: { } file } ? FileListContextMenu.Build(MenuContext(file)) : null);

        // The tree shows the same files as the list: picking a file node shows its diff, and the selected file is shown as selected.
        FileTree.SelectionChanged += (_, _) =>
        {
            if (FileTree.SelectedItem is ChangeTreeNode { File: { } file } && ViewModel is { } viewModel)
            {
                viewModel.SelectedFile = file;
            }
        };
        DataContextChanged += (_, _) => Subscribe();
    }

    private RevisionDiffViewModel? ViewModel => DataContext as RevisionDiffViewModel;

    private RevisionDiffViewModel? _subscribed;

    private void Subscribe()
    {
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribed.Tree.CollectionChanged -= OnTreeChanged;
        }

        _subscribed = ViewModel;
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged += OnViewModelPropertyChanged;
            _subscribed.Tree.CollectionChanged += OnTreeChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RevisionDiffViewModel.SelectedFile))
        {
            SelectInTree();
        }
    }

    private void OnTreeChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => Avalonia.Threading.Dispatcher.UIThread.Post(SelectInTree, Avalonia.Threading.DispatcherPriority.Loaded);

    private void SelectInTree()
    {
        if (ViewModel?.SelectedFile is not { } file || (FileTree.SelectedItem is ChangeTreeNode { File: { } shown } && ReferenceEquals(shown, file)))
        {
            return;
        }

        if (ViewModel.Tree.SelectMany(Flatten).FirstOrDefault(node => ReferenceEquals(node.File, file)) is { } match)
        {
            FileTree.SelectedItem = match;
        }
    }

    private static IEnumerable<ChangeTreeNode> Flatten(ChangeTreeNode node) => [node, .. node.Children.SelectMany(Flatten)];

    private FileMenuContext MenuContext(ChangedFileViewModel file)
    {
        MainWindowViewModel? main = this.FindAncestorOfType<Window>()?.DataContext as MainWindowViewModel;
        string? revision = ViewModel?.Revision;
        return FileMenus.Context(this, main, file.Name, revision, ViewModel?.BaseRevision, isFolder: false);
    }
}

/// <summary>The file menu context of the lists that show a revision's files (Diff tab, Commit tab, File tree).</summary>
internal static class FileMenus
{
    public static FileMenuContext Context(Control host, MainWindowViewModel? main, string path, string? revision, string? baseRevision, bool isFolder, bool offerFileTree = true)
    {
        TopLevel? top = TopLevel.GetTopLevel(host);
        return new FileMenuContext
        {
            Paths = [path],
            WorkingDir = main?.RepositoryPath ?? string.Empty,
            Revision = revision,
            BaseRevision = baseRevision,
            IsFolder = isFolder,
            OpenHistory = (p, blame) => _ = main?.RevisionTabs.OpenFileHistoryAsync(p, blame, revision),
            Clipboard = new TopLevelClipboardService(top),
            Owner = top as Window,
            Changed = revision is null && main is not null ? main.RefreshChangesAsync : null,
            WorkingTreeChanged = main is not null ? main.RefreshChangesAsync : null,
            ShowInFileTree = main is null || !offerFileTree ? null : main.RevisionTabs.ShowInFileTree,
            FilterInGrid = main is null ? null : main.FilterHistoryByPath,
            PickSaveFile = top is null ? null : new WindowFileSavePicker(top).PickSaveFileAsync,
        };
    }
}
