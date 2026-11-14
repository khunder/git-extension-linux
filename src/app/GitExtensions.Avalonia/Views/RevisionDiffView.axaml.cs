using Avalonia.Controls;
using Avalonia.Interactivity;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>The Diff tab: changed files on the left, the diff of the selected one on the right.</summary>
public sealed partial class RevisionDiffView : UserControl
{
    public RevisionDiffView()
        => InitializeComponent();

    private string? Revision => (DataContext as RevisionDiffViewModel)?.Revision;

    private void OnFileHistoryClick(object? sender, RoutedEventArgs e) => FileActions.History(this, e, Revision);

    private void OnBlameClick(object? sender, RoutedEventArgs e) => FileActions.Blame(this, e, Revision);

    private void OnCopyPathClick(object? sender, RoutedEventArgs e) => FileActions.CopyPath(this, e);

    private void OnCopyNameClick(object? sender, RoutedEventArgs e) => FileActions.CopyName(this, e);
}
