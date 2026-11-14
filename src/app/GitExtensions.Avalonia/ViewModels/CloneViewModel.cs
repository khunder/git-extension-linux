using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;
using GitExtensions.Extensibility;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The "Clone repository" dialog: collects the options, runs <c>git clone --progress</c> and opens the result.
/// </summary>
public sealed partial class CloneViewModel : ObservableObject
{
    // Captured on the creating (UI) thread: callers run on pool threads, and touching the static Dispatcher.UIThread there
    // can create a bogus dispatcher while a headless test session is being set up.
    private readonly Avalonia.Threading.Dispatcher _dispatcher = Avalonia.Threading.Dispatcher.UIThread;

    private readonly IFolderPicker _folderPicker;
    private readonly Func<string, Task> _open;
    private readonly IAskPassProvider? _askPass;
    private readonly Func<string, IExecutable> _executableFactory;
    private readonly Action<string> _reportStatus;
    private readonly Action<string>? _beginLog;
    private readonly Action<string>? _appendLog;
    private readonly Action<string>? _endLog;
    private readonly OutputAccumulator _output = new(200);
    private readonly object _outputLock = new();
    private CancellationTokenSource? _cancellation;
    private bool _nameEdited;
    private bool _deriving;
    private bool _submitted;
    private int _flushPending;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _parentFolder = string.Empty;

    [ObservableProperty]
    private string _targetName = string.Empty;

    [ObservableProperty]
    private string _branch = string.Empty;

    [ObservableProperty]
    private string _depth = string.Empty;

    [ObservableProperty]
    private bool _recurseSubmodules;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUrlError))]
    private string? _urlError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasParentError))]
    private string? _parentError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNameError))]
    private string? _nameError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDepthError))]
    private string? _depthError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBranchError))]
    private string? _branchError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(CloneCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private string _progressText = string.Empty;

    /// <param name="open">Opens the cloned repository (and records it as recent).</param>
    /// <param name="executableFactory">Creates the git executable for a working directory.</param>
    /// <param name="beginLog">Starts a section in the shared output panel with the command line.</param>
    /// <param name="appendLog">Receives raw output chunks (any thread).</param>
    /// <param name="endLog">Receives the outcome line.</param>
    public CloneViewModel(
        IFolderPicker folderPicker,
        Func<string, Task> open,
        IAskPassProvider? askPass = null,
        Func<string, IExecutable>? executableFactory = null,
        Action<string>? reportStatus = null,
        Action<string>? beginLog = null,
        Action<string>? appendLog = null,
        Action<string>? endLog = null)
    {
        _folderPicker = folderPicker;
        _open = open;
        _askPass = askPass;
        _executableFactory = executableFactory ?? (static dir => new Executable(AppSettings.GitCommand, dir));
        _reportStatus = reportStatus ?? (static _ => { });
        _beginLog = beginLog;
        _appendLog = appendLog;
        _endLog = endLog;
    }

    public bool HasUrlError => UrlError is not null;

    public bool HasParentError => ParentError is not null;

    public bool HasNameError => NameError is not null;

    public bool HasDepthError => DepthError is not null;

    public bool HasBranchError => BranchError is not null;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool IsIdle => !IsRunning;

    /// <summary>
    ///  Where the repository will be created.
    /// </summary>
    public string TargetPath
        => string.IsNullOrWhiteSpace(ParentFolder) || string.IsNullOrWhiteSpace(TargetName) ? string.Empty : Path.Combine(ParentFolder.Trim(), TargetName.Trim());

    /// <summary>
    ///  Shows the dialog with the given starting values.
    /// </summary>
    public void Open(string? defaultParentFolder = null)
    {
        if (IsRunning)
        {
            IsOpen = true;
            return;
        }

        _submitted = false;
        _nameEdited = false;
        Url = string.Empty;
        TargetName = string.Empty;
        Branch = string.Empty;
        Depth = string.Empty;
        RecurseSubmodules = false;
        ErrorMessage = null;
        ProgressText = string.Empty;
        ClearErrors();
        if (!string.IsNullOrWhiteSpace(defaultParentFolder))
        {
            ParentFolder = defaultParentFolder;
        }

        IsOpen = true;
    }

    partial void OnUrlChanged(string value)
    {
        if (!_nameEdited)
        {
            _deriving = true;
            TargetName = CloneTargetNameParser.Parse(value);
            _deriving = false;
        }

        Revalidate();
    }

    partial void OnTargetNameChanged(string value)
    {
        if (!_deriving)
        {
            // Typing a name takes over; clearing it hands the name back to the URL.
            _nameEdited = value.Length > 0;
            if (!_nameEdited)
            {
                _deriving = true;
                TargetName = CloneTargetNameParser.Parse(Url);
                _deriving = false;
            }
        }

        OnPropertyChanged(nameof(TargetPath));
        Revalidate();
    }

    partial void OnParentFolderChanged(string value)
    {
        OnPropertyChanged(nameof(TargetPath));
        Revalidate();
    }

    partial void OnDepthChanged(string value) => Revalidate();

    partial void OnBranchChanged(string value) => Revalidate();

    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? folder = await _folderPicker.PickFolderAsync("Choose the folder to clone into");
        if (folder is not null)
        {
            ParentFolder = folder;
        }
    }

    /// <summary>
    ///  Closes the dialog, or cancels the running clone (the dialog stays open to show the outcome).
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        if (IsRunning)
        {
            _cancellation?.Cancel();
        }
        else
        {
            IsOpen = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task CloneAsync()
    {
        _submitted = true;
        ErrorMessage = null;
        if (!Revalidate())
        {
            return;
        }

        string url = Url.Trim();
        string parent = Path.GetFullPath(ParentFolder.Trim());
        string target = Path.Combine(parent, TargetName.Trim());
        CloneOptionsValidator.TryParseDepth(Depth, out int? depth);

        List<string> args = ["clone", "--progress"];
        if (!string.IsNullOrWhiteSpace(Branch))
        {
            args.Add("--branch");
            args.Add(Branch.Trim());
        }

        if (depth is not null)
        {
            args.Add("--depth");
            args.Add(depth.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (RecurseSubmodules)
        {
            args.Add("--recurse-submodules");
        }

        args.Add("--");
        args.Add(url);
        args.Add(target);

        using CancellationTokenSource cancellation = new();
        _cancellation = cancellation;
        IsRunning = true;
        lock (_outputLock)
        {
            _output.Clear();
        }

        ProgressText = "Starting…";
        _reportStatus($"Cloning {url}…");
        _beginLog?.Invoke("git " + string.Join(' ', args));

        bool createdByUs = false;
        bool existedEmpty = false;
        bool cancelled = false;
        int exitCode = -1;
        string? exception = null;
        using AskPassOperation? askPassOperation = _askPass?.Begin(cancellation.Token);
        try
        {
            // The folder is only ours to delete if it did not exist before this clone started.
            (createdByUs, existedEmpty) = await Task.Run(() =>
            {
                bool existed = Directory.Exists(target);
                Directory.CreateDirectory(parent);
                return (!existed, existed && !Directory.EnumerateFileSystemEntries(target).Any());
            });

            exitCode = await Task.Run(() => GitProcessRunner.RunAsync(_executableFactory(parent), args, OnOutput, cancellation.Token, askPassOperation));
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            exception = ex.Message;
        }

        string? failure = null;
        string summary;
        if (cancelled)
        {
            summary = "Clone cancelled.";
        }
        else if (exception is not null)
        {
            failure = exception;
            summary = $"Clone failed: {exception}";
        }
        else if (exitCode != 0)
        {
            string[] lines;
            lock (_outputLock)
            {
                lines = [.. _output.Lines];
            }

            failure = GitFailureText.Describe(lines, askPassOperation?.UserCancelled ?? false);
            summary = $"Clone failed: {failure}";
        }
        else
        {
            summary = "Clone completed.";
        }

        if ((createdByUs || existedEmpty) && (cancelled || failure is not null))
        {
            // A folder that was already there (empty) stays; only what this clone put into it goes.
            string? cleanupError = await Task.Run(() => RemoveFolder(target, keepRoot: !createdByUs));
            if (cleanupError is not null)
            {
                summary += $" The partial folder '{target}' could not be removed: {cleanupError}";
            }
        }

        _endLog?.Invoke(summary);
        _cancellation = null;
        _reportStatus(summary);
        ProgressText = string.Empty;

        if (failure is not null)
        {
            ErrorMessage = failure;
        }
        else if (cancelled)
        {
            ErrorMessage = summary;
        }

        IsRunning = false;
        if (failure is null && !cancelled)
        {
            IsOpen = false;
            await _open(target);
        }
    }

    private static string? RemoveFolder(string path, bool keepRoot)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return null;
            }

            // Pack files are read-only on Windows.
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (keepRoot)
                    {
                        foreach (string directory in Directory.EnumerateDirectories(path))
                        {
                            Directory.Delete(directory, recursive: true);
                        }

                        foreach (string file in Directory.EnumerateFiles(path))
                        {
                            File.Delete(file);
                        }
                    }
                    else
                    {
                        Directory.Delete(path, recursive: true);
                    }

                    return null;
                }
                catch (IOException) when (attempt < 5)
                {
                    // The killed process may still hold a handle for a moment.
                    Thread.Sleep(100);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    private bool Revalidate()
    {
        if (!_submitted)
        {
            return false;
        }

        CloneValidation result = CloneOptionsValidator.Validate(Url, ParentFolder, TargetName, Depth, Branch);
        UrlError = result.Url;
        ParentError = result.ParentFolder;
        NameError = result.TargetName;
        DepthError = result.Depth;
        BranchError = result.Branch;
        return result.IsValid;
    }

    private void ClearErrors()
    {
        UrlError = null;
        ParentError = null;
        NameError = null;
        DepthError = null;
        BranchError = null;
    }

    // Called from reader threads: forward to the output panel and keep the last progress line for the dialog.
    private void OnOutput(string chunk)
    {
        _appendLog?.Invoke(chunk);
        lock (_outputLock)
        {
            _output.Append(chunk);
        }

        if (Interlocked.Exchange(ref _flushPending, 1) == 0)
        {
            _dispatcher.Post(FlushProgress, DispatcherPriority.Background);
        }
    }

    private void FlushProgress()
    {
        Interlocked.Exchange(ref _flushPending, 0);
        if (!IsRunning)
        {
            return;
        }

        string last;
        lock (_outputLock)
        {
            last = _output.Lines.LastOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? string.Empty;
        }

        ProgressText = last;
    }
}
