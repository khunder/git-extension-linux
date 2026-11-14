using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The "Apply patch" dialog: applies one patch file or all patch files of a folder, either as commits
///  (<c>git am</c>) or to the working tree only (<c>git apply</c>). After <c>git am</c> stopped on a conflict the dialog offers
///  continue, skip and abort.
/// </summary>
public sealed partial class ApplyPatchViewModel : ObservableObject
{
    private static readonly string[] PatchExtensions = [".patch", ".diff", ".mbox"];

    private readonly Func<GitModule?> _getModule;
    private readonly Func<Task> _refresh;
    private readonly Action<string> _reportStatus;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _patchPath = string.Empty;

    [ObservableProperty]
    private bool _isFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignOff))]
    private bool _commit = true;

    [ObservableProperty]
    private bool _signOff;

    [ObservableProperty]
    private bool _threeWay;

    [ObservableProperty]
    private bool _ignoreWhitespace;

    [ObservableProperty]
    private string _output = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConflicts), nameof(ConflictText))]
    private IReadOnlyList<string> _conflictedFiles = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart), nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(ContinueCommand), nameof(SkipCommand), nameof(AbortCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(ContinueCommand), nameof(SkipCommand), nameof(AbortCommand))]
    private bool _isAmInProgress;

    public ApplyPatchViewModel(Func<GitModule?> getModule, Func<Task> refresh, Action<string>? reportStatus = null)
    {
        _getModule = getModule;
        _refresh = refresh;
        _reportStatus = reportStatus ?? (static _ => { });
    }

    public IFilePicker? FilePicker { get; set; }

    public IFolderPicker? FolderPicker { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasConflicts => ConflictedFiles.Count > 0;

    public bool IsIdle => !IsRunning;

    public bool CanStart => !IsRunning && !IsAmInProgress;

    public bool CanSignOff => Commit;

    public string ConflictText => $"Conflicts in {ConflictedFiles.Count} {(ConflictedFiles.Count == 1 ? "file" : "files")}: {string.Join(", ", ConflictedFiles)}. Resolve them and stage the result, then choose Continue.";

    public async Task OpenAsync()
    {
        ErrorMessage = null;
        IsOpen = true;
        await UpdateStateAsync();
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? path = IsFolder
            ? FolderPicker is null ? null : await FolderPicker.PickFolderAsync("Choose the folder with the patch files")
            : FilePicker is null ? null : await FilePicker.PickFileAsync("Choose a patch file");
        if (path is not null)
        {
            PatchPath = path;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task ApplyAsync()
    {
        ErrorMessage = null;
        Output = string.Empty;
        ConflictedFiles = [];
        GitModule? module = _getModule();
        if (module is null)
        {
            ErrorMessage = "Open a repository first.";
            return;
        }

        string path = PatchPath.Trim();
        List<string> files;
        if (path.Length == 0)
        {
            ErrorMessage = IsFolder ? "Choose the folder with the patch files." : "Choose the patch file.";
            return;
        }

        if (IsFolder)
        {
            if (!Directory.Exists(path))
            {
                ErrorMessage = $"'{path}' is not a folder.";
                return;
            }

            files = [.. Directory.EnumerateFiles(path)
                .Where(file => PatchExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)];
            if (files.Count == 0)
            {
                ErrorMessage = "The folder contains no .patch, .diff or .mbox files.";
                return;
            }
        }
        else
        {
            if (!File.Exists(path))
            {
                ErrorMessage = $"'{path}' does not exist.";
                return;
            }

            files = [Path.GetFullPath(path)];
        }

        IsRunning = true;
        try
        {
            if (Commit)
            {
                List<string> arguments = ["am"];
                AddOptions(arguments, allowSignOff: true);
                arguments.AddRange(files);
                await RunAsync(module, "Applied", arguments);
            }
            else
            {
                await ApplyToWorkingTreeAsync(module, files);
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private Task ContinueAsync() => ResumeAsync("--continue", "Continued applying the patches");

    [RelayCommand(CanExecute = nameof(CanResume))]
    private Task SkipAsync() => ResumeAsync("--skip", "Skipped the current patch");

    [RelayCommand(CanExecute = nameof(CanResume))]
    private Task AbortAsync() => ResumeAsync("--abort", "Patch application aborted");

    private bool CanResume() => IsAmInProgress && !IsRunning;

    private void AddOptions(List<string> arguments, bool allowSignOff)
    {
        if (ThreeWay)
        {
            arguments.Add("--3way");
        }

        if (IgnoreWhitespace)
        {
            arguments.Add("--ignore-whitespace");
        }

        if (allowSignOff && SignOff)
        {
            arguments.Add("--signoff");
        }
    }

    private async Task ApplyToWorkingTreeAsync(GitModule module, List<string> files)
    {
        foreach (string file in files)
        {
            List<string> arguments = ["apply", "--verbose"];
            AddOptions(arguments, allowSignOff: false);
            arguments.Add(file);
            if (!await RunAsync(module, "Applied", arguments, $"{Path.GetFileName(file)}: ", reportSuccess: file == files[^1]))
            {
                return;
            }
        }
    }

    private async Task ResumeAsync(string option, string successMessage)
    {
        GitModule? module = _getModule();
        if (module is null)
        {
            return;
        }

        ErrorMessage = null;
        IsRunning = true;
        try
        {
            await RunAsync(module, successMessage, ["am", option]);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task<bool> RunAsync(GitModule module, string successMessage, List<string> arguments, string prefix = "", bool reportSuccess = true)
    {
        GitRunResult result;
        try
        {
            result = await GitOperationRunner.RunAsync(module.GitExecutable, [.. arguments]);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ErrorMessage = ex.Message;
            return false;
        }

        string text = string.Join(Environment.NewLine, new[] { result.Output.Trim(), result.Error.Trim() }.Where(part => part.Length > 0));
        Output += prefix + text + Environment.NewLine;
        await UpdateStateAsync();
        await _refresh();
        if (result.Succeeded)
        {
            if (reportSuccess)
            {
                _reportStatus(successMessage);
            }

            return true;
        }

        ErrorMessage = IsAmInProgress && HasConflicts
            ? "git am stopped because a patch does not apply cleanly."
            : $"git {arguments[0]} failed: {result.Message}";
        return false;
    }

    private async Task UpdateStateAsync()
    {
        GitModule? module = _getModule();
        if (module is null)
        {
            IsAmInProgress = false;
            ConflictedFiles = [];
            return;
        }

        IsAmInProgress = File.Exists(Path.Combine(module.WorkingDirGitDir, "rebase-apply", "applying"));
        if (!IsAmInProgress)
        {
            ConflictedFiles = [];
            return;
        }

        GitRunResult unmerged = await GitOperationRunner.RunAsync(module.GitExecutable, "diff", "--name-only", "--diff-filter=U");
        ConflictedFiles = [.. unmerged.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }
}
