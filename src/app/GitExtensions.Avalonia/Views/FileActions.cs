using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  Context menu actions shared by the file lists of the revision tabs (file history, blame, copy path).
/// </summary>
internal static class FileActions
{
    public static void History(Control host, RoutedEventArgs e, string? revision)
        => Open(host, e, blame: false, revision);

    public static void Blame(Control host, RoutedEventArgs e, string? revision)
        => Open(host, e, blame: true, revision);

    public static void CopyPath(Control host, RoutedEventArgs e)
    {
        if (GetPath(e) is { } path)
        {
            _ = new TopLevelClipboardService(TopLevel.GetTopLevel(host)).SetTextAsync(path);
        }
    }

    public static void CopyName(Control host, RoutedEventArgs e)
    {
        if (GetPath(e) is { } path)
        {
            _ = new TopLevelClipboardService(TopLevel.GetTopLevel(host)).SetTextAsync(path[(path.LastIndexOf('/') + 1)..]);
        }
    }

    private static void Open(Control host, RoutedEventArgs e, bool blame, string? revision)
    {
        if (GetPath(e) is { } path && host.FindAncestorOfType<RevisionTabsView>()?.DataContext is MainWindowViewModel main)
        {
            _ = main.RevisionTabs.OpenFileHistoryAsync(path, blame, revision);
        }
    }

    private static string? GetPath(RoutedEventArgs e)
        => GetItem(e) switch
        {
            ChangedFileViewModel file => file.Name,
            FileTreeNode { IsPlaceholder: false } node => node.FullPath,
            _ => null
        };

    // The menu is not part of the visual tree: the row it was opened on is its placement target.
    private static object? GetItem(RoutedEventArgs e)
        => e.Source is MenuItem { Parent: ContextMenu { PlacementTarget: { } target } }
            ? target.DataContext
            : (e.Source as StyledElement)?.DataContext;
}
