using Avalonia.Controls;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views.ContextMenus;

/// <summary>
///  The context menu of the left refs tree (branches, remotes, tags, submodules, stashes and their group headers), after the
///  original's RepoObjectsTree menus. Every entry reuses a command of <see cref="RefsTreeViewModel"/> or the main view-model.
/// </summary>
public static class RefsContextMenu
{
    public static List<Control> Build(RefsTreeViewModel refs, MainWindowViewModel? main, RefNodeViewModel node, Services.IClipboardService? clipboard)
    {
        List<Control> items = node.Kind switch
        {
            RefNodeKind.LocalBranch => LocalBranch(refs, main, node),
            RefNodeKind.RemoteBranch => RemoteBranch(refs, main, node),
            RefNodeKind.Tag => Tag(refs, node),
            RefNodeKind.Stash => Stash(main, node),
            RefNodeKind.Submodule => Submodule(main, node),
            RefNodeKind.Folder when node.IsRemoteRoot => Remote(refs, main, node),
            RefNodeKind.Group => Group(refs, main, node),
            _ => [],
        };

        if (node.IsLeaf)
        {
            items.Add(MenuKit.Sep());
            items.Add(MenuKit.Item("Copy name", "CopyToClipboard", () => _ = clipboard?.SetTextAsync(node.FullName), enabled: clipboard is not null));
        }

        return MenuKit.Tidy(items);
    }

    // Both open their dialog with this branch prefilled; the options (fast-forward mode, rebase flags) are chosen there.
    private static List<Control> MergeAndRebase(MainWindowViewModel? main, RefNodeViewModel node)
        =>
        [
            MenuKit.Item("Merge into current branch…", "Merge", () => _ = main?.OpenMergeDialogAsync(node.FullName), enabled: main is not null && node.CanMergeOrRebase, disabledTip: "This is the current branch"),
            MenuKit.Item("Rebase current branch on this…", "Rebase", () => _ = main?.OpenRebaseDialogAsync(node.FullName), enabled: main is not null && node.CanMergeOrRebase, disabledTip: "This is the current branch"),
        ];

    private static List<Control> LocalBranch(RefsTreeViewModel refs, MainWindowViewModel? main, RefNodeViewModel node)
    {
        List<Control> items =
        [
            MenuKit.Item("Checkout", "BranchCheckout", () => refs.CheckoutCommand.Execute(node), enabled: node.CanCheckout, disabledTip: "This is the current branch"),
            .. MergeAndRebase(main, node),
            MenuKit.Sep(),
            MenuKit.Item("Create branch from here…", "BranchCreate", () => refs.RequestNewBranchCommand.Execute(node)),
            MenuKit.Item("Create tag here…", "TagCreate", () => refs.RequestNewTagCommand.Execute(node)),
            MenuKit.Sep(),
            MenuKit.Item("Rename…", "Renamed", () => refs.RequestRenameCommand.Execute(node)),
            MenuKit.Item("Delete branch", "BranchDelete", () => refs.DeleteBranchCommand.Execute(node), enabled: !node.IsCurrent, disabledTip: "The current branch cannot be deleted"),
            MenuKit.Sep(),
            MenuKit.Item("Push", "Push", () => refs.PushBranchCommand.Execute(node)),
            MenuKit.Item("Set upstream…", "Link", () => refs.RequestSetUpstreamCommand.Execute(node)),
            MenuKit.Item("Unset upstream", "RemoteDelete", () => refs.UnsetUpstreamCommand.Execute(node), enabled: node.HasUpstream, disabledTip: "The branch has no upstream"),
        ];
        return items;
    }

    private static List<Control> RemoteBranch(RefsTreeViewModel refs, MainWindowViewModel? main, RefNodeViewModel node)
        =>
        [
            MenuKit.Item("Checkout as local tracking branch", "BranchCheckout", () => refs.CheckoutRemoteAsTrackingCommand.Execute(node)),
            .. MergeAndRebase(main, node),
            MenuKit.Sep(),
            MenuKit.Item("Create branch from here…", "BranchCreate", () => refs.RequestNewBranchCommand.Execute(node)),
            MenuKit.Item("Create tag here…", "TagCreate", () => refs.RequestNewTagCommand.Execute(node)),
            MenuKit.Sep(),
            MenuKit.Item("Fetch this remote", "PullFetch", () => refs.FetchRemoteCommand.Execute(node), enabled: node.CanFetchRemote),
            MenuKit.Item("Delete remote branch…", "BranchDelete", () => refs.RequestDeleteRemoteBranchCommand.Execute(node)),
        ];

    private static List<Control> Tag(RefsTreeViewModel refs, RefNodeViewModel node)
        =>
        [
            MenuKit.Item("Checkout", "Checkout", () => refs.CheckoutCommand.Execute(node), enabled: node.CanCheckout),
            MenuKit.Item("Create branch from here…", "BranchCreate", () => refs.RequestNewBranchCommand.Execute(node)),
            MenuKit.Sep(),
            MenuKit.Item("Push tag", "Push", () => refs.PushTagCommand.Execute(node)),
            MenuKit.Item("Delete tag…", "TagDelete", () => refs.RequestDeleteTagCommand.Execute(node)),
        ];

    private static List<Control> Remote(RefsTreeViewModel refs, MainWindowViewModel? main, RefNodeViewModel node)
        =>
        [
            MenuKit.Item("Fetch this remote", "PullFetch", () => refs.FetchRemoteCommand.Execute(node), enabled: node.CanFetchRemote),
            MenuKit.Item("Fetch and prune this remote", "PullFetchPrune", () => refs.FetchAndPruneRemoteCommand.Execute(node), enabled: node.CanFetchRemote),
            MenuKit.Item("Enable or disable this remote", "RemoteEnableAndFetch", enabled: false, disabledTip: "Git itself cannot disable a remote; Git Extensions keeps that flag in its own settings, which this version does not use"),
            MenuKit.Sep(),
            MenuKit.Item("Manage remote repositories…", "Remotes", () => main?.ShowRemotesCommand.Execute(null), enabled: main is not null),
        ];

    private static List<Control> Stash(MainWindowViewModel? main, RefNodeViewModel node)
    {
        StashItemViewModel? stash = main?.Stash.Items.FirstOrDefault(s => node.Name.StartsWith(s.Name, StringComparison.Ordinal));
        bool known = stash is not null;
        return
        [
            MenuKit.Item("Apply stash", "Stash", () => main!.Stash.ApplyCommand.Execute(stash), enabled: known),
            MenuKit.Item("Pop stash", "Stash", () => main!.Stash.PopCommand.Execute(stash), enabled: known),
            MenuKit.Item(
                "Drop stash…",
                "Stash",
                () =>
                {
                    main!.ManageStashesCommand.Execute(null);
                    main.Stash.RequestDropCommand.Execute(stash);
                },
                enabled: known),
            MenuKit.Sep(),
            MenuKit.Item("Manage stashes…", "Stash", () => main?.ManageStashesCommand.Execute(null), "Ctrl+Shift+T", enabled: main is not null),
        ];
    }

    private static List<Control> Submodule(MainWindowViewModel? main, RefNodeViewModel? node)
        =>
        [
            MenuKit.Item("Open this submodule", "FolderSubmodule", () => _ = main?.OpenSubmoduleAsync(node!.FullName.Length > 0 ? node.FullName : node.Name), enabled: main is not null && node is { Kind: RefNodeKind.Submodule }, disabledTip: "Select a submodule"),
            MenuKit.Sep(),
            MenuKit.Item("Manage submodules…", "SubmodulesManage", () => main?.ShowSubmodulesCommand.Execute(null), enabled: main is not null),
            MenuKit.Item("Update all submodules", "SubmodulesUpdate", () => main?.UpdateAllSubmodulesCommand.Execute(null), enabled: main is not null),
            MenuKit.Item("Synchronize all submodules", "SubmodulesSync", () => main?.SynchronizeAllSubmodulesCommand.Execute(null), enabled: main is not null),
        ];

    private static List<Control> Group(RefsTreeViewModel refs, MainWindowViewModel? main, RefNodeViewModel node)
        => node.Name switch
        {
            "Branches" =>
            [
                MenuKit.Item("Create branch…", "BranchCreate", () => refs.RequestNewBranchCommand.Execute(null)),
                MenuKit.Item("Checkout branch…", "BranchCheckout", () => main?.ShowCheckoutBranchDialogCommand.Execute(null), enabled: main is not null),
            ],
            "Remotes" =>
            [
                MenuKit.Item("Manage remote repositories…", "Remotes", () => main?.ShowRemotesCommand.Execute(null), enabled: main is not null),
                MenuKit.Item("Fetch all", "PullFetchAll", () => main?.FetchAllCommand.Execute(null), enabled: main is not null),
            ],
            "Tags" =>
            [
                MenuKit.Item("Create tag…", "TagCreate", () => refs.RequestNewTagCommand.Execute(null)),
                MenuKit.Item("Push all tags…", "Push", () => refs.RequestPushAllTagsCommand.Execute(null)),
            ],
            "Submodules" => Submodule(main, null),
            "Stashes" => [MenuKit.Item("Manage stashes…", "Stash", () => main?.ManageStashesCommand.Execute(null), "Ctrl+Shift+T", enabled: main is not null)],
            _ => [],
        };
}
