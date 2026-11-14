using Avalonia.Controls;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.Services;
using GitExtensions.Desktop.ViewModels;

namespace GitExtensions.Desktop.Views.ContextMenus;

/// <summary>
///  The context menu of the diff of a file (Diff tab, Commit window): copy, stage/unstage/discard the selected lines or
///  the hunk under the pointer, stage the file; navigation and view options of the original editor are shown disabled.
/// </summary>
public static class DiffContextMenu
{
    public static List<Control> Build(FileDiffViewModel diff, DiffLine? row, IClipboardService? clipboard)
    {
        DiffLine? hunk = row is { HunkIndex: >= 0 }
            ? diff.Lines.FirstOrDefault(l => l.Kind == DiffLineKind.Hunk && l.HunkIndex == row.HunkIndex)
            : null;
        string selected = string.Join('\n', diff.Lines.Where(l => l.IsSelected).Select(l => l.Text));
        string all = string.Join('\n', diff.Lines.Select(l => l.Text));
        bool hasFile = diff.File is not null;

        return MenuKit.Tidy(
        [
            MenuKit.Item("Copy", "CopyToClipboard", () => _ = clipboard?.SetTextAsync(selected), "Ctrl+C", enabled: selected.Length > 0 && clipboard is not null, disabledTip: "Select lines first"),
            MenuKit.Item("Copy the whole diff", "CopyToClipboard", () => _ = clipboard?.SetTextAsync(all), enabled: all.Length > 0 && clipboard is not null),
            MenuKit.Sep(),
            MenuKit.Item("Stage selected line(s)", "Stage", () => diff.StageSelectedCommand.Execute(null), "S", enabled: diff.CanStageSelected, disabledTip: "Select changed lines of an unstaged file"),
            MenuKit.Item("Unstage selected line(s)", "Unstage", () => diff.UnstageSelectedCommand.Execute(null), "U", enabled: diff.CanUnstageSelected, disabledTip: "Select changed lines of a staged file"),
            MenuKit.Item("Reset selected line(s)", "ResetFileTo", () => diff.DiscardSelectedCommand.Execute(null), "R", enabled: diff.CanDiscardSelected, disabledTip: "Select changed lines of an unstaged file"),
            MenuKit.Sep(),
            MenuKit.Item("Stage hunk", "Stage", () => diff.StageHunkCommand.Execute(hunk), enabled: hunk is { CanStageHunk: true }, disabledTip: "Right-click a line of a hunk of an unstaged file"),
            MenuKit.Item("Unstage hunk", "Unstage", () => diff.UnstageHunkCommand.Execute(hunk), enabled: hunk is { CanUnstageHunk: true }, disabledTip: "Right-click a line of a hunk of a staged file"),
            MenuKit.Item("Discard hunk…", "ResetFileTo", () => diff.DiscardHunkCommand.Execute(hunk), enabled: hunk is { CanDiscardHunk: true }, disabledTip: "Right-click a line of a hunk of an unstaged file"),
            MenuKit.Sep(),
            MenuKit.Item("Stage file", "StageAll", () => diff.StageCommand.Execute(null), enabled: diff.StageCommand.CanExecute(null), disabledTip: "The file is already staged"),
            MenuKit.Item("Unstage file", "UnstageAll", () => diff.UnstageCommand.Execute(null), enabled: diff.UnstageCommand.CanExecute(null), disabledTip: "The file is not staged"),
            MenuKit.Sep(),
            MenuKit.Item("Copy file path", "CopyToClipboard", () => _ = clipboard?.SetTextAsync(diff.File!.Name), enabled: hasFile && clipboard is not null),
            MenuKit.Sep(),
            MenuKit.Stub("Next change", "ArrowDown"),
            MenuKit.Stub("Previous change", "ArrowUp"),
            MenuKit.Stub("Show entire file", "ShowEntireFile"),
            MenuKit.Stub("Ignore whitespace changes", "WhitespaceIgnore"),
        ]);
    }
}
