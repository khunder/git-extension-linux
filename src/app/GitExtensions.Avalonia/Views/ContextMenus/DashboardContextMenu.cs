using Avalonia.Controls;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views.ContextMenus;

/// <summary>The context menu of a repository entry of the dashboard (recent, favorites and categories).</summary>
public static class DashboardContextMenu
{
    public static List<Control> Build(DashboardViewModel dashboard, DashboardEntry entry)
    {
        List<Control> items =
        [
            MenuKit.Item("Open", "RepoOpen", () => dashboard.OpenCommand.Execute(entry)),
            MenuKit.Item("Open in file explorer", "BrowseFileExplorer", () => dashboard.OpenInExplorerCommand.Execute(entry)),
            MenuKit.Item("Open in terminal", "GitForWindows", () => dashboard.OpenInTerminal(entry)),
            MenuKit.Sep(),
            MenuKit.Item("Category…", "DashboardFolderGit", () => dashboard.BeginAssignCategoryCommand.Execute(entry)),
        ];

        if (entry.FavoriteCategory is null)
        {
            items.Add(MenuKit.Item(entry.PinMenuText, "ArrowUp", () => dashboard.TogglePinCommand.Execute(entry)));
        }

        items.Add(MenuKit.Item(entry.FavoriteMenuText, "Preview", () => dashboard.ToggleFavoriteCommand.Execute(entry)));
        items.Add(MenuKit.Sep());
        items.Add(MenuKit.Item("Remove from list", "DeleteText", () => dashboard.RemoveFromListCommand.Execute(entry)));
        items.Add(MenuKit.Item("Copy path", "CopyToClipboard", () => dashboard.CopyPathCommand.Execute(entry), enabled: dashboard.Clipboard is not null));
        return items;
    }
}
