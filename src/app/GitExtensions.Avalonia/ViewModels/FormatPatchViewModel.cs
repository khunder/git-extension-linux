using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The "Format patch" dialog: writes the commits <c>oldest..newest</c> (both included) as mail-format patch files
///  (<c>git format-patch</c>) or as one plain diff file.
/// </summary>
public sealed partial class FormatPatchViewModel : ObservableObject
{
    private readonly Func<GitModule?> _getModule;
    private readonly IFolderPicker _folderPicker;
    private readonly Action<string> _reportStatus;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _outputFolder = string.Empty;

    [ObservableProperty]
    private string _newestRevision = "HEAD";

    [ObservableProperty]
    private string _oldestRevision = "HEAD";

    [ObservableProperty]
    private bool _mailFormat = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string? _resultText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _isRunning;

    public FormatPatchViewModel(Func<GitModule?> getModule, IFolderPicker folderPicker, Action<string>? reportStatus = null)
    {
        _getModule = getModule;
        _folderPicker = folderPicker;
        _reportStatus = reportStatus ?? (static _ => { });
    }

    /// <summary>The patch files that were written by the last run.</summary>
    public RangeObservableCollection<string> CreatedFiles { get; } = [];

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasResult => !string.IsNullOrEmpty(ResultText);

    public bool IsIdle => !IsRunning;

    /// <param name="revision">The commit to start from (the selected commit); HEAD when null.</param>
    public void Open(string? revision = null, string? defaultFolder = null)
    {
        if (IsRunning)
        {
            IsOpen = true;
            return;
        }

        NewestRevision = string.IsNullOrWhiteSpace(revision) ? "HEAD" : revision;
        OldestRevision = NewestRevision;
        if (!string.IsNullOrWhiteSpace(defaultFolder))
        {
            OutputFolder = defaultFolder;
        }
        else if (string.IsNullOrWhiteSpace(OutputFolder) && _getModule() is { } module)
        {
            OutputFolder = Path.Combine(module.WorkingDir, "patches");
        }

        ErrorMessage = null;
        ResultText = null;
        CreatedFiles.Clear();
        IsOpen = true;
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? folder = await _folderPicker.PickFolderAsync("Choose the folder for the patch files");
        if (folder is not null)
        {
            OutputFolder = folder;
        }
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void OpenFolder()
    {
        if (Directory.Exists(OutputFolder.Trim()))
        {
            ShellLauncher.Open(OutputFolder.Trim());
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task CreateAsync()
    {
        ErrorMessage = null;
        ResultText = null;
        CreatedFiles.Clear();
        GitModule? module = _getModule();
        if (module is null)
        {
            ErrorMessage = "Open a repository first.";
            return;
        }

        string folder = OutputFolder.Trim();
        string newest = NewestRevision.Trim();
        string oldest = OldestRevision.Trim();
        if (folder.Length == 0 || !Path.IsPathRooted(folder))
        {
            ErrorMessage = "Enter the full path of the output folder.";
            return;
        }

        if (File.Exists(folder))
        {
            ErrorMessage = "The output path is a file, not a folder.";
            return;
        }

        if (newest.Length == 0 || oldest.Length == 0 || newest.StartsWith('-') || oldest.StartsWith('-'))
        {
            ErrorMessage = "Enter the newest and the oldest commit of the series.";
            return;
        }

        IsRunning = true;
        try
        {
            string? newestId = await ResolveAsync(module, newest);
            string? oldestId = await ResolveAsync(module, oldest);
            if (newestId is null || oldestId is null)
            {
                ErrorMessage = $"'{(newestId is null ? newest : oldest)}' is not a commit of this repository.";
                return;
            }

            Directory.CreateDirectory(folder);
            bool hasParent = (await GitOperationRunner.RunAsync(module.GitExecutable, "rev-parse", "--verify", "--quiet", oldestId + "^{commit}^")).Succeeded;
            if (MailFormat)
            {
                await FormatMailAsync(module, folder, newestId, oldestId, hasParent);
            }
            else
            {
                await FormatDiffAsync(module, folder, newestId, oldestId, hasParent);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsRunning = false;
        }
    }

    private static async Task<string?> ResolveAsync(GitModule module, string revision)
    {
        GitRunResult result = await GitOperationRunner.RunAsync(module.GitExecutable, "rev-parse", "--verify", "--quiet", revision + "^{commit}");
        return result.Succeeded ? result.Output.Trim() : null;
    }

    private async Task FormatMailAsync(GitModule module, string folder, string newest, string oldest, bool hasParent)
    {
        List<string> arguments = ["format-patch", "-o", folder];
        if (newest == oldest)
        {
            arguments.AddRange(["-1", newest]);
        }
        else if (hasParent)
        {
            arguments.Add($"{oldest}^..{newest}");
        }
        else
        {
            arguments.AddRange(["--root", newest]);
        }

        GitRunResult result = await GitOperationRunner.RunAsync(module.GitExecutable, [.. arguments]);
        if (!result.Succeeded)
        {
            ErrorMessage = $"git format-patch failed: {result.Message}";
            return;
        }

        string[] files = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        CreatedFiles.AddRange(files);
        Succeed(files.Length);
    }

    private async Task FormatDiffAsync(GitModule module, string folder, string newest, string oldest, bool hasParent)
    {
        string baseRevision = oldest + "^";
        if (!hasParent)
        {
            GitRunResult empty = await GitOperationRunner.RunAsync(module.GitExecutable, "hash-object", "-t", "tree", "--stdin");
            baseRevision = empty.Output.Trim();
        }

        GitRunResult result = await GitOperationRunner.RunAsync(module.GitExecutable, "diff", "--binary", baseRevision, newest);
        if (!result.Succeeded)
        {
            ErrorMessage = $"git diff failed: {result.Message}";
            return;
        }

        string file = Path.Combine(folder, $"changes-{oldest[..7]}-{newest[..7]}.patch");
        await File.WriteAllTextAsync(file, result.Output);
        CreatedFiles.Add(file);
        Succeed(1);
    }

    private void Succeed(int count)
    {
        ResultText = count == 0
            ? "git did not produce any patch (the range is empty)."
            : $"Created {count} patch {(count == 1 ? "file" : "files")}.";
        _reportStatus(ResultText);
    }
}
