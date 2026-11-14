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
}

/// <summary>
///  The context menu of the changed-file lists (Diff tab, Commit tab, Commit window, file tree), after the original's
///  FileStatusList menu: open, show in folder, history, blame, stage/unstage/reset, copy path, ignore, delete.
/// </summary>
public static class FileListContextMenu
{
    public static List<Control> Build(FileMenuContext context)
    {
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
        items.Add(MenuKit.Stub("Open with…", "FolderOpen"));
        items.Add(MenuKit.Stub("Edit file", "EditFile"));
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

        items.Add(MenuKit.Stub("Stop tracking the file", "StopTrackingFile"));
        items.Add(MenuKit.Stub("Delete…", "DeleteFile"));
        return MenuKit.Tidy(items);
    }
}
