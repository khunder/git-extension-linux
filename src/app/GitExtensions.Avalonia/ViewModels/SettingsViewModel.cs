using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The settings panel. Edits are staged in the properties here; nothing is persisted until <see cref="SaveCommand"/> succeeds,
///  and <see cref="CancelCommand"/> discards them.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly Func<string?> _repositoryDirectory;
    private readonly BehaviorSettings _behavior;
    private string _savedUserName = string.Empty;
    private string _savedUserEmail = string.Empty;
    private bool _savedShowStashes;
    private int _savedHistoryLimit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGitTestResult))]
    private string? _gitTestResult;

    [ObservableProperty]
    private bool _gitTestSucceeded;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _gitPath = string.Empty;

    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private string _userEmail = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocalIdentityOverride))]
    private string _localIdentityNote = string.Empty;

    [ObservableProperty]
    private string _historyLimitText = string.Empty;

    [ObservableProperty]
    private bool _showStashes;

    public SettingsViewModel(Func<string?> repositoryDirectory, BehaviorSettings? behavior = null)
    {
        _repositoryDirectory = repositoryDirectory;
        _behavior = behavior ?? BehaviorSettings.Current;
    }

    /// <summary>
    ///  Raised after a successful save; the argument tells whether history-related options changed.
    /// </summary>
    public event Action<bool>? Saved;

    /// <summary>
    ///  Set by the view once it is attached to a window.
    /// </summary>
    public IFilePicker? FilePicker { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasGitTestResult => !string.IsNullOrEmpty(GitTestResult);

    public bool HasLocalIdentityOverride => !string.IsNullOrEmpty(LocalIdentityNote);

    /// <summary>
    ///  Shows the panel with the persisted values.
    /// </summary>
    [RelayCommand]
    public async Task OpenAsync()
    {
        ErrorMessage = null;
        GitTestResult = null;
        GitPath = AppSettings.GitCommandValue;
        ShowStashes = _savedShowStashes = AppSettings.ShowStashes;
        HistoryLimitText = (_savedHistoryLimit = _behavior.HistoryLimit).ToString(System.Globalization.CultureInfo.InvariantCulture);
        UserName = UserEmail = LocalIdentityNote = string.Empty;
        _savedUserName = _savedUserEmail = string.Empty;
        IsOpen = true;

        await LoadIdentityAsync();
    }

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task BrowseGitAsync()
    {
        if (FilePicker is null)
        {
            return;
        }

        string? file = await FilePicker.PickFileAsync("Select the git executable");
        if (file is not null)
        {
            GitPath = file;
        }
    }

    [RelayCommand]
    private async Task TestGitAsync()
    {
        IsBusy = true;
        try
        {
            (bool ok, string message) = await CheckGitAsync(EffectiveGitPath);
            GitTestSucceeded = ok;
            GitTestResult = message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        ErrorMessage = null;
        if (!int.TryParse(HistoryLimitText.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int limit)
            || !BehaviorSettings.IsValidHistoryLimit(limit))
        {
            ErrorMessage = $"The history limit must be a whole number between 1 and {BehaviorSettings.MaxHistoryLimit}.";
            return;
        }

        IsBusy = true;
        try
        {
            string gitPath = EffectiveGitPath;
            (bool ok, string message) = await CheckGitAsync(gitPath);
            GitTestSucceeded = ok;
            GitTestResult = message;
            if (!ok)
            {
                ErrorMessage = "The git executable is not valid, nothing was saved. " + message;
                return;
            }

            string name = UserName.Trim();
            string email = UserEmail.Trim();
            if (name != _savedUserName)
            {
                await WriteGlobalConfigAsync(gitPath, "user.name", name);
            }

            if (email != _savedUserEmail)
            {
                await WriteGlobalConfigAsync(gitPath, "user.email", email);
            }

            _savedUserName = name;
            _savedUserEmail = email;

            AppSettings.GitCommandValue = GitPath.Trim();
            AppSettings.ShowStashes = ShowStashes;
            AppSettings.SaveSettings();
            _behavior.SetHistoryLimit(limit);

            bool historyChanged = limit != _savedHistoryLimit || ShowStashes != _savedShowStashes;
            IsOpen = false;
            Saved?.Invoke(historyChanged);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not save the settings: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    // An empty path means "git from PATH", like AppSettings.GitCommand.
    private string EffectiveGitPath => string.IsNullOrWhiteSpace(GitPath) ? "git" : GitPath.Trim();

    private static async Task<(bool Ok, string Message)> CheckGitAsync(string gitPath)
    {
        try
        {
            GitCli.Result result = await GitCli.RunAsync(gitPath, workingDir: null, "--version");
            return result.Succeeded && result.Output.StartsWith("git version", StringComparison.OrdinalIgnoreCase)
                ? (true, result.Output)
                : (false, result.Succeeded ? $"'{gitPath}' does not look like git: {result.Output}" : $"git exited with code {result.ExitCode}: {result.Error}");
        }
        catch (Exception ex)
        {
            return (false, $"Cannot run '{gitPath}': {ex.Message}");
        }
    }

    private async Task LoadIdentityAsync()
    {
        string gitPath = string.IsNullOrWhiteSpace(AppSettings.GitCommandValue) ? "git" : AppSettings.GitCommandValue;
        string? repository = _repositoryDirectory();
        try
        {
            string name = await ReadConfigAsync(gitPath, "--global", "user.name", null);
            string email = await ReadConfigAsync(gitPath, "--global", "user.email", null);
            string localName = repository is null ? string.Empty : await ReadConfigAsync(gitPath, "--local", "user.name", repository);
            string localEmail = repository is null ? string.Empty : await ReadConfigAsync(gitPath, "--local", "user.email", repository);

            _savedUserName = name;
            _savedUserEmail = email;
            UserName = name;
            UserEmail = email;

            List<string> overrides = [];
            if (localName.Length > 0 && localName != name)
            {
                overrides.Add($"name \"{localName}\"");
            }

            if (localEmail.Length > 0 && localEmail != email)
            {
                overrides.Add($"email \"{localEmail}\"");
            }

            LocalIdentityNote = overrides.Count == 0 ? string.Empty : "The current repository overrides this locally: " + string.Join(", ", overrides) + ".";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not read the git identity: {ex.Message}";
        }
    }

    // Exit code 1 means "key not set".
    private static async Task<string> ReadConfigAsync(string gitPath, string scope, string key, string? workingDir)
    {
        GitCli.Result result = await GitCli.RunAsync(gitPath, workingDir, "config", scope, "--get", key);
        return result.ExitCode switch
        {
            0 => result.Output,
            1 => string.Empty,
            _ => throw new InvalidOperationException(result.Error),
        };
    }

    private static async Task WriteGlobalConfigAsync(string gitPath, string key, string value)
    {
        GitCli.Result result = value.Length == 0
            ? await GitCli.RunAsync(gitPath, null, "config", "--global", "--unset", key)
            : await GitCli.RunAsync(gitPath, null, "config", "--global", key, value);

        // Exit code 5: unsetting a key that is not set.
        if (!result.Succeeded && !(value.Length == 0 && result.ExitCode == 5))
        {
            throw new InvalidOperationException($"git config {key} failed: {result.Error}");
        }
    }
}
