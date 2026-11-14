using GitExtensions.Desktop.History;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

// Support for the rich context menus (revision grid, refs tree, file lists): GitHub link, filter focus.
public sealed partial class MainWindowViewModel
{
    private string? _gitHubWebUrl;
    private string? _gitHubCheckedFor;

    /// <summary>Raised when "Advanced filter…" asks the revision grid to show and focus its filter box.</summary>
    public event Action? RevisionFilterFocusRequested;

    /// <summary>
    ///  The web address of the GitHub repository behind the remotes of the open repository, or null when none is on GitHub.
    ///  Read from the config file (no git process) and cached per repository.
    /// </summary>
    public string? GitHubWebUrl
    {
        get
        {
            if (_module is not { } module)
            {
                return null;
            }

            string key = module.WorkingDirGitDir;
            if (_gitHubCheckedFor != key)
            {
                _gitHubCheckedFor = key;
                try
                {
                    string config = Path.Combine(key, "config");
                    _gitHubWebUrl = File.Exists(config) ? GitHubRemote.WebUrlFromConfig(File.ReadAllText(config)) : null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _gitHubWebUrl = null;
                }
            }

            return _gitHubWebUrl;
        }
    }

    /// <summary>Makes sure the left panel (refs tree) is visible, e.g. before one of its inline prompts opens.</summary>
    public void ShowSidebar() => UiRequested?.Invoke(UiTarget.ShowSidebar);

    /// <summary>Shows the filter bar and focuses its text box (the closest thing to the original's advanced filter dialog).</summary>
    public void RequestRevisionFilter()
    {
        ViewOptions.ShowFilterBar = true;
        RevisionFilterFocusRequested?.Invoke();
    }

    /// <summary>Opens the commit on GitHub in the browser; returns false when the remote is not on GitHub.</summary>
    public bool OpenCommitOnGitHub(CommitItemViewModel commit)
    {
        if (GitHubWebUrl is not { } url)
        {
            return false;
        }

        ShellLauncher.OpenUrl(GitHubRemote.CommitUrl(url, commit.ObjectId.ToString()));
        return true;
    }

    /// <summary>Every branch and tag that points at the commit (all of them, whatever the grid shows), current branch first.</summary>
    public IReadOnlyList<RefBadge> RefsOf(CommitItemViewModel commit)
        => _refsByCommit.TryGetValue(commit.ObjectId, out CommitRefs found) && found.Badges is { } badges
            ? RefBadgeBuilder.Arrange(badges, int.MaxValue)
            : [];

    /// <summary>Marks the selected commit with a bisect command (good, bad or skip).</summary>
    public void MarkBisect(System.Windows.Input.ICommand mark)
    {
        Bisect.MarkSelectedCommit = true;
        mark.Execute(null);
    }
}
