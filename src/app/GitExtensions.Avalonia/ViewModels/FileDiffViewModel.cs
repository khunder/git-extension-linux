using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Diff;
using GitExtensions.Extensibility.Git;

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StageCommand), nameof(UnstageCommand))]
    private ChangedFileViewModel? _file;

    [ObservableProperty]
    private string _message = "Select a changed file to see its diff.";

    [ObservableProperty]
    private bool _isLoading;

    public FileDiffViewModel(GitModule module, Func<Task> refreshAsync)
    {
        _module = module;
        _refreshAsync = refreshAsync;
    }

    public ObservableCollection<DiffLine> Lines { get; } = [];

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

        if (file is null)
        {
            Message = "Select a changed file to see its diff.";
            return;
        }

        IsLoading = true;
        Message = "Loading diff…";
        try
        {
            IReadOnlyList<DiffLine> lines = await LoadLinesAsync(file.Item, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            foreach (DiffLine line in lines)
            {
                Lines.Add(line);
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

    private async Task<IReadOnlyList<DiffLine>> LoadLinesAsync(GitItemStatus item, CancellationToken cancellationToken)
    {
        if (!item.IsTracked && item.Staged != StagedStatus.Index)
        {
            return await Task.Run(() => LoadUntracked(item), cancellationToken);
        }

        Patch? patch = await Task.Run(
            () => _module.GetCurrentChangesAsync(item.Name, item.OldName, item.Staged == StagedStatus.Index, string.Empty, noLocks: true),
            cancellationToken);
        if (patch is null)
        {
            return [];
        }

        return patch.FileType == PatchFileType.Binary
            ? [.. UnifiedDiffParser.Parse(patch.Text ?? patch.Header), new DiffLine(DiffLineKind.Info, "Binary file differs.", null, null)]
            : UnifiedDiffParser.Parse(patch.Text ?? patch.Header);
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
