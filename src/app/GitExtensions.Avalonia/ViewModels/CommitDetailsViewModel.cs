using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GitCommands;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;
using GitUIPluginInterfaces;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  Details of the commit selected in the history: metadata, full message and the files it changed.
/// </summary>
public sealed partial class CommitDetailsViewModel : ObservableObject
{
    private CancellationTokenSource? _cancellation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _hasCommit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string? _error;

    [ObservableProperty]
    private string _hash = string.Empty;

    [ObservableProperty]
    private string _author = string.Empty;

    [ObservableProperty]
    private string _date = string.Empty;

    [ObservableProperty]
    private string _parents = string.Empty;

    [ObservableProperty]
    private string _refs = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _filesHeader = string.Empty;

    /// <summary>
    ///  True when there is nothing to show (no selection).
    /// </summary>
    public bool IsEmpty => !HasCommit && !IsLoading && Error is null;

    public ObservableCollection<ChangedFileViewModel> Files { get; } = [];

    /// <summary>
    ///  Clears the pane and cancels any load in flight.
    /// </summary>
    public async Task ClearAsync()
    {
        await CancelCurrentAsync();
        Reset();
        IsLoading = false;
    }

    /// <summary>
    ///  Loads the details of <paramref name="id"/>; a load started earlier is cancelled.
    /// </summary>
    public async Task LoadAsync(GitModule module, ObjectId id, string refs)
    {
        await CancelCurrentAsync();
        CancellationTokenSource cancellation = new();
        _cancellation = cancellation;
        CancellationToken cancellationToken = cancellation.Token;

        Reset();
        IsLoading = true;

        try
        {
            // The git calls block, so keep them off the UI thread.
            (GitRevision revision, IReadOnlyList<GitItemStatus> files) = await Task.Run(
                () =>
                {
                    GitRevision revision = module.GetRevision(id);
                    cancellationToken.ThrowIfCancellationRequested();
                    return (revision, GetChangedFiles(module, revision, cancellationToken));
                },
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            Hash = revision.ObjectId.ToString();
            Author = string.IsNullOrEmpty(revision.AuthorEmail) ? revision.Author ?? string.Empty : $"{revision.Author} <{revision.AuthorEmail}>";
            Date = revision.AuthorDate.ToString("f", CultureInfo.CurrentCulture);
            Parents = revision.HasParent ? string.Join("  ", revision.ParentIds!.Select(parentId => parentId.ToShortString())) : "(root commit)";
            Refs = refs;
            Message = (revision.Body ?? revision.Subject).TrimEnd();

            foreach (GitItemStatus file in files)
            {
                if (file.IsStatusOnly)
                {
                    // An error entry (not a real file) from the git call.
                    Error = file.ErrorMessage ?? file.Name;
                    continue;
                }

                Files.Add(new ChangedFileViewModel(file));
            }

            FilesHeader = Files.Count == 1 ? "1 file changed" : $"{Files.Count} files changed";
            HasCommit = true;
            IsLoading = false;
        }
        catch (OperationCanceledException)
        {
            // A newer selection replaced this one; it owns the state now.
        }
        catch (Exception ex)
        {
            Reset();
            Error = $"Failed to load commit: {ex.Message}";
            IsLoading = false;
        }
    }

    private static IReadOnlyList<GitItemStatus> GetChangedFiles(GitModule module, GitRevision revision, CancellationToken cancellationToken)
    {
        if (revision.HasParent)
        {
            return module.GetDiffFilesWithUntracked(revision.FirstParentId.ToString(), revision.ObjectId.ToString(), StagedStatus.None, noCache: true, cancellationToken: cancellationToken);
        }

        // A root commit has nothing to diff against (git diff cannot name the empty tree), so list what it added.
        ExecutionResult result = module.GitExecutable.Execute(
            new GitArgumentBuilder("diff-tree") { "--root", "--no-commit-id", "-r", "--raw", "-z", "--find-renames", revision.ObjectId.ToString() },
            throwOnErrorExit: false,
            cancellationToken: cancellationToken);

        // "-z" raw output is ":<modes> <ids> A\0path\0..."; every file of a root commit is an addition, so only the paths matter.
        return result.StandardOutput
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => !entry.StartsWith(':'))
            .Select(path => new GitItemStatus(path) { IsNew = true })
            .ToList();
    }

    private async Task CancelCurrentAsync()
    {
        CancellationTokenSource? previous = _cancellation;
        _cancellation = null;
        if (previous is not null)
        {
            await previous.CancelAsync();
            previous.Dispose();
        }
    }

    private void Reset()
    {
        HasCommit = false;
        Error = null;
        Hash = Author = Date = Parents = Refs = Message = FilesHeader = string.Empty;
        Files.Clear();
    }
}
