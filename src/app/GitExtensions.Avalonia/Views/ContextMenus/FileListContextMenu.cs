using Avalonia.Controls;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.Views.ContextMenus;

/// <summary>
///  What the file context menu acts on and which index operations the list offers (null = the list has no such operation).
/// </summary>
public sealed class FileMenuContext
{
    /// <summary>The files (or folders) right-clicked, as repository-relative paths with forward slashes.</summary>
    public required IReadOnlyList<string> Paths { get; init; }

    public required string WorkingDir { get; init; }

    /// <summary>The revision the list shows (for the history and blame views), or null for the working tree.</summary>
    public string? Revision { get; init; }

    /// <summary>Opens the history (or the blame) of a path.</summary>
    public Action<string, bool>? OpenHistory { get; init; }

    public IClipboardService? Clipboard { get; init; }

    public Action? Stage { get; init; }

    public Action? Unstage { get; init; }

    public Action? Reset { get; init; }

    /// <summary>Appends the paths to .gitignore (only offered for untracked files).</summary>
    public Action? AddToGitIgnore { get; init; }

    public bool IsFolder { get; init; }

    /// <summary>The window that owns the dialogs of the menu's commands (defaults to the topmost dialog or the main window).</summary>
    public Window? Owner { get; init; }

    /// <summary>Refreshes the list after a command changed the working directory or the index; null when the list shows no working directory files.</summary>
    public Func<Task>? Changed { get; init; }

    /// <summary>The other side of the diff of a commit's files (null: the first parent of <see cref="Revision"/>).</summary>
    public string? BaseRevision { get; init; }

    /// <summary>Selects the path in the File tree tab.</summary>
    public Action<string>? ShowInFileTree { get; init; }

    /// <summary>Shows only the commits that touch the path in the history.</summary>
    public Action<string>? FilterInGrid { get; init; }

    /// <summary>Refreshes the working directory changes after a command of a commit's file menu changed them (reset, cherry-pick, rename).</summary>
    public Func<Task>? WorkingTreeChanged { get; init; }

    /// <summary>Asks where to save a file (title, suggested name, extension); null when cancelled.</summary>
    public Func<string, string, string, Task<string?>>? PickSaveFile { get; init; }
}

/// <summary>
///  The context menu of the changed-file lists (Diff tab, Commit tab, Commit window, file tree), after the original's
///  FileStatusList menu: open, show in folder, history, blame, stage/unstage/reset, copy path, ignore, delete.
/// </summary>
public static class FileListContextMenu
{
    public static List<Control> Build(FileMenuContext context)
    {
        if (context.Revision is not null && !context.IsFolder)
        {
            return RevisionFileContextMenu.Build(context);
        }

        string first = context.Paths[0];
        bool single = context.Paths.Count == 1;
        string full = Path.Combine(context.WorkingDir, first);
        bool exists = !context.IsFolder && File.Exists(full);
        bool folderExists = Directory.Exists(context.IsFolder ? full : Path.GetDirectoryName(full) ?? string.Empty);
        List<Control> items = [];

        items.Add(MenuKit.Item(
            "Open",
            "ViewFile",
            () => ShellLauncher.Open(full),
            enabled: single && exists,
            disabledTip: single ? "The file does not exist in the working directory" : "Select a single file"));
        items.Add(MenuKit.Item(
            "Open with…",
            "FolderOpen",
            () => OpenWith(context, full),
            enabled: single && exists,
            disabledTip: single ? "The file does not exist in the working directory" : "Select a single file"));
        items.Add(MenuKit.Item(
            "Edit file",
            "EditFile",
            () => ReportFailure(context, "Edit file", FileCommands.Edit(context.WorkingDir, full)),
            enabled: single && exists,
            disabledTip: single ? "The file does not exist in the working directory" : "Select a single file",
            tip: "Opens the file in the editor configured for git (or the desktop's default editor)"));
        items.Add(MenuKit.Item(
            "Show in folder",
            "BrowseFileExplorer",
            () => ShellLauncher.Open(context.IsFolder ? full : Path.GetDirectoryName(full)!),
            enabled: single && folderExists,
            disabledTip: "The folder does not exist in the working directory"));
        items.Add(MenuKit.Sep());

        if (context.OpenHistory is { } openHistory)
        {
            items.Add(MenuKit.Item("File history", "FileHistory", () => openHistory(first, false), enabled: single && !context.IsFolder, disabledTip: "Select a single file"));
            items.Add(MenuKit.Item("Blame", "Blame", () => openHistory(first, true), enabled: single && !context.IsFolder, disabledTip: "Select a single file"));
            items.Add(MenuKit.Sep());
        }

        if (context.Stage is { } stage)
        {
            items.Add(MenuKit.Item("Stage", "Stage", stage, "S"));
        }

        if (context.Unstage is { } unstage)
        {
            items.Add(MenuKit.Item("Unstage", "Unstage", unstage, "U"));
        }

        if (context.Reset is { } reset)
        {
            items.Add(MenuKit.Item("Reset file or directory changes…", "ResetFileTo", reset, "R"));
        }

        if (context.Stage is not null || context.Unstage is not null || context.Reset is not null)
        {
            items.Add(MenuKit.Sep());
        }

        IClipboardService? clipboard = context.Clipboard;
        string fullPaths = string.Join('\n', context.Paths.Select(p => Path.Combine(context.WorkingDir, p)));
        string relative = string.Join('\n', context.Paths);
        string names = string.Join('\n', context.Paths.Select(p => p[(p.LastIndexOf('/') + 1)..]));
        items.Add(MenuKit.Sub(
            "Copy path",
            "CopyToClipboard",
            [
                MenuKit.Item("Full path", "CopyToClipboard", () => _ = clipboard?.SetTextAsync(fullPaths), enabled: clipboard is not null),
                MenuKit.Item("Relative path", "CopyToClipboard", () => _ = clipboard?.SetTextAsync(relative), enabled: clipboard is not null),
                MenuKit.Item("File name", "CopyToClipboard", () => _ = clipboard?.SetTextAsync(names), enabled: clipboard is not null),
            ]));
        items.Add(MenuKit.Sep());

        if (context.AddToGitIgnore is { } ignore)
        {
            items.Add(MenuKit.Item("Add to .gitignore", "AddToGitIgnore", ignore));
        }
        else if (context.Stage is not null || context.Unstage is not null)
        {
            items.Add(MenuKit.Item("Add to .gitignore", "AddToGitIgnore", enabled: false, disabledTip: "Only untracked, unstaged files can be added to .gitignore"));
        }
        else
        {
            items.Add(MenuKit.Stub("Add to .gitignore", "AddToGitIgnore"));
        }

        bool working = context.Changed is not null && context.Revision is null;
        items.Add(MenuKit.Item(
            "Stop tracking the file",
            "StopTrackingFile",
            () => _ = StopTrackingAsync(context),
            enabled: working,
            disabledTip: "Only files of the working directory can be removed from the index",
            tip: "git rm --cached: the file stays on disk but is no longer tracked"));
        items.Add(MenuKit.Item(
            "Delete…",
            "DeleteFile",
            () => ConfirmDelete(context),
            enabled: working,
            disabledTip: "Only files of the working directory can be deleted"));
        return MenuKit.Tidy(items);
    }

    internal static void OpenWith(FileMenuContext context, string full)
    {
        if (FileCommands.OwnerOf(context) is not { } owner)
        {
            return;
        }

        QuickDialog.Ask(
            owner,
            "Open with",
            $"Program (and arguments) to open '{Path.GetFileName(full)}' with:",
            "Open",
            program =>
            {
                FileCommands.LastOpenWith = program.Trim();
                ReportFailure(context, "Open with", FileCommands.StartWith(program, full));
            },
            FileCommands.LastOpenWith,
            watermark: "e.g. code, gedit, kate");
    }

    private static void ConfirmDelete(FileMenuContext context)
    {
        if (FileCommands.OwnerOf(context) is not { } owner)
        {
            return;
        }

        string what = context.Paths.Count == 1 ? $"'{context.Paths[0]}'" : $"{context.Paths.Count} files";
        QuickDialog.Ask(
            owner,
            "Delete",
            $"Delete {what} from the working directory? This cannot be undone (tracked files can be restored with a reset).",
            "Delete",
            unused =>
            {
                ReportFailure(context, "Delete", FileCommands.Delete(context.WorkingDir, context.Paths));
                _ = context.Changed?.Invoke();
            },
            destructive: true);
    }

    internal static async Task StopTrackingAsync(FileMenuContext context)
    {
        string? error = await FileCommands.StopTrackingAsync(context.WorkingDir, context.Paths);
        ReportFailure(context, "Stop tracking", error);
        if (context.Changed is { } changed)
        {
            await changed();
        }
    }

    internal static void ReportFailure(FileMenuContext context, string title, string? error)
    {
        if (!string.IsNullOrEmpty(error) && FileCommands.OwnerOf(context) is { } owner)
        {
            QuickDialog.Notice(owner, title, error);
        }
    }
}
