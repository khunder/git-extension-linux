using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.FileHistory;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The file history overlay: the commits touching one file (renames followed), the file's diff in the selected commit,
///  and the file's blame.
/// </summary>
public sealed partial class FileHistoryViewModel : ObservableObject
{
    private readonly Func<GitModule?> _getModule;
    private CancellationTokenSource? _historyCancellation;
    private CancellationTokenSource? _diffCancellation;
    private string _blameRevision = "HEAD";
    private string _blamePath = string.Empty;
    private string? _loadedBlameKey;
    private bool _autoSelecting;
    private Task _blameTask = Task.CompletedTask;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHistory))]
    private bool _showBlame;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private FileHistoryEntry? _selectedEntry;

    [ObservableProperty]
    private string _diffMessage = string.Empty;

    /// <param name="getModule">The current repository.</param>
    /// <param name="selectCommit">Called when a blame row is clicked, to select the commit in the main history.</param>
    public FileHistoryViewModel(Func<GitModule?> getModule, Action<ObjectId>? selectCommit)
    {
        _getModule = getModule;
        Blame = new BlameViewModel(getModule, selectCommit);
    }

    public bool ShowHistory => !ShowBlame;

    public ObservableCollection<FileHistoryEntry> Entries { get; } = [];

    public ObservableCollection<DiffLine> DiffLines { get; } = [];

    public BlameViewModel Blame { get; }

    /// <summary>
    ///  Opens the overlay for <paramref name="path"/> and loads its history. With <paramref name="revision"/> the
    ///  blame starts at that commit (and its history row is selected) instead of HEAD.
    /// </summary>
    public async Task OpenAsync(string path, bool blame, string? revision = null)
    {
        await CancelAllAsync();
        CancellationTokenSource cancellation = new();
        _historyCancellation = cancellation;
        CancellationToken cancellationToken = cancellation.Token;

        FilePath = path;
        IsOpen = true;
        _blamePath = path;
        _blameRevision = revision ?? "HEAD";
        _loadedBlameKey = null;
        _blameTask = Task.CompletedTask;
        _autoSelecting = true;
        SelectedEntry = null;
        _autoSelecting = false;
        Entries.Clear();
        DiffLines.Clear();
        DiffMessage = string.Empty;
        ShowBlame = blame;
        Message = "Loading history…";
        IsLoading = true;

        GitModule? module = _getModule();
        if (module is null)
        {
            Message = "No repository is open.";
            IsLoading = false;
            return;
        }

        Task blameTask = blame ? EnsureBlameAsync() : Task.CompletedTask;
        try
        {
            IReadOnlyList<FileHistoryEntry> entries = await Task.Run(() => FileHistoryGit.GetHistory(module, path, cancellationToken), cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            foreach (FileHistoryEntry entry in entries)
            {
                Entries.Add(entry);
            }

            Message = entries.Count == 0 ? "No commits touch this file." : string.Empty;
            if (entries.Count > 0)
            {
                FileHistoryEntry? preferred = revision is null ? null : entries.FirstOrDefault(e => e.Hash.StartsWith(revision, StringComparison.OrdinalIgnoreCase));

                // The blame is already loading for the requested revision (or HEAD, which equals the newest entry).
                _autoSelecting = true;
                SelectedEntry = preferred ?? entries[0];
                _autoSelecting = false;
                await LoadDiffAsync(SelectedEntry);
            }
        }
        catch (OperationCanceledException)
        {
            // Another file was opened meanwhile.
        }
        catch (Exception ex)
        {
            Message = $"Failed to load history: {ex.Message}";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }

        await blameTask;
    }

    [RelayCommand]
    public async Task CloseAsync()
    {
        IsOpen = false;
        await CancelAllAsync();
    }

    [RelayCommand]
    private void ShowHistoryTab() => ShowBlame = false;

    [RelayCommand]
    private void ShowBlameTab() => ShowBlame = true;

    partial void OnShowBlameChanged(bool value)
    {
        if (value && IsOpen)
        {
            _ = EnsureBlameAsync();
        }
    }

    partial void OnSelectedEntryChanged(FileHistoryEntry? value)
    {
        if (value is null || _autoSelecting)
        {
            return;
        }

        _blameRevision = value.Hash;
        _blamePath = value.Path;
        _loadedBlameKey = null;
        if (ShowBlame)
        {
            _ = EnsureBlameAsync();
        }

        _ = LoadDiffAsync(value);
    }

    // Loads the blame for the current revision/path unless it is already shown. Failures end up in Blame.Message.
    private Task EnsureBlameAsync()
    {
        string key = _blameRevision + ":" + _blamePath;
        if (key == _loadedBlameKey)
        {
            return _blameTask;
        }

        _loadedBlameKey = key;
        _blameTask = Blame.LoadAsync(_blamePath, _blameRevision);
        return _blameTask;
    }

    private async Task CancelAllAsync()
    {
        await Task.WhenAll(
            _historyCancellation?.CancelAsync() ?? Task.CompletedTask,
            _diffCancellation?.CancelAsync() ?? Task.CompletedTask,
            Blame.CancelAsync());
    }

    private async Task LoadDiffAsync(FileHistoryEntry? entry)
    {
        if (_diffCancellation is not null)
        {
            await _diffCancellation.CancelAsync();
        }

        CancellationTokenSource cancellation = new();
        _diffCancellation = cancellation;
        DiffLines.Clear();
        DiffMessage = string.Empty;
        GitModule? module = _getModule();
        if (entry is null || module is null)
        {
            return;
        }

        DiffMessage = "Loading diff…";
        try
        {
            IReadOnlyList<DiffLine> lines = await Task.Run(() => FileHistoryGit.GetFileDiff(module, entry, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            foreach (DiffLine line in lines)
            {
                DiffLines.Add(line);
            }

            DiffMessage = lines.Count == 0 ? "No textual changes." : string.Empty;
        }
        catch (OperationCanceledException)
        {
            // Another commit was selected.
        }
        catch (Exception ex)
        {
            DiffMessage = $"Failed to load diff: {ex.Message}";
        }
    }
}
