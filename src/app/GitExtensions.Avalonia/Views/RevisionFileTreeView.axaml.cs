using Avalonia.Controls;
using Avalonia.Interactivity;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>The File tree tab: the tree at the selected revision on the left, the selected file's content on the right.</summary>
public sealed partial class RevisionFileTreeView : UserControl
{
    public RevisionFileTreeView()
        => InitializeComponent();

    private string? Revision => (DataContext as RevisionFileTreeViewModel)?.Revision;

    private void OnFileHistoryClick(object? sender, RoutedEventArgs e) => FileActions.History(this, e, Revision);

    private void OnBlameClick(object? sender, RoutedEventArgs e) => FileActions.Blame(this, e, Revision);

    private void OnCopyPathClick(object? sender, RoutedEventArgs e) => FileActions.CopyPath(this, e);
}
