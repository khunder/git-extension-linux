using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

public enum HistoryEditKind
{
    None,
    Fixup,
    Squash,
    Amend,
    FixupAndRebase,
    Edit,
    Reword,
    Autosquash,
    ResetBranch,
    CompareWithRevision,
}

/// <summary>
///  The dialog behind the grid's Advanced and Compare commands that need a choice or a confirmation: fixup / squash / amend
///  commits, edit and reword (a rebase that stops at the commit), autosquash rebase, resetting another branch to the
///  commit and comparing the commit with a branch or revision. Git runs on background threads; failures stay in the dialog.
/// </summary>
public sealed partial class HistoryEditViewModel : ObservableObject
{
    private readonly Func<GitModule?> _getModule;
    private readonly Func<Task> _refresh;
    private readonly Action<string> _reportStatus;
    private CommitItemViewModel? _commit;
    private int _noteVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpen), nameof(Title), nameof(Description), nameof(ConfirmText), nameof(ShowMessage), nameof(ShowChoices), nameof(ChoiceLabel), nameof(IsEditableChoice))]
    private HistoryEditKind _kind;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string? _selectedChoice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote))]
    private string _note = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    [ObservableProperty]
    private bool _isBusy;

    public HistoryEditViewModel(Func<GitModule?> getModule, Func<Task> refresh, Action<string> reportStatus)
    {
        _getModule = getModule;
        _refresh = refresh;
        _reportStatus = reportStatus;
    }

    /// <summary>Raised to show two revisions in the Diff tab (older side first); set by the main view-model.</summary>
    public Action<ObjectId, ObjectId>? CompareRequested { get; set; }

    public ObservableCollection<string> Choices { get; } = [];

    public bool IsOpen
    {
        get => Kind != HistoryEditKind.None;
        set
        {
            if (!value)
            {
                Close();
            }
        }
    }

    public bool HasNote => Note.Length > 0;

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool ShowMessage => Kind is HistoryEditKind.Amend or HistoryEditKind.Reword;

    public bool ShowChoices => Kind is HistoryEditKind.ResetBranch or HistoryEditKind.CompareWithRevision;

    public bool IsEditableChoice => Kind == HistoryEditKind.CompareWithRevision;

    public string ChoiceLabel => Kind == HistoryEditKind.ResetBranch ? "Branch:" : "Compare with:";

    public string Title => Kind switch
    {
        HistoryEditKind.Fixup => "Create a fixup commit",
        HistoryEditKind.Squash => "Create a squash commit",
        HistoryEditKind.Amend => "Create an amend commit",
        HistoryEditKind.FixupAndRebase => "Fixup commit and squash-rebase",
        HistoryEditKind.Edit => "Edit commit",
        HistoryEditKind.Reword => "Reword commit",
        HistoryEditKind.Autosquash => "Autosquash rebase",
        HistoryEditKind.ResetBranch => "Reset another branch",
        HistoryEditKind.CompareWithRevision => "Compare with a branch or revision",
        _ => string.Empty,
    };

    public string ConfirmText => Kind switch
    {
        HistoryEditKind.Fixup or HistoryEditKind.Squash or HistoryEditKind.Amend => "Create commit",
        HistoryEditKind.FixupAndRebase => "Fixup and rebase",
        HistoryEditKind.Edit => "Start edit",
        HistoryEditKind.Reword => "Reword",
        HistoryEditKind.Autosquash => "Rebase",
        HistoryEditKind.ResetBranch => "Reset branch",
        HistoryEditKind.CompareWithRevision => "Compare",
        _ => "OK",
    };

    public string Description
    {
        get
        {
            string commit = _commit is null ? "the commit" : $"{_commit.ShortHash} ({_commit.Subject})";
            return Kind switch
            {
                HistoryEditKind.Fixup => $"Creates a 'fixup!' commit from the staged changes for {commit}. A later autosquash rebase folds it into that commit.",
                HistoryEditKind.Squash => $"Creates a 'squash!' commit from the staged changes for {commit}. A later autosquash rebase squashes it into that commit and lets you combine the messages.",
                HistoryEditKind.Amend => $"Creates an 'amend!' commit for {commit} with the message below (and the staged changes, if any). A later autosquash rebase replaces the message of that commit.",
                HistoryEditKind.FixupAndRebase => $"Creates a fixup commit of the staged changes for {commit}, then rebases the current branch with autosquash so the changes end up in that commit. This rewrites history.",
                HistoryEditKind.Edit => $"Starts a rebase that stops at {commit} so you can change it (amend, split, ...); continue or abort in the operation bar. This rewrites history when continued.",
                HistoryEditKind.Reword => $"Changes the message of {commit} by rebasing the current branch. This rewrites the history of every commit after it.",
                HistoryEditKind.Autosquash => $"Rebases the current branch onto the parent of {commit} and folds every fixup!/squash! commit into its target. This rewrites history.",
                HistoryEditKind.ResetBranch => $"Moves the chosen branch to {commit}. The files of the working directory are not touched.",
                HistoryEditKind.CompareWithRevision => $"Shows the changes between {commit} and the chosen branch, tag or revision in the Diff tab.",
                _ => string.Empty,
            };
        }
    }

    /// <summary>Opens the dialog for <paramref name="kind"/> on <paramref name="commit"/> (loading the branches the dialog offers).</summary>
    public async Task OpenAsync(HistoryEditKind kind, CommitItemViewModel commit)
    {
        _commit = commit;
        Message = string.Empty;
        Note = string.Empty;
        Error = null;
        SelectedChoice = null;
        Choices.Clear();
        OnPropertyChanged(nameof(Description));
        Kind = kind;

        GitModule? module = _getModule();
        if (module is null)
        {
            return;
        }

        if (kind is HistoryEditKind.Amend or HistoryEditKind.Reword)
        {
            string sha = commit.ObjectId.ToString();
            string body = await Task.Run(() => module.GitExecutable.Execute(new GitArgumentBuilder("log") { "-1", "--format=%B", sha }, throwOnErrorExit: false).StandardOutput.TrimEnd());
            if (Kind == kind)
            {
                Message = body.Length > 0 ? body : commit.Subject;
            }
        }
        else if (kind is HistoryEditKind.ResetBranch or HistoryEditKind.CompareWithRevision)
        {
            string[] choices = await Task.Run(() => LoadChoices(module, kind));
            if (Kind != kind)
            {
                return;
            }

            foreach (string choice in choices)
            {
                Choices.Add(choice);
            }

            SelectedChoice = kind == HistoryEditKind.CompareWithRevision ? Choices.FirstOrDefault() : null;
            if (kind == HistoryEditKind.ResetBranch && Choices.Count == 0)
            {
                Error = "There is no other local branch to reset.";
            }
        }
    }

    [RelayCommand]
    public void Close()
    {
        if (IsBusy)
        {
            return;
        }

        Kind = HistoryEditKind.None;
        _commit = null;
        Error = null;
        Note = string.Empty;
    }

    /// <summary>Runs the chosen operation; a failure stays in the dialog, success closes it.</summary>
    [RelayCommand]
    public async Task ConfirmAsync()
    {
        GitModule? module = _getModule();
        CommitItemViewModel? commit = _commit;
        HistoryEditKind kind = Kind;
        if (module is null || commit is null || kind == HistoryEditKind.None || IsBusy)
        {
            return;
        }

        IsBusy = true;
        Error = null;
        string? failure;
        try
        {
            failure = await RunAsync(module, commit, kind);
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        if (failure is not null)
        {
            Error = failure;
            await _refresh();
            return;
        }

        Close();
    }

    partial void OnSelectedChoiceChanged(string? value)
    {
        if (Kind == HistoryEditKind.ResetBranch)
        {
            _ = UpdateResetNoteAsync(value);
        }
    }

    private static string[] LoadChoices(GitModule module, HistoryEditKind kind)
    {
        IExecutable git = module.GitExecutable;
        string current = module.GetSelectedBranch(emptyIfDetached: true);
        if (kind == HistoryEditKind.ResetBranch)
        {
            ExecutionResult local = git.Execute(new GitArgumentBuilder("for-each-ref") { "--format=%(refname:short)", "refs/heads" }, throwOnErrorExit: false);
            return [.. Lines(local).Where(b => b != current)];
        }

        ExecutionResult all = git.Execute(new GitArgumentBuilder("for-each-ref") { "--format=%(refname:short)", "refs/heads", "refs/remotes", "refs/tags" }, throwOnErrorExit: false);
        return ["HEAD", .. Lines(all).Where(r => !r.EndsWith("/HEAD", StringComparison.Ordinal))];
    }

    private static IEnumerable<string> Lines(ExecutionResult result)
        => result.ExitedSuccessfully ? result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];

    // Tells before confirming what moving the branch does: how many of its commits it will no longer reach.
    private async Task UpdateResetNoteAsync(string? branch)
    {
        int version = ++_noteVersion;
        GitModule? module = _getModule();
        if (module is null || _commit is null || string.IsNullOrEmpty(branch))
        {
            Note = string.Empty;
            return;
        }

        string sha = _commit.ObjectId.ToString();
        string text = await Task.Run(() =>
        {
            ExecutionResult lost = module.GitExecutable.Execute(new GitArgumentBuilder("rev-list") { "--count", $"{sha}..{branch.Quote()}" }, throwOnErrorExit: false);
            return int.TryParse(lost.StandardOutput.Trim(), out int count) && count > 0
                ? $"'{branch}' has {count} commit{(count == 1 ? string.Empty : "s")} that {(count == 1 ? "is" : "are")} not in the history of the target. {(count == 1 ? "It" : "They")} will no longer be reachable from the branch (still in the reflog)."
                : $"'{branch}' will move to a commit that contains everything it has now (a fast-forward).";
        });
        if (version == _noteVersion)
        {
            Note = text;
        }
    }

    private async Task<string?> RunAsync(GitModule module, CommitItemViewModel commit, HistoryEditKind kind)
    {
        IExecutable git = module.GitExecutable;
        string sha = commit.ObjectId.ToString();
        bool hasParent = commit.Revision.HasParent;
        string[] rebaseBase = hasParent ? [$"{sha}^"] : ["--root"];

        switch (kind)
        {
            case HistoryEditKind.Fixup:
            case HistoryEditKind.Squash:
            case HistoryEditKind.FixupAndRebase:
            {
                string? problem = await Task.Run(() => RequireStaged(git));
                if (problem is not null)
                {
                    return problem;
                }

                string flag = kind == HistoryEditKind.Squash ? "--squash" : "--fixup";
                GitRunResult commitResult = await GitHistoryRunner.RunAsync(git, null, null, "commit", $"{flag}={sha}");
                if (!commitResult.Succeeded)
                {
                    return $"git commit failed: {commitResult.Message}";
                }

                if (kind == HistoryEditKind.FixupAndRebase)
                {
                    return await RebaseAsync(module, null, null, "rebase", "-i", "--autosquash", "--autostash", rebaseBase[0]);
                }

                _reportStatus(kind == HistoryEditKind.Squash ? $"Created a squash commit for {commit.ShortHash}" : $"Created a fixup commit for {commit.ShortHash}");
                await _refresh();
                return null;
            }

            case HistoryEditKind.Amend:
            {
                string file = Path.GetTempFileName();
                try
                {
                    if (Message.Trim().Length == 0)
                    {
                        return "The message must not be empty.";
                    }

                    // The first line marks the commit for autosquash; the rest replaces the message of the target.
                    await File.WriteAllTextAsync(file, $"amend! {commit.Subject}\n\n{Message.Trim()}\n");
                    GitRunResult result = await GitHistoryRunner.RunAsync(git, null, GitHistoryRunner.UseMessageFile(file), "commit", $"--fixup=amend:{sha}");
                    if (!result.Succeeded)
                    {
                        return $"git commit failed: {result.Message}";
                    }
                }
                finally
                {
                    TryDelete(file);
                }

                _reportStatus($"Created an amend commit for {commit.ShortHash}");
                await _refresh();
                return null;
            }

            case HistoryEditKind.Edit:
                return await RebaseAsync(module, GitHistoryRunner.ChangeFirstPick("edit"), null, ["rebase", "-i", "--autostash", .. rebaseBase]);

            case HistoryEditKind.Reword:
            {
                if (Message.Trim().Length == 0)
                {
                    return "The message must not be empty.";
                }

                string file = Path.GetTempFileName();
                try
                {
                    await File.WriteAllTextAsync(file, Message.Trim() + "\n");
                    return await RebaseAsync(module, GitHistoryRunner.ChangeFirstPick("reword"), GitHistoryRunner.UseMessageFile(file), ["rebase", "-i", "--autostash", .. rebaseBase]);
                }
                finally
                {
                    TryDelete(file);
                }
            }

            case HistoryEditKind.Autosquash:
                return await RebaseAsync(module, null, null, ["rebase", "-i", "--autosquash", "--autostash", .. rebaseBase]);

            case HistoryEditKind.ResetBranch:
            {
                if (string.IsNullOrEmpty(SelectedChoice))
                {
                    return "Choose the branch to reset.";
                }

                string branch = SelectedChoice;
                GitRunResult result = await GitHistoryRunner.RunAsync(git, null, null, "branch", "-f", "--", branch, sha);
                if (!result.Succeeded)
                {
                    return $"git branch failed: {result.Message}";
                }

                _reportStatus($"Reset {branch} to {commit.ShortHash}");
                await _refresh();
                return null;
            }

            case HistoryEditKind.CompareWithRevision:
            {
                string revision = SelectedChoice?.Trim() ?? string.Empty;
                string? error = RefNameValidator.PreCheck(revision);
                if (error is not null)
                {
                    return error;
                }

                ExecutionResult resolved = await Task.Run(() => git.Execute(new GitArgumentBuilder("rev-parse") { "--verify", "--quiet", $"{revision}^{{commit}}".Quote() }, throwOnErrorExit: false));
                if (!resolved.ExitedSuccessfully || !ObjectId.TryParse(resolved.StandardOutput.Trim(), out ObjectId other))
                {
                    return $"'{revision}' is not a commit, branch or tag.";
                }

                CompareRequested?.Invoke(commit.ObjectId, other);
                return null;
            }
        }

        return null;
    }

    // A rebase that stops (for the edit, or on conflicts) is not a failure: the operation bar takes over.
    private async Task<string?> RebaseAsync(GitModule module, string? sequenceEditor, string? messageEditor, params string[] arguments)
    {
        GitRunResult result = await GitHistoryRunner.RunAsync(module.GitExecutable, sequenceEditor, messageEditor, arguments);
        OperationKind state = await Task.Run(() => RepositoryOperationState.Detect(module.WorkingDirGitDir));
        if (!result.Succeeded && state == OperationKind.None)
        {
            return $"git rebase failed: {result.Message}";
        }

        _reportStatus(state == OperationKind.Rebase
            ? result.Succeeded ? "The rebase stopped at the commit: change it, then continue or abort in the operation bar." : "The rebase stopped on conflicts. Resolve them, then continue or abort in the operation bar."
            : "Rebase finished.");
        await _refresh();
        return null;
    }

    private static string? RequireStaged(IExecutable git)
    {
        ExecutionResult staged = git.Execute(new GitArgumentBuilder("diff") { "--cached", "--quiet" }, throwOnErrorExit: false);
        return staged.ExitCode == 1 ? null : staged.ExitedSuccessfully ? "Nothing is staged. Stage the changes that belong to the commit first." : $"git diff failed: {staged.StandardError.Trim()}";
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
            // Best effort cleanup of a temp file.
        }
    }
}
