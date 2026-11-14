using GitCommands;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

public static partial class SettingsPageCatalog
{
    private const string NotSet = "(not set)";

    /// <summary>Runs <c>git --version</c> to see whether <paramref name="gitPath"/> is a working git.</summary>
    public static async Task<(bool Ok, string Message)> CheckGitAsync(string gitPath)
    {
        gitPath = string.IsNullOrWhiteSpace(gitPath) ? "git" : gitPath.Trim();
        try
        {
            GitCli.Result result = await GitCli.RunAsync(gitPath, workingDir: null, "--version");
            return result.Succeeded && result.Output.StartsWith("git version", StringComparison.OrdinalIgnoreCase)
                ? (true, result.Output)
                : (false, result.Succeeded ? $"'{gitPath}' does not look like git: {result.Output}" : $"git exited with code {result.ExitCode}: {result.Error}");
        }
        catch (Exception ex)
        {
            return (false, $"Cannot run '{gitPath}': {ex.Message}");
        }
    }

    private static SettingsPageViewModel GitPaths(SettingsContext context)
    {
        _ = context;
        TextRow gitPath = new("Command used to run git (leave empty to use git from the PATH)", () => AppSettings.GitCommandValue, v => AppSettings.GitCommandValue = v.Trim(), immediate: false)
        {
            Browse = BrowseKind.File,
            Watermark = "git",
        };
        ActionRow test = new("Check the git command", "Test", () => CheckGitAsync(gitPath.Value));
        TextRow tools = new("Path to the Linux tools (sh); leave empty when it is in the PATH", () => AppSettings.LinuxToolsDir, v => AppSettings.LinuxToolsDir = v.Trim()) { Browse = BrowseKind.Folder };

        SettingsPageViewModel page = new(SettingsPageIds.GitPaths, "Paths", "FolderOpen")
        {
            Validate = async () =>
            {
                if (!gitPath.IsDirty)
                {
                    return null;
                }

                (bool ok, string message) = await CheckGitAsync(gitPath.Value);
                if (ok)
                {
                    return null;
                }

                gitPath.Load();
                return "The git executable is not valid, nothing was saved. " + message;
            },
        };
        return page.With(
            new HeaderRow("Git"),
            gitPath,
            test,
            new HeaderRow("Environment"),
            tools,
            new NoticeRow($"HOME is set to: {Environment.GetEnvironmentVariable("HOME") ?? "(not set)"}"));
    }

    private static SettingsPageViewModel GitConfig(SettingsContext context, bool advancedPage)
    {
        GitConfigStore store = new(
            () => string.IsNullOrWhiteSpace(AppSettings.GitCommandValue) ? "git" : AppSettings.GitCommandValue.Trim(),
            context.RepositoryDirectory);

        SettingsPageViewModel page = new(
            advancedPage ? SettingsPageIds.GitAdvanced : SettingsPageIds.GitConfig,
            advancedPage ? "Advanced" : "Config",
            advancedPage ? "AdvancedSettings" : "GeneralSettings")
        {
            GitConfig = store,
        };

        if (advancedPage)
        {
            string[] bools = [NotSet, "true", "false"];
            (string Key, string Label, string[] Values)[] keys =
            [
                ("pull.ff", "Pull: fast-forward only or not", [NotSet, "true", "false", "only"]),
                ("pull.rebase", "Pull: rebase instead of merge", [NotSet, "true", "false", "merges", "interactive"]),
                ("fetch.prune", "Fetch: prune deleted remote branches", bools),
                ("merge.autostash", "Merge: stash local changes automatically", bools),
                ("rebase.autostash", "Rebase: stash local changes automatically", bools),
                ("rebase.autosquash", "Rebase: autosquash fixup and squash commits", bools),
                ("rebase.updaterefs", "Rebase: update stacked branches", bools),
                ("rerere.enabled", "Reuse recorded conflict resolutions", bools),
                ("rerere.autoupdate", "Stage files resolved by rerere", bools),
                ("status.showuntrackedfiles", "Status: show untracked files", [NotSet, "no", "normal", "all"]),
                ("core.untrackedcache", "Cache untracked files", [NotSet, "true", "false", "keep"]),
                ("core.fsmonitor", "Use the file system monitor", bools),
            ];
            foreach ((string key, string label, string[] values) in keys)
            {
                page.Rows.Add(Choice(store, key, label, values));
            }
        }
        else
        {
            page.Rows.Add(new HeaderRow("User"));
            page.Rows.Add(Text(store, "user.name", "User name"));
            page.Rows.Add(Text(store, "user.email", "User email"));
            page.Rows.Add(new HeaderRow("Editor and commit"));
            page.Rows.Add(Text(store, "core.editor", "Editor"));
            page.Rows.Add(Text(store, "commit.template", "Path to the commit template", BrowseKind.File));
            page.Rows.Add(Text(store, "credential.helper", "Credential helper"));
            page.Rows.Add(new HeaderRow("Merge and diff tools"));
            page.Rows.Add(Text(store, "merge.tool", "Mergetool"));
            page.Rows.Add(ToolText(store, "merge.tool", "mergetool", "path", "Path to the mergetool", BrowseKind.File));
            page.Rows.Add(ToolText(store, "merge.tool", "mergetool", "cmd", "Mergetool command"));
            page.Rows.Add(Text(store, "diff.tool", "Difftool"));
            page.Rows.Add(ToolText(store, "diff.tool", "difftool", "path", "Path to the difftool", BrowseKind.File));
            page.Rows.Add(ToolText(store, "diff.tool", "difftool", "cmd", "Difftool command"));
            page.Rows.Add(new HeaderRow("Line endings"));
            page.Rows.Add(new ChoiceRow(
                "Convert line endings [core.autocrlf]",
                [NotSet, "true", "input", "false"],
                () => Or(store.Get("core.autocrlf")),
                v => store.Set("core.autocrlf", v == NotSet ? string.Empty : v),
                immediate: false,
                description: "true: checkout Windows-style, commit Unix-style. input: checkout as-is, commit Unix-style. false: checkout and commit as-is."));
        }

        return page;
    }

    private static string Or(string value) => value.Length == 0 ? NotSet : value;

    private static TextRow Text(GitConfigStore store, string key, string label, BrowseKind browse = BrowseKind.None)
        => new($"{label} [{key}]", () => store.Get(key), v => store.Set(key, v), immediate: false) { Browse = browse };

    private static ChoiceRow Choice(GitConfigStore store, string key, string label, string[] values)
        => new($"{label} [{key}]", values, () => Or(store.Get(key)), v => store.Set(key, v == NotSet ? string.Empty : v), immediate: false);

    // mergetool.<tool>.path and friends: the tool name is whatever the tool row currently says.
    private static TextRow ToolText(GitConfigStore store, string toolKey, string section, string name, string label, BrowseKind browse = BrowseKind.None)
        => new(
            label,
            () => store.Current(toolKey) is { Length: > 0 } tool ? store.Get($"{section}.{tool}.{name}") : string.Empty,
            v =>
            {
                if (store.Current(toolKey) is { Length: > 0 } tool)
                {
                    store.Set($"{section}.{tool}.{name}", v);
                }
            },
            immediate: false)
        {
            Browse = browse,
        };
}
