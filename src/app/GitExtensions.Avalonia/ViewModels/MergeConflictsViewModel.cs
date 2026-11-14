using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The "Solve merge conflicts" window: the unmerged files of the working tree and a resolver (ours, theirs, mark resolved)
///  for the selected one.
/// </summary>
public sealed partial class MergeConflictsViewModel : ObservableObject, IDisposable
{
    private readonly MainWindowViewModel _main;
    private readonly GitModule _module;
    private bool _disposed;
    private int _version;

    [ObservableProperty]
    private ChangedFileViewModel? _selectedFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(HasNoConflicts))]
    private int _conflictCount;

    [ObservableProperty]
    private bool _isLoading;

    public MergeConflictsViewModel(MainWindowViewModel main, GitModule module)
    {
        _main = main;
        _module = module;
        Resolver = new ConflictResolverViewModel(() => _module, AfterResolveAsync, message => _main.ErrorMessage = message);
        Title = $"Solve merge conflicts - {new DirectoryInfo(module.WorkingDir.TrimEnd('/', '\\')).Name}";
    }

    public string Title { get; }

    public string WorkingDir => _module.WorkingDir;

    public ConflictResolverViewModel Resolver { get; }

    /// <summary>The unmerged files, one entry per path.</summary>
    public RangeObservableCollection<ChangedFileViewModel> Files { get; } = [];

    public bool HasNoConflicts => ConflictCount == 0;

    public string Summary => ConflictCount switch
    {
        0 => "There are no merge conflicts.",
        1 => "1 file has a merge conflict.",
        _ => $"{ConflictCount} files have merge conflicts.",
    };

    /// <summary>Reads the unmerged files and keeps the selection when the file is still conflicted.</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        int version = ++_version;
        IsLoading = true;
        try
        {
            List<ChangedFileViewModel> files = await Task.Run(() =>
            {
                (List<GitItemStatus> items, _) = GitErrorMessages.SplitStatusError(_module.GetAllChangedFiles(), _module.WorkingDir);
                return items.Where(i => i.IsUnmerged).DistinctBy(i => i.Name).OrderBy(i => i.Name, StringComparer.Ordinal).Select(i => new ChangedFileViewModel(i)).ToList();
            });
            if (version != _version || _disposed)
            {
                return;
            }

            string? previous = SelectedFile?.Name;
            Files.ReplaceAll(files);
            ConflictCount = files.Count;
            SelectedFile = files.FirstOrDefault(f => f.Name == previous) ?? files.FirstOrDefault();
            await Resolver.ShowAsync(SelectedFile, Files);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _main.ErrorMessage = $"Could not read the merge conflicts: {ex.Message}";
        }
        finally
        {
            if (version == _version)
            {
                IsLoading = false;
            }
        }
    }

    public void Dispose() => _disposed = true;

    partial void OnSelectedFileChanged(ChangedFileViewModel? value)
    {
        // Started from the UI thread on purpose: ShowAsync updates bound collections and handles its own errors.
        _ = Resolver.ShowAsync(value, Files);
    }

    private async Task AfterResolveAsync()
    {
        await LoadAsync();
        await _main.RefreshChangesAsync();
    }
}
