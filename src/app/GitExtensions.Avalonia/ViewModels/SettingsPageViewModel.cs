using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>Stable identifiers of the settings pages, used to navigate (for example from the checklist).</summary>
public static class SettingsPageIds
{
    public const string Checklist = "checklist";
    public const string General = "general";
    public const string Appearance = "appearance";
    public const string Sorting = "sorting";
    public const string Colors = "colors";
    public const string Fonts = "fonts";
    public const string ConsoleStyle = "console";
    public const string RevisionLinks = "revisionlinks";
    public const string BuildServer = "buildserver";
    public const string Scripts = "scripts";
    public const string Hotkeys = "hotkeys";
    public const string ShellExtension = "shellextension";
    public const string Advanced = "advanced";
    public const string Confirmations = "confirmations";
    public const string Detailed = "detailed";
    public const string BrowseRepository = "browserepository";
    public const string CommitDialog = "commitdialog";
    public const string DiffViewer = "diffviewer";
    public const string BlameViewer = "blameviewer";
    public const string Ssh = "ssh";
    public const string Git = "git";
    public const string GitPaths = "gitpaths";
    public const string GitConfig = "gitconfig";
    public const string GitAdvanced = "gitadvanced";
    public const string Plugins = "plugins";
}

/// <summary>A node of the settings tree: its title and icon, child pages and the rows (or checklist) shown on the right.</summary>
public sealed partial class SettingsPageViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isTitleMatch;

    public SettingsPageViewModel(string id, string title, string iconName)
    {
        Id = id;
        Title = title;
        IconName = iconName;
    }

    public string Id { get; }

    public string Title { get; }

    public string IconName { get; }

    public SettingsPageViewModel? Parent { get; private set; }

    public ObservableCollection<SettingsPageViewModel> Children { get; } = [];

    public ObservableCollection<SettingRow> Rows { get; } = [];

    /// <summary>The checklist shown instead of rows on the root page.</summary>
    public ChecklistViewModel? Checklist { get; init; }

    public bool IsChecklist => Checklist is not null;

    /// <summary>True for pages backed by git config: they offer the Global / Local / Effective choice.</summary>
    public bool UsesGitConfig => GitConfig is not null;

    public GitConfigStore? GitConfig { get; init; }

    /// <summary>Runs before the rows are saved; returns an error message to refuse the save.</summary>
    public Func<Task<string?>>? Validate { get; init; }

    public SettingsPageViewModel Add(SettingsPageViewModel child)
    {
        child.Parent = this;
        Children.Add(child);
        return this;
    }

    public SettingsPageViewModel With(params SettingRow[] rows)
    {
        foreach (SettingRow row in rows)
        {
            Rows.Add(row);
        }

        return this;
    }

    public IEnumerable<SettingsPageViewModel> Descendants()
    {
        yield return this;
        foreach (SettingsPageViewModel child in Children)
        {
            foreach (SettingsPageViewModel node in child.Descendants())
            {
                yield return node;
            }
        }
    }
}
