using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The "Init repository" dialog: creates a repository in a (possibly new) folder and opens it.
/// </summary>
public sealed partial class InitViewModel : ObservableObject
{
    private readonly IFolderPicker _folderPicker;
    private readonly Func<string, Task> _open;
    private readonly Func<string, IExecutable> _executableFactory;
    private readonly Action<string> _reportStatus;
    private readonly Action<string> _reportError;
    private bool _submitted;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _folder = string.Empty;

    [ObservableProperty]
    private string _initialBranch = string.Empty;

    [ObservableProperty]
    private bool _createReadmeCommit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFolderError))]
    private string? _folderError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBranchError))]
    private string? _branchError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(InitCommand))]
    private bool _isRunning;

    /// <param name="open">Opens the new repository (and records it as recent).</param>
    /// <param name="reportError">Shows a problem that remains after the dialog closed (e.g. the initial commit failed).</param>
    public InitViewModel(
        IFolderPicker folderPicker,
        Func<string, Task> open,
        Func<string, IExecutable>? executableFactory = null,
        Action<string>? reportStatus = null,
        Action<string>? reportError = null)
    {
        _folderPicker = folderPicker;
        _open = open;
        _executableFactory = executableFactory ?? (static dir => new Executable(AppSettings.GitCommand, dir));
        _reportStatus = reportStatus ?? (static _ => { });
        _reportError = reportError ?? (static _ => { });
    }

    public bool HasFolderError => FolderError is not null;

    public bool HasBranchError => BranchError is not null;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsIdle => !IsRunning;

    public void Open(string? defaultFolder = null)
    {
        if (IsRunning)
        {
            IsOpen = true;
            return;
        }

        _submitted = false;
        Folder = defaultFolder ?? string.Empty;
        InitialBranch = string.Empty;
        CreateReadmeCommit = false;
        FolderError = null;
        BranchError = null;
        ErrorMessage = null;
        IsOpen = true;
    }

    partial void OnFolderChanged(string value) => ValidateFolder();

    partial void OnInitialBranchChanged(string value)
    {
        if (_submitted)
        {
            BranchError = null;
        }
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? folder = await _folderPicker.PickFolderAsync("Choose the folder for the new repository");
        if (folder is not null)
        {
            Folder = folder;
        }
    }

    [RelayCommand]
    private void Cancel() => IsOpen = false;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task InitAsync()
    {
        _submitted = true;
        ErrorMessage = null;
        if (!ValidateFolder())
        {
            return;
        }

        string folder = Path.GetFullPath(Folder.Trim());
        string? branch = string.IsNullOrWhiteSpace(InitialBranch) ? null : InitialBranch.Trim();
        bool readme = CreateReadmeCommit;
        IsRunning = true;
        try
        {
            if (branch is not null && !await IsValidBranchNameAsync(folder, branch))
            {
                BranchError = $"'{branch}' is not a valid branch name.";
                return;
            }

            string? failure = await Task.Run(() => CreateFolder(folder));
            if (failure is not null)
            {
                ErrorMessage = failure;
                return;
            }

            IExecutable git = _executableFactory(folder);
            GitRunResult init = await (branch is null
                ? GitOperationRunner.RunAsync(git, "init")
                : GitOperationRunner.RunAsync(git, "init", "-b", branch));
            if (!init.Succeeded)
            {
                ErrorMessage = $"git init failed: {init.Message}";
                return;
            }

            string? commitProblem = readme ? await CommitReadmeAsync(git, folder) : null;
            IsOpen = false;
            _reportStatus($"Initialized a repository in {folder}.");
            if (commitProblem is not null)
            {
                _reportError(commitProblem);
            }

            await _open(folder);
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

    private static string? CreateFolder(string folder)
    {
        try
        {
            if (File.Exists(folder))
            {
                return $"'{folder}' is a file, not a folder.";
            }

            Directory.CreateDirectory(folder);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Cannot create '{folder}': {ex.Message}";
        }
    }

    private async Task<bool> IsValidBranchNameAsync(string folder, string branch)
    {
        // Ask git from the nearest folder that exists; "@{-1}" style shorthands are no names.
        if (branch.StartsWith('-') || branch.Contains("@{", StringComparison.Ordinal))
        {
            return false;
        }

        string? directory = folder;
        while (directory is not null && !Directory.Exists(directory))
        {
            directory = Path.GetDirectoryName(directory);
        }

        GitRunResult result = await GitOperationRunner.RunAsync(_executableFactory(directory ?? Path.GetTempPath()), "check-ref-format", "--branch", branch);
        return result.Succeeded;
    }

    private static async Task<string?> CommitReadmeAsync(IExecutable git, string folder)
    {
        string readme = Path.Combine(folder, "README.md");
        if (!File.Exists(readme))
        {
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
            await File.WriteAllTextAsync(readme, $"# {name}{Environment.NewLine}");
        }

        GitRunResult add = await GitOperationRunner.RunAsync(git, "add", "--", "README.md");
        if (!add.Succeeded)
        {
            return $"The repository was created, but adding README.md failed: {add.Message}";
        }

        GitRunResult commit = await GitOperationRunner.RunAsync(git, "commit", "-m", "Initial commit", "--", "README.md");
        return commit.Succeeded ? null : GitFailureText.DescribeCommitFailure(commit.Message);
    }

    private bool ValidateFolder()
    {
        if (!_submitted)
        {
            return false;
        }

        FolderError = string.IsNullOrWhiteSpace(Folder)
            ? "Choose or enter the folder for the new repository."
            : !Path.IsPathRooted(Folder.Trim())
                ? "Enter a full folder path."
                : File.Exists(Folder.Trim())
                    ? "That path is a file, not a folder."
                    : Directory.Exists(Path.Combine(Folder.Trim(), ".git")) || File.Exists(Path.Combine(Folder.Trim(), ".git"))
                        ? "That folder already contains a Git repository."
                        : null;
        return FolderError is null;
    }
}
