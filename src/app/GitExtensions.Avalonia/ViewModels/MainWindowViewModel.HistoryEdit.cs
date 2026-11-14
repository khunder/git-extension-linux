using CommunityToolkit.Mvvm.ComponentModel;
using GitCommands;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

// The grid context menu's Advanced, Compare and Navigate commands that go beyond the plain checkout/merge family.
public sealed partial class MainWindowViewModel
{
    private HistoryEditViewModel? _historyEdit;

    /// <summary>The commit remembered by "Select as BASE to compare".</summary>
    [ObservableProperty]
    private CommitItemViewModel? _compareBase;

    /// <summary>The dialog of the fixup, squash, amend, edit, reword, autosquash, reset-another-branch and compare commands.</summary>
    public HistoryEditViewModel HistoryEdit => _historyEdit ??= CreateHistoryEdit();

    /// <summary>Opens the history editing dialog for a commit.</summary>
    public Task OpenHistoryEditAsync(HistoryEditKind kind, CommitItemViewModel commit) => HistoryEdit.OpenAsync(kind, commit);

    /// <summary>Compares a commit with the commit that is checked out (HEAD) in the Diff tab.</summary>
    public async Task CompareWithCurrentBranchAsync(CommitItemViewModel commit)
    {
        if (_module is { } module && await ResolveAsync(module, "HEAD") is { } head)
        {
            RevisionTabs.ShowDiff(module, commit.ObjectId, head);
        }
    }

    /// <summary>Compares a commit with the working directory in the Diff tab.</summary>
    public void CompareWithWorkingDirectory(CommitItemViewModel commit)
    {
        if (_module is { } module)
        {
            RevisionTabs.ShowDiffToWorkingDirectory(module, commit.ObjectId);
        }
    }

    /// <summary>Compares the remembered BASE commit with <paramref name="commit"/> in the Diff tab.</summary>
    public void CompareToBase(CommitItemViewModel commit)
    {
        if (_module is { } module && CompareBase is { } baseCommit)
        {
            RevisionTabs.ShowDiff(module, baseCommit.ObjectId, commit.ObjectId);
        }
    }

    /// <summary>Opens the configured difftool on the changes of a commit (against its first parent).</summary>
    public async Task OpenDifftoolAsync(CommitItemViewModel commit)
    {
        if (_module is not { } module)
        {
            return;
        }

        string sha = commit.ObjectId.ToString();
        Status = "Starting the difftool…";
        ExecutionResult result = await Task.Run(() => module.GitExecutable.Execute(
            new GitArgumentBuilder("difftool") { "--dir-diff", "--no-prompt", commit.Revision.HasParent ? $"{sha}^" : "4b825dc642cb6eb9a060e54bf8d69288fbee4904", sha },
            throwOnErrorExit: false));
        Status = result.ExitedSuccessfully ? "The difftool was closed." : string.Empty;
        if (!result.ExitedSuccessfully)
        {
            ErrorMessage = $"git difftool failed: {(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError).Trim()}";
        }
    }

    /// <summary>Selects the common ancestor of a commit and the current commit (git merge-base).</summary>
    public async Task GoToMergeBaseAsync(CommitItemViewModel commit)
    {
        if (_module is not { } module)
        {
            return;
        }

        string sha = commit.ObjectId.ToString();
        ExecutionResult result = await Task.Run(() => module.GitExecutable.Execute(new GitArgumentBuilder("merge-base") { "HEAD", sha }, throwOnErrorExit: false));
        if (result.ExitedSuccessfully && ObjectId.TryParse(result.StandardOutput.Trim(), out ObjectId ancestor))
        {
            if (Commits.Any(c => c.ObjectId == ancestor))
            {
                SelectCommit(ancestor);
            }
            else
            {
                Status = $"The common ancestor {ancestor.ToShortString()} is not in the loaded history.";
            }
        }
        else
        {
            Status = "The commit has no common ancestor with the current commit.";
        }
    }

    /// <summary>Opens the submodule at a path of this repository (relative, forward slashes) as the current repository.</summary>
    public async Task OpenSubmoduleAsync(string relativePath)
    {
        string path = Path.GetFullPath(Path.Combine(RepositoryPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(Path.Combine(path, ".git")) && !Directory.Exists(Path.Combine(path, ".git")))
        {
            ErrorMessage = $"The submodule '{relativePath}' is not initialized: update it first (Manage submodules).";
            return;
        }

        await OpenAsync(path);
    }

    private HistoryEditViewModel CreateHistoryEdit()
    {
        HistoryEditViewModel edit = new(() => _module, () => OpenAsync(RepositoryPath), message => Status = message);
        edit.CompareRequested = (from, to) =>
        {
            if (_module is { } module)
            {
                RevisionTabs.ShowDiff(module, from, to);
            }
        };
        return edit;
    }

    private static async Task<ObjectId?> ResolveAsync(GitModule module, string revision)
    {
        ExecutionResult result = await Task.Run(() => module.GitExecutable.Execute(new GitArgumentBuilder("rev-parse") { "--verify", "--quiet", $"{revision}^{{commit}}" }, throwOnErrorExit: false));
        return result.ExitedSuccessfully && ObjectId.TryParse(result.StandardOutput.Trim(), out ObjectId id) ? id : null;
    }
}
