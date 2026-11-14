using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitCommands.Git;
using GitExtensions.Desktop.Diff;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  Shows the diff of one changed file and stages/unstages files.
/// </summary>
public sealed partial class FileDiffViewModel : ObservableObject
{
    private const int MaxUntrackedBytes = 1024 * 1024;

    private readonly GitModule _module;
    private readonly Func<Task> _refreshAsync;
    private CancellationTokenSource? _loadCancellation;
    private string[]? _rawLines;
    private bool _canSelectLines;
    private DiffLine? _selectionAnchor;
    private PendingDiscard? _pendingDiscard;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StageCommand), nameof(UnstageCommand))]
    private ChangedFileViewModel? _file;

    [ObservableProperty]
    private string _message = "Select a changed file to see its diff.";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _selectedLineCount;

    [ObservableProperty]
    private bool _canStageSelected;

    [ObservableProperty]
    private bool _canUnstageSelected;

    [ObservableProperty]
    private bool _canDiscardSelected;

    [ObservableProperty]
    private string _confirmationText = string.Empty;

    [ObservableProperty]
    private bool _isConfirmingDiscard;

    public FileDiffViewModel(GitModule module, Func<Task> refreshAsync)
    {
        _module = module;
        _refreshAsync = refreshAsync;
    }

    public ObservableCollection<DiffLine> Lines { get; } = [];

    public bool HasSelection => SelectedLineCount > 0;

    /// <summary>
    ///  Loads the diff of <paramref name="file"/> (or clears the viewer for <see langword="null"/>).
    /// </summary>
    public async Task ShowAsync(ChangedFileViewModel? file)
    {
        if (_loadCancellation is not null)
        {
            await _loadCancellation.CancelAsync();
        }

        CancellationTokenSource cancellation = new();
        _loadCancellation = cancellation;
        File = file;
        Lines.Clear();
        _rawLines = null;
        _canSelectLines = false;
        _selectionAnchor = null;
        CancelDiscard();
        UpdateSelectionState();

        if (file is null)
        {
            Message = "Select a changed file to see its diff.";
            return;
        }

        IsLoading = true;
        Message = "Loading diff…";
        try
        {
            LoadedDiff loaded = await LoadLinesAsync(file.Item, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            IReadOnlyList<DiffLine> lines = loaded.Lines;
            _rawLines = loaded.Raw;
            _canSelectLines = loaded.Raw is not null;
            bool canStageHunk = loaded.CanPatch && !file.IsStaged;
            bool canUnstageHunk = loaded.CanPatch && file.IsStaged;
            bool canDiscardHunk = loaded.Raw is not null && !file.IsStaged;
            foreach (DiffLine line in lines)
            {
                Lines.Add(line.Kind == DiffLineKind.Hunk
                    ? line with { CanStageHunk = canStageHunk, CanUnstageHunk = canUnstageHunk, CanDiscardHunk = canDiscardHunk }
                    : line);
            }

            Message = lines.Count == 0 ? "No textual changes." : string.Empty;
        }
        catch (OperationCanceledException)
        {
            // A newer selection replaced this one.
        }
        catch (Exception ex)
        {
            Message = $"Failed to load diff: {ex.Message}";
        }
        finally
        {
            if (!cancellation.IsCancellationRequested)
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanStage))]
    private Task StageAsync() => File is { } file ? ChangeIndexAsync(module => module.StageFiles([file.Item], out _)) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanUnstage))]
    private Task UnstageAsync() => File is { } file ? ChangeIndexAsync(module => module.UnstageFiles([file.Item], out _)) : Task.CompletedTask;

    [RelayCommand]
    private Task StageAllAsync() => ChangeIndexAsync(module =>
    {
        // Stage by status of the working tree; staged entries are already in the index.
        List<GitItemStatus> items = [.. module.GetAllChangedFiles().Where(item => item.Staged != StagedStatus.Index)];
        module.StageFiles(items, out _);
    });

    [RelayCommand]
    private Task UnstageAllAsync() => ChangeIndexAsync(module =>
    {
        List<GitItemStatus> items = [.. module.GetAllChangedFiles().Where(item => item.Staged == StagedStatus.Index)];
        module.UnstageFiles(items, out _);
    });

    private bool CanStage() => File is { IsStaged: false };

    private bool CanUnstage() => File is { IsStaged: true };

    private async Task ChangeIndexAsync(Action<GitModule> change)
    {
        try
        {
            await Task.Run(() => change(_module));
        }
        catch (Exception ex)
        {
            Message = $"Failed to update the index: {ex.Message}";
            return;
        }

        await _refreshAsync();
    }

    /// <summary>
    ///  Selects/deselects a changed line. Ctrl toggles, Shift extends a range from the last clicked line.
    /// </summary>
    public void SelectLine(DiffLine line, bool ctrl, bool shift)
    {
        if (!_canSelectLines || !line.IsChange)
        {
            return;
        }

        if (shift && _selectionAnchor is { } anchor)
        {
            int from = IndexOf(anchor);
            int to = IndexOf(line);
            if (from >= 0 && to >= 0)
            {
                if (!ctrl)
                {
                    ClearSelectionCore();
                }

                for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++)
                {
                    if (Lines[i].IsChange)
                    {
                        Lines[i].IsSelected = true;
                    }
                }
            }
        }
        else if (ctrl)
        {
            line.IsSelected = !line.IsSelected;
            _selectionAnchor = line;
        }
        else
        {
            bool onlyThis = line.IsSelected && Lines.Count(l => l.IsSelected) == 1;
            ClearSelectionCore();
            line.IsSelected = !onlyThis;
            _selectionAnchor = line;
        }

        UpdateSelectionState();
    }

    public void ClearSelection()
    {
        ClearSelectionCore();
        _selectionAnchor = null;
        UpdateSelectionState();
    }

    [RelayCommand]
    private Task StageHunkAsync(DiffLine? hunk)
        => hunk is null ? Task.CompletedTask : ApplyAsync(PatchOperation.Stage, HunkIndices(hunk));

    [RelayCommand]
    private Task UnstageHunkAsync(DiffLine? hunk)
        => hunk is null ? Task.CompletedTask : ApplyAsync(PatchOperation.Unstage, HunkIndices(hunk));

    [RelayCommand]
    private Task StageSelectedAsync() => ApplyAsync(PatchOperation.Stage, SelectedIndices());

    [RelayCommand]
    private Task UnstageSelectedAsync() => ApplyAsync(PatchOperation.Unstage, SelectedIndices());

    /// <summary>
    ///  Asks for confirmation before discarding a hunk; nothing is changed until <see cref="ConfirmDiscardCommand"/> runs.
    /// </summary>
    [RelayCommand]
    private void DiscardHunk(DiffLine? hunk)
    {
        if (hunk is not null)
        {
            RequestDiscard(HunkIndices(hunk));
        }
    }

    [RelayCommand]
    private void DiscardSelected() => RequestDiscard(SelectedIndices());

    [RelayCommand]
    private void CancelDiscard()
    {
        _pendingDiscard = null;
        IsConfirmingDiscard = false;
        ConfirmationText = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmDiscardAsync()
    {
        PendingDiscard? pending = _pendingDiscard;
        CancelDiscard();
        if (pending is not null)
        {
            await ApplyAsync(PatchOperation.Discard, pending.Indices);
        }
    }

    private void RequestDiscard(HashSet<int> indices)
    {
        if (File is not { } file || indices.Count == 0)
        {
            return;
        }

        _pendingDiscard = new PendingDiscard(indices);
        ConfirmationText = $"Discard {indices.Count} {(indices.Count == 1 ? "line" : "lines")} from {file.Name}? This cannot be undone.";
        IsConfirmingDiscard = true;
    }

    private async Task ApplyAsync(PatchOperation operation, HashSet<int> indices)
    {
        if (File is not { } file)
        {
            return;
        }

        // Untracked files are staged as a whole.
        if (_rawLines is null)
        {
            if (operation == PatchOperation.Stage && !file.Item.IsTracked && Lines.Any(l => l.Kind == DiffLineKind.Hunk))
            {
                await FinishAsync(file, () => Task.Run(() => _module.StageFiles([file.Item], out _) ? null : "git add failed."));
            }

            return;
        }

        string[] raw = _rawLines;
        List<DiffLine> lines = [.. Lines];
        string? patch = HunkPatchBuilder.Build(raw, lines, indices, operation);
        if (patch is null)
        {
            ClearSelection();
            return;
        }

        await FinishAsync(file, () => Task.Run(() => ApplyPatch(patch, operation)));
    }

    private async Task FinishAsync(ChangedFileViewModel file, Func<Task<string?>> operation)
    {
        ClearSelection();
        string? error;
        try
        {
            error = await operation();
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        if (error is not null)
        {
            Message = $"Failed to apply the change: {error}";
            return;
        }

        await _refreshAsync();

        // When the refresh did not already re-select (and thereby re-load) this entry, show its remaining diff.
        if (ReferenceEquals(File, file))
        {
            await ShowAsync(file);
        }
    }

    private string? ApplyPatch(string patch, PatchOperation operation)
    {
        GitArgumentBuilder args = new("apply")
        {
            { operation != PatchOperation.Discard, "--cached" },
            { operation != PatchOperation.Stage, "-R" },
            "--whitespace=nowarn",
            "-"
        };

        // The patch is Latin-1 text (the lossless decoding of git's output), so write the original bytes back.
        byte[] bytes = GitModule.LosslessEncoding.GetBytes(patch);
        ExecutionResult result = _module.GitExecutable.Execute(
            args,
            writer =>
            {
                writer.Flush();
                writer.BaseStream.Write(bytes, 0, bytes.Length);
                writer.BaseStream.Flush();
            },
            throwOnErrorExit: false);
        return result.ExitedSuccessfully ? null : result.AllOutput.Trim();
    }

    private HashSet<int> HunkIndices(DiffLine hunk)
    {
        HashSet<int> indices = [];
        for (int i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].IsChange && Lines[i].HunkIndex == hunk.HunkIndex)
            {
                indices.Add(i);
            }
        }

        return indices;
    }

    private HashSet<int> SelectedIndices()
    {
        HashSet<int> indices = [];
        for (int i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].IsSelected)
            {
                indices.Add(i);
            }
        }

        return indices;
    }

    private int IndexOf(DiffLine line)
    {
        for (int i = 0; i < Lines.Count; i++)
        {
            if (ReferenceEquals(Lines[i], line))
            {
                return i;
            }
        }

        return -1;
    }

    private void ClearSelectionCore()
    {
        foreach (DiffLine line in Lines)
        {
            line.IsSelected = false;
        }
    }

    private void UpdateSelectionState()
    {
        int count = Lines.Count(l => l.IsSelected);
        SelectedLineCount = count;
        bool any = count > 0 && _rawLines is not null && File is not null;
        CanStageSelected = any && File is { IsStaged: false };
        CanUnstageSelected = any && File is { IsStaged: true };
        CanDiscardSelected = any && File is { IsStaged: false };
        OnPropertyChanged(nameof(HasSelection));
    }

    private sealed record PendingDiscard(HashSet<int> Indices);

    private sealed record LoadedDiff(IReadOnlyList<DiffLine> Lines, string[]? Raw, bool CanPatch);

    private async Task<LoadedDiff> LoadLinesAsync(GitItemStatus item, CancellationToken cancellationToken)
    {
        if (!item.IsTracked && item.Staged != StagedStatus.Index)
        {
            // Untracked files can only be staged as a whole ("Stage hunk" stages the file).
            IReadOnlyList<DiffLine> untracked = await Task.Run(() => LoadUntracked(item), cancellationToken);
            return new LoadedDiff(untracked, null, CanPatch: untracked.Any(l => l.Kind == DiffLineKind.Hunk));
        }

        bool staged = item.Staged == StagedStatus.Index;
        Task<Patch?> patchTask = Task.Run(
            () => _module.GetCurrentChangesAsync(item.Name, item.OldName, staged, string.Empty, noLocks: true),
            cancellationToken);

        // The raw (lossless, CRLF preserving) text is what partial patches are built from.
        Task<ExecutionResult> rawTask = Task.Run(
            () => _module.GitExecutable.ExecuteAsync(
                Commands.GetCurrentChanges(item.Name, item.OldName, staged, string.Empty, noLocks: true),
                outputEncoding: GitModule.LosslessEncoding,
                stripAnsiEscapeCodes: false,
                throwOnErrorExit: false,
                cancellationToken: cancellationToken),
            cancellationToken);
        Patch? patch = await patchTask;
        ExecutionResult rawResult = await rawTask;
        if (patch is null)
        {
            return new LoadedDiff([], null, false);
        }

        if (patch.FileType == PatchFileType.Binary)
        {
            return new LoadedDiff([.. UnifiedDiffParser.Parse(patch.Text ?? patch.Header), new DiffLine(DiffLineKind.Info, "Binary file differs.", null, null)], null, false);
        }

        IReadOnlyList<DiffLine> lines = UnifiedDiffParser.Parse(patch.Text ?? patch.Header);
        string[]? raw = rawResult.ExitedSuccessfully ? HunkPatchBuilder.SplitRaw(rawResult.StandardOutput) : null;

        // Partial patches need the raw text to line up with what is displayed; otherwise offer no partial operations.
        if (raw is null || raw.Length != lines.Count)
        {
            raw = null;
        }

        return new LoadedDiff(lines, raw, raw is not null);
    }

    private IReadOnlyList<DiffLine> LoadUntracked(GitItemStatus item)
    {
        string path = Path.Combine(_module.WorkingDir, item.Name);
        if (Directory.Exists(path))
        {
            return [new DiffLine(DiffLineKind.Info, "Untracked directory.", null, null)];
        }

        if (!System.IO.File.Exists(path))
        {
            return [];
        }

        using FileStream stream = System.IO.File.OpenRead(path);
        byte[] buffer = new byte[(int)Math.Min(stream.Length, MaxUntrackedBytes)];
        stream.ReadExactly(buffer);
        if (Array.IndexOf(buffer, (byte)0) >= 0)
        {
            return [new DiffLine(DiffLineKind.Header, $"new file: {item.Name}", null, null), new DiffLine(DiffLineKind.Info, "Binary file.", null, null)];
        }

        List<DiffLine> lines = [.. UnifiedDiffParser.FromNewFileContent(item.Name, Encoding.UTF8.GetString(buffer))];
        if (stream.Length > MaxUntrackedBytes)
        {
            lines.Add(new DiffLine(DiffLineKind.Info, "… file truncated.", null, null));
        }

        return lines;
    }
}
