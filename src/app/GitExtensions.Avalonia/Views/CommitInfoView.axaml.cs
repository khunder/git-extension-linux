using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GitExtensions.Desktop.ViewModels;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.Views;

/// <summary>The Commit tab: author, dates, hash, parent and child links, message, related links and containing branches and tags.</summary>
public sealed partial class CommitInfoView : UserControl
{
    public CommitInfoView()
        => InitializeComponent();

    private void OnAuthorClick(object? sender, RoutedEventArgs e)
        => OpenLink((sender as Button)?.Tag as string);

    private void OnLinkClick(object? sender, RoutedEventArgs e)
        => OpenLink((sender as Button)?.Tag as string);

    private void OnCommitLinkClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ObjectId id)
        {
            SelectCommit(id);
        }
    }

    private void OnDescribeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CommitDetailsViewModel { DescribeTagCommit: { } id })
        {
            SelectCommit(id);
        }
    }

    // The commit lives in the grid of the main view-model, which is the data context of the tab control hosting this view.
    private void SelectCommit(ObjectId id)
    {
        if (this.FindAncestorOfType<RevisionTabsView>()?.DataContext is MainWindowViewModel main)
        {
            main.SelectCommit(id);
        }
    }

    // Only web and mail links are opened: a commit message must not be able to launch anything else.
    private void OpenLink(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            && uri.Scheme is "http" or "https" or "mailto"
            && TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
        {
            _ = launcher.LaunchUriAsync(uri);
        }
    }
}
