using Avalonia.Controls;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.Views.ContextMenus;

/// <summary>
///  The file menu of a commit's files (Diff tab, Commit tab, File tree), with the entries of the original's RevisionDiff
///  menu: reset the files to a side of the diff, cherry-pick their changes, the difftool, the working directory file and
///  the file of the revision, save as, rename, copy path, show in folder, show in the File tree, filter the history by
///  the file, history, blame and stop tracking.
/// </summary>
public static class RevisionFileContextMenu
{
    public static List<Control> Build(FileMenuContext context)
    {
        string revision = context.Revision!;
        string first = context.Paths[0];
        bool single = context.Paths.Count == 1;
        string full = Path.Combine(context.WorkingDir, first);
        bool exists = File.Exists(full);
        bool folderExists = Directory.Exists(Path.GetDirectoryName(full) ?? string.Empty);
        string shortRevision = revision.Length > 8 ? revision[..8] : revision;
        string noWorkingFile = single ? "The file does not exist in the working directory" : "Select a single file";
        List<Control> items = [];

        items.Add(MenuKit.Sub(
            "Reset file(s) to",
            "ResetFileTo",
            [
                MenuKit.Item($"This revision ({shortRevision})", "ResetFileTo", () => ConfirmReset(context, revision, $"this revision ({shortRevision})")),
                MenuKit.Item("Parent revision (the other side of the diff)", "ResetFileTo", () => _ = ResetToBaseAsync(context)),
            ]));
        items.Add(MenuKit.Item(
            "Cherry pick changes",
            "CherryPick",
            () => _ = CherryPickAsync(context),
            tip: "Applies what this commit changed in the file(s) to the working directory"));
        items.Add(MenuKit.Sep());

        items.Add(MenuKit.Sub(
            "Open with difftool",
            "Diff",
            [
                MenuKit.Item("A ↔ B (parent ↔ this revision)", "Diff", () => _ = DifftoolAsync(context, first, workingDirectory: false)),
                MenuKit.Item("B ↔ working directory", "Diff", () => _ = DifftoolAsync(context, first, workingDirectory: true), enabled: exists, disabledTip: noWorkingFile),
            ],
            enabled: single,
            disabledTip: "Select a single file"));
        items.Add(MenuKit.Item("Open working directory file", "ViewFile", () => ShellLauncher.Open(full), enabled: single && exists, disabledTip: noWorkingFile));
        items.Add(MenuKit.Item("Open working directory file with…", "FolderOpen", () => FileListContextMenu.OpenWith(context, full), enabled: single && exists, disabledTip: noWorkingFile));
        items.Add(MenuKit.Item("Open this revision (temp file)", "ViewFile", () => _ = OpenRevisionAsync(context, first, with: false), enabled: single, disabledTip: "Select a single file"));
        items.Add(MenuKit.Item("Open this revision with… (temp file)", "FolderOpen", () => _ = OpenRevisionAsync(context, first, with: true), enabled: single, disabledTip: "Select a single file"));
        items.Add(MenuKit.Item(
            "Edit working directory file",
            "EditFile",
            () => FileListContextMenu.ReportFailure(context, "Edit file", FileCommands.Edit(context.WorkingDir, full)),
            enabled: single && exists,
            disabledTip: noWorkingFile));
        items.Add(MenuKit.Item(
            "Save selected as…",
            "FileNew",
            () => _ = SaveAsAsync(context, first),
            enabled: single && context.PickSaveFile is not null,
            disabledTip: "Select a single file"));
        items.Add(MenuKit.Item("Rename / move…", "Renamed", () => Rename(context, first), enabled: single && exists, disabledTip: noWorkingFile));
        items.Add(MenuKit.Sep());

        IClipboardService? clipboard = context.Clipboard;
        string fullPaths = string.Join('\n', context.Paths.Select(p => Path.Combine(context.WorkingDir, p)));
        string relative = string.Join('\n', context.Paths);
        string names = string.Join('\n', context.Paths.Select(p => p[(p.LastIndexOf('/') + 1)..]));
        items.Add(MenuKit.Sub(
            "Copy path(s)",
            "CopyToClipboard",
            [
                MenuKit.Item("Full path", "CopyToClipboard", () => _ = clipboard?.SetTextAsync(fullPaths), enabled: clipboard is not null),
                MenuKit.Item("Relative path", "CopyToClipboard", () => _ = clipboard?.SetTextAsync(relative), enabled: clipboard is not null),
                MenuKit.Item("File name", "CopyToClipboard", () => _ = clipboard?.SetTextAsync(names), enabled: clipboard is not null),
            ]));
        items.Add(MenuKit.Item(
            "Show in folder",
            "BrowseFileExplorer",
            () => ShellLauncher.Open(Path.GetDirectoryName(full)!),
            enabled: single && folderExists,
            disabledTip: "The folder does not exist in the working directory"));
        items.Add(MenuKit.Sep());

        if (context.ShowInFileTree is { } showInTree)
        {
            items.Add(MenuKit.Item("Show in File tree", "FileTree", () => showInTree(first), enabled: single, disabledTip: "Select a single file"));
        }

        if (context.FilterInGrid is { } filter)
        {
            items.Add(MenuKit.Item("Filter file in grid", "EditFilter", () => filter(first), enabled: single, disabledTip: "Select a single file", tip: "Shows only the commits that changed this file"));
        }

        if (context.OpenHistory is { } openHistory)
        {
            MenuItem history = MenuKit.Item("File history", "FileHistory", () => openHistory(first, false), enabled: single, disabledTip: "Select a single file");
            history.FontWeight = Avalonia.Media.FontWeight.Bold;
            items.Add(history);
            items.Add(MenuKit.Item("Blame", "Blame", () => openHistory(first, true), enabled: single, disabledTip: "Select a single file"));
        }

        items.Add(MenuKit.Sep());
        items.Add(MenuKit.Item(
            "Stop tracking this file",
            "StopTrackingFile",
            () => _ = StopTrackingAsync(context),
            enabled: exists,
            disabledTip: "The file is not in the working directory",
            tip: "git rm --cached: the file stays on disk but is no longer tracked"));
        return MenuKit.Tidy(items);
    }

    private static string What(FileMenuContext context) => context.Paths.Count == 1 ? $"'{context.Paths[0]}'" : $"{context.Paths.Count} files";

    private static void ConfirmReset(FileMenuContext context, string target, string description)
    {
        if (FileCommands.OwnerOf(context) is not { } owner)
        {
            return;
        }

        QuickDialog.Ask(
            owner,
            "Reset file(s)",
            $"Reset {What(context)} to {description}? Uncommitted changes of the file(s) in the working directory are lost.",
            "Reset",
            unused => _ = RunAsync(context, "Reset file(s)", () => RevisionFileCommands.ResetToAsync(context.WorkingDir, target, context.Paths)),
            destructive: true);
    }

    private static async Task ResetToBaseAsync(FileMenuContext context)
    {
        string baseRevision = await RevisionFileCommands.ResolveBaseAsync(context.WorkingDir, context.Revision!, context.BaseRevision);
        string description = baseRevision == RevisionFileCommands.EmptyTree ? "the parent revision (where the file does not exist yet)" : $"the parent revision ({baseRevision[..8]})";
        ConfirmReset(context, baseRevision, description);
    }

    private static async Task CherryPickAsync(FileMenuContext context)
    {
        string baseRevision = await RevisionFileCommands.ResolveBaseAsync(context.WorkingDir, context.Revision!, context.BaseRevision);
        await RunAsync(context, "Cherry pick changes", () => RevisionFileCommands.CherryPickChangesAsync(context.WorkingDir, baseRevision, context.Revision!, context.Paths));
    }

    private static async Task DifftoolAsync(FileMenuContext context, string path, bool workingDirectory)
    {
        string revision = context.Revision!;
        string? error = workingDirectory
            ? await RevisionFileCommands.DifftoolAsync(context.WorkingDir, revision, null, path)
            : await RevisionFileCommands.DifftoolAsync(context.WorkingDir, await RevisionFileCommands.ResolveBaseAsync(context.WorkingDir, revision, context.BaseRevision), revision, path);
        FileListContextMenu.ReportFailure(context, "Difftool", error);
    }

    private static async Task OpenRevisionAsync(FileMenuContext context, string path, bool with)
    {
        (string? file, string? error) = await RevisionFileCommands.SaveTemporaryAsync(context.WorkingDir, context.Revision!, path);
        if (file is null)
        {
            FileListContextMenu.ReportFailure(context, "Open this revision", error);
        }
        else if (with)
        {
            FileListContextMenu.OpenWith(context, file);
        }
        else
        {
            ShellLauncher.Open(file);
        }
    }

    private static async Task SaveAsAsync(FileMenuContext context, string path)
    {
        string name = Path.GetFileName(path);
        string extension = Path.GetExtension(name).TrimStart('.');
        if (await context.PickSaveFile!("Save selected as", name, extension) is { } target)
        {
            FileListContextMenu.ReportFailure(context, "Save selected as", await RevisionFileCommands.SaveAsync(context.WorkingDir, context.Revision!, path, target));
        }
    }

    private static void Rename(FileMenuContext context, string path)
    {
        if (FileCommands.OwnerOf(context) is not { } owner)
        {
            return;
        }

        QuickDialog.Ask(
            owner,
            "Rename / move",
            $"New path of '{path}' (relative to the repository):",
            "Rename",
            target =>
            {
                string to = target.Trim().Replace('\\', '/');
                if (to.Length > 0 && to != path)
                {
                    _ = RunAsync(context, "Rename / move", () => RevisionFileCommands.MoveAsync(context.WorkingDir, path, to));
                }
            },
            path);
    }

    private static Task StopTrackingAsync(FileMenuContext context)
        => RunAsync(context, "Stop tracking", () => FileCommands.StopTrackingAsync(context.WorkingDir, context.Paths));

    private static async Task RunAsync(FileMenuContext context, string title, Func<Task<string?>> command)
    {
        FileListContextMenu.ReportFailure(context, title, await command());
        if (context.WorkingTreeChanged is { } changed)
        {
            await changed();
        }
    }
}
