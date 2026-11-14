using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Extensibility;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The commit area: message, amend flag and the commit command.
/// </summary>
public sealed partial class CommitViewModel : ObservableObject, IDisposable
{
    private readonly GitModule _module;
    private readonly IMessageDialogs _messages;
    private readonly Func<Task> _afterCommitAsync;
    private readonly ObservableCollection<ChangedFileViewModel> _changes;
    private string _draftMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    private bool _amend;

    [ObservableProperty]
    private bool _signOff;

    /// <summary>Amend only: also overwrite the author and the author date of the commit (git commit --reset-author).</summary>
    [ObservableProperty]
    private bool _resetAuthor;

    /// <summary>Amend only: soft-reset to the previous commit (index and working tree untouched) and commit the staged changes as a new commit.</summary>
    [ObservableProperty]
    private bool _resetSoft;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    private bool _hasHead;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    private bool _isCommitting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _stagedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _unstagedCount;

    public CommitViewModel(GitModule module, ObservableCollection<ChangedFileViewModel> changes, IMessageDialogs messages, Func<Task> afterCommitAsync)
    {
        _module = module;
        _changes = changes;
        _messages = messages;
        _afterCommitAsync = afterCommitAsync;
        _changes.CollectionChanged += OnChangesChanged;
        UpdateCounts();
    }

    public string Summary => $"{StagedCount} staged · {UnstagedCount} unstaged";

    /// <summary>
    ///  Determines whether there is a HEAD commit and prefills the message from a pending merge/commit message.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            (bool hasHead, string pending) = await Task.Run(() => (GetHasHead(), ReadPendingMessage()));
            HasHead = hasHead;
            if (string.IsNullOrEmpty(Message) && !Amend)
            {
                Message = pending;
            }
        }
        catch (Exception ex)
        {
            _messages.ShowError(null, $"Could not prepare the commit message: {ex.Message}");
        }
    }

    public void Dispose() => _changes.CollectionChanged -= OnChangesChanged;

    partial void OnAmendChanged(bool value)
    {
        if (value)
        {
            _draftMessage = Message;
            _ = LoadPreviousMessageAsync();
        }
        else
        {
            ResetAuthor = false;
            ResetSoft = false;
            Message = _draftMessage;
        }
    }

    private bool CanCommit()
        => !IsCommitting && !string.IsNullOrWhiteSpace(Message) && (StagedCount > 0 || (Amend && HasHead));

    /// <summary>
    ///  Raised after a commit was created, with the message that was used.
    /// </summary>
    public event Action<string>? Committed;

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private async Task CommitAsync()
    {
        if (await TryCommitAsync())
        {
            await _afterCommitAsync();
        }
    }

    /// <summary>
    ///  Commits the staged changes (or amends) with the current message. Returns <see langword="false"/> and sets
    ///  <see cref="ErrorText"/> (and reports through the message dialogs) when git refuses; the message is kept then.
    /// </summary>
    public async Task<bool> TryCommitAsync()
    {
        string message = Message;
        bool amend = Amend;
        bool signOff = SignOff;
        bool resetAuthor = amend && ResetAuthor;
        bool resetSoft = amend && ResetSoft;
        ErrorText = null;
        IsCommitting = true;
        string? error;
        try
        {
            error = await Task.Run(() => RunCommit(message, amend, signOff, resetAuthor, resetSoft));
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        finally
        {
            IsCommitting = false;
        }

        if (error is not null)
        {
            ErrorText = error;
            _messages.ShowError(null, error, "Commit failed");
            return false;
        }

        _draftMessage = string.Empty;
        Amend = false;
        Message = string.Empty;
        Committed?.Invoke(message);
        return true;
    }

    private async Task LoadPreviousMessageAsync()
    {
        try
        {
            string previous = await Task.Run(() => _module.GitExecutable.Execute(new GitArgumentBuilder("log") { "-1", "--format=%B" }, throwOnErrorExit: false) is { ExitedSuccessfully: true } result
                ? result.StandardOutput.TrimEnd()
                : string.Empty);
            if (Amend)
            {
                Message = previous;
            }
        }
        catch (Exception ex)
        {
            _messages.ShowError(null, $"Could not read the previous commit message: {ex.Message}");
        }
    }

    private string? RunCommit(string message, bool amend, bool signOff, bool resetAuthor, bool resetSoft)
    {
        if (amend && resetSoft)
        {
            ExecutionResult reset = _module.GitExecutable.Execute(new GitArgumentBuilder("reset") { "--soft", "HEAD~1" }, throwOnErrorExit: false);
            if (!reset.ExitedSuccessfully)
            {
                string resetText = string.IsNullOrWhiteSpace(reset.StandardError) ? reset.StandardOutput : reset.StandardError;
                return string.IsNullOrWhiteSpace(resetText) ? "git reset --soft failed." : resetText.Trim();
            }

            amend = false;
        }

        string file = Path.Combine(_module.WorkingDirGitDir, "GE_COMMIT_EDITMSG");
        File.WriteAllText(file, message.TrimEnd() + "\n", new UTF8Encoding(false));
        try
        {
            GitArgumentBuilder args = new("commit")
            {
                "--cleanup=strip",
                { amend, "--amend" },
                { amend && resetAuthor, "--reset-author" },
                { signOff, "--signoff" },
                "-F",
                file.QuoteNE(),
            };

            ExecutionResult result = _module.GitExecutable.Execute(args, throwOnErrorExit: false);
            if (result.ExitedSuccessfully)
            {
                return null;
            }

            string text = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            return string.IsNullOrWhiteSpace(text) ? $"git commit exited with code {result.ExitCode}." : text.Trim();
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }

    private bool GetHasHead()
        => _module.GitExecutable.Execute(new GitArgumentBuilder("rev-parse") { "--verify", "--quiet", "HEAD" }, throwOnErrorExit: false).ExitedSuccessfully;

    private string ReadPendingMessage()
    {
        foreach (string name in new[] { "MERGE_MSG", "COMMIT_EDITMSG_PENDING" })
        {
            string file = Path.Combine(_module.WorkingDirGitDir, name);
            if (File.Exists(file))
            {
                // Drop git's "# ..." comment lines.
                return string.Join('\n', File.ReadAllLines(file).Where(line => !line.StartsWith('#'))).Trim();
            }
        }

        return string.Empty;
    }

    private void OnChangesChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateCounts();

    private void UpdateCounts()
    {
        StagedCount = _changes.Count(c => c.IsStaged);
        UnstagedCount = _changes.Count(c => !c.IsStaged);
    }
}
