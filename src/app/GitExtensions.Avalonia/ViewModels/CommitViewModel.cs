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
            Message = _draftMessage;
        }
    }

    private bool CanCommit()
        => !IsCommitting && !string.IsNullOrWhiteSpace(Message) && (StagedCount > 0 || (Amend && HasHead));

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private async Task CommitAsync()
    {
        string message = Message;
        bool amend = Amend;
        IsCommitting = true;
        string? error;
        try
        {
            error = await Task.Run(() => RunCommit(message, amend));
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
            _messages.ShowError(null, error, "Commit failed");
            return;
        }

        _draftMessage = string.Empty;
        Amend = false;
        Message = string.Empty;
        await _afterCommitAsync();
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

    private string? RunCommit(string message, bool amend)
    {
        string file = Path.Combine(_module.WorkingDirGitDir, "GE_COMMIT_EDITMSG");
        File.WriteAllText(file, message.TrimEnd() + "\n", new UTF8Encoding(false));
        try
        {
            GitArgumentBuilder args = new("commit")
            {
                "--cleanup=strip",
                { amend, "--amend" },
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
