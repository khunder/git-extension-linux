using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExtensions.Desktop.Diff;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The "View patch file" dialog: shows a .patch / .diff file (a plain diff or a mail-format patch) read-only.
/// </summary>
public sealed partial class ViewPatchViewModel : ObservableObject
{
    private const long MaxBytes = 20 * 1024 * 1024;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _summary = "Choose a patch file to view.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public IFilePicker? FilePicker { get; set; }

    public RangeObservableCollection<DiffLine> Lines { get; } = [];

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public void Open() => IsOpen = true;

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (FilePicker is null)
        {
            return;
        }

        string? file = await FilePicker.PickFileAsync("Choose a patch file");
        if (file is not null)
        {
            await LoadAsync(file);
        }
    }

    [RelayCommand]
    private Task LoadTypedAsync() => LoadAsync(FilePath);

    /// <summary>Reads and shows the patch file; problems are reported inline.</summary>
    public async Task LoadAsync(string path)
    {
        path = path.Trim();
        FilePath = path;
        ErrorMessage = null;
        Lines.Clear();
        try
        {
            FileInfo info = new(path);
            if (!info.Exists)
            {
                ErrorMessage = $"'{path}' does not exist.";
                return;
            }

            if (info.Length > MaxBytes)
            {
                ErrorMessage = "The file is too large to show.";
                return;
            }

            string text = await File.ReadAllTextAsync(path);
            IReadOnlyList<DiffLine> parsed = await Task.Run(() => UnifiedDiffParser.Parse(text));
            Lines.AddRange(parsed);
            int files = parsed.Count(line => line.Kind == DiffLineKind.Header && line.Text.StartsWith("diff --git", StringComparison.Ordinal));
            int added = parsed.Count(line => line.Kind == DiffLineKind.Added);
            int removed = parsed.Count(line => line.Kind == DiffLineKind.Removed);
            Summary = parsed.Count == 0
                ? "The file is empty."
                : $"{files} {(files == 1 ? "file" : "files")} changed, +{added} -{removed}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ErrorMessage = ex.Message;
        }
    }
}
