using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GitCommands;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>One green or red line of the checklist.</summary>
public sealed record ChecklistItem(string Text, bool Ok, string? FixPageId = null, string? FixText = null)
{
    public bool HasFix => FixPageId is not null;
}

/// <summary>
///  The root page: validates the settings Git Extensions needs to work (git, identity, diff/merge tools, shell tools, ssh, language),
///  like the original checklist page. The shell-extension lines of the original are Windows only and left out.
/// </summary>
public sealed partial class ChecklistViewModel : ObservableObject
{
    private readonly Func<string?> _repositoryDirectory;

    [ObservableProperty]
    private bool _checkAtStartup;

    [ObservableProperty]
    private bool _isScanning;

    public ChecklistViewModel(Func<string?> repositoryDirectory)
    {
        _repositoryDirectory = repositoryDirectory;
        try
        {
            _checkAtStartup = AppSettings.CheckSettings;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
        }
    }

    public ObservableCollection<ChecklistItem> Items { get; } = [];

    public bool AllOk => Items.Count > 0 && Items.All(i => i.Ok);

    public void PersistStartupCheck()
    {
        try
        {
            AppSettings.CheckSettings = CheckAtStartup;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
        }
    }

    public async Task RescanAsync()
    {
        IsScanning = true;
        try
        {
            string git = string.IsNullOrWhiteSpace(AppSettings.GitCommandValue) ? "git" : AppSettings.GitCommandValue.Trim();
            string? repository = _repositoryDirectory();
            List<ChecklistItem> items = [];

            string? version = await ReadVersionAsync(git);
            items.Add(version is not null
                ? new ChecklistItem($"Git {version} is found on your computer.", true)
                : new ChecklistItem("Git is not found: set the path to git.", false, SettingsPageIds.GitPaths, "Set git path"));

            if (version is not null)
            {
                string name = await ReadConfigAsync(git, repository, "user.name");
                string email = await ReadConfigAsync(git, repository, "user.email");
                items.Add(name.Length > 0 && email.Length > 0
                    ? new ChecklistItem("A username and an email address are configured.", true)
                    : new ChecklistItem("A username and an email address need to be configured.", false, SettingsPageIds.GitConfig, "Configure"));

                string merge = await ReadConfigAsync(git, repository, "merge.tool");
                items.Add(merge.Length > 0
                    ? new ChecklistItem($"There is a mergetool configured: {merge}", true)
                    : new ChecklistItem("There is no mergetool configured.", false, SettingsPageIds.GitConfig, "Configure"));

                string diff = await ReadConfigAsync(git, repository, "diff.tool");
                items.Add(diff.Length > 0
                    ? new ChecklistItem($"There is a difftool configured: {diff}", true)
                    : new ChecklistItem("There is no difftool configured.", false, SettingsPageIds.GitConfig, "Configure"));
            }

            string? sh = FindShell();
            items.Add(sh is not null
                ? new ChecklistItem($"Linux tools (sh) found on your computer: {sh}", true)
                : new ChecklistItem("Linux tools (sh) were not found on your computer.", false, SettingsPageIds.GitPaths, "Set path"));

            string ssh = AppSettings.SshPath;
            if (string.IsNullOrWhiteSpace(ssh))
            {
                bool found = FindInPath("ssh") is not null;
                items.Add(found
                    ? new ChecklistItem("Default SSH client, OpenSSH, will be used.", true)
                    : new ChecklistItem("The default SSH client, OpenSSH (ssh), was not found in the PATH.", false, SettingsPageIds.Ssh, "Configure"));
            }
            else
            {
                items.Add(File.Exists(ssh)
                    ? new ChecklistItem($"Other SSH client configured: {ssh}.", true)
                    : new ChecklistItem($"SSH client not found: {ssh}.", false, SettingsPageIds.Ssh, "Configure"));
            }

            string language = AppSettings.Translation;
            items.Add(new ChecklistItem($"The configured language is {(string.IsNullOrWhiteSpace(language) ? "English" : language)}.", true));

            Items.Clear();
            foreach (ChecklistItem item in items)
            {
                Items.Add(item);
            }

            OnPropertyChanged(nameof(AllOk));
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>The first <c>sh</c> in the configured Linux tools folder or in the PATH.</summary>
    public static string? FindShell()
    {
        string configured = AppSettings.LinuxToolsDir;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string candidate = Path.Combine(configured, "sh");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return FindInPath("sh");
    }

    public static string? FindInPath(string program)
    {
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, program);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<string?> ReadVersionAsync(string git)
    {
        try
        {
            GitCli.Result result = await GitCli.RunAsync(git, null, "--version");
            const string prefix = "git version ";
            return result.Succeeded && result.Output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? result.Output[prefix.Length..] : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private static async Task<string> ReadConfigAsync(string git, string? repository, string key)
    {
        try
        {
            GitCli.Result result = await GitCli.RunAsync(git, repository, "config", "--get", key);
            return result.Succeeded ? result.Output : string.Empty;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return string.Empty;
        }
    }
}
