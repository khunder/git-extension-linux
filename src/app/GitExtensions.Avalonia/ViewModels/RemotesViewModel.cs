using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>A configured remote with its fetch and (optional) push URL.</summary>
public sealed record RemoteItem(string Name, string Url, string PushUrl);

/// <summary>A branch of a remote and the local branches that track it.</summary>
public sealed record RemoteBranchRow(string Branch, string TrackedBy)
{
    public bool IsTracked => TrackedBy.Length > 0;
}

/// <summary>
///  The "Remote repositories" dialog: list, add, rename, edit URLs, remove and prune remotes, and show the
///  branches of the selected remote with the local branches tracking them.
/// </summary>
public sealed partial class RemotesViewModel : ObservableObject
{
    private readonly Func<string?> _workingDir;
    private readonly Action<string> _reportStatus;
    private readonly Func<Task> _changed;
    private string? _editing;
    private int _loadVersion;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private RemoteItem? _selectedRemote;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _url = string.Empty;

    [ObservableProperty]
    private string _pushUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string? _message;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isConfirmingRemove;

    /// <param name="workingDir">The working directory of the open repository, or <see langword="null"/>.</param>
    /// <param name="reportStatus">Shows a short result in the status bar.</param>
    /// <param name="changed">Called after remotes changed so the refs of the open repository reload.</param>
    public RemotesViewModel(Func<string?> workingDir, Action<string> reportStatus, Func<Task>? changed = null)
    {
        _workingDir = workingDir;
        _reportStatus = reportStatus;
        _changed = changed ?? (static () => Task.CompletedTask);
    }

    public ObservableCollection<RemoteItem> Remotes { get; } = [];

    public ObservableCollection<RemoteBranchRow> Branches { get; } = [];

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public bool IsIdle => !IsBusy;

    /// <summary>True while a not yet existing remote is being entered.</summary>
    public bool IsNew => _editing is null;

    public string SaveText => IsNew ? "Add remote" : "Save";

    /// <summary>Opens the dialog and lists the remotes of the repository.</summary>
    public async Task OpenAsync()
    {
        Message = null;
        IsOpen = true;
        await ReloadAsync(null);
    }

    /// <summary>Validates a remote name the way <c>git remote add</c> would refuse it; null when fine.</summary>
    public static string? ValidateName(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return "Enter a name for the remote.";
        }

        if (trimmed.StartsWith('-') || trimmed.StartsWith('.') || trimmed.EndsWith('.') || trimmed.EndsWith('/') || trimmed.Contains("..", StringComparison.Ordinal) || trimmed.Contains("@{", StringComparison.Ordinal) || trimmed.EndsWith(".lock", StringComparison.Ordinal) ||
            trimmed.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || "~^:?*[\\".Contains(c)))
        {
            return $"'{trimmed}' is not a valid remote name.";
        }

        return null;
    }

    partial void OnSelectedRemoteChanged(RemoteItem? value)
    {
        IsConfirmingRemove = false;
        if (value is null)
        {
            return;
        }

        _editing = value.Name;
        Name = value.Name;
        Url = value.Url;
        PushUrl = value.PushUrl;
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(SaveText));
        _ = LoadBranchesAsync(value.Name);
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void New()
    {
        SelectedRemote = null;
        _editing = null;
        Name = Url = PushUrl = string.Empty;
        Branches.Clear();
        IsConfirmingRemove = false;
        Message = null;
        OnPropertyChanged(nameof(IsNew));
        OnPropertyChanged(nameof(SaveText));
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task SaveAsync()
    {
        string name = Name.Trim();
        string url = Url.Trim();
        string pushUrl = PushUrl.Trim();
        string? problem = ValidateName(name) ?? (url.Length == 0 ? "Enter the URL (or path) of the remote." : null);
        if (problem is not null)
        {
            Message = problem;
            return;
        }

        string? previous = _editing;
        if (previous is null && Remotes.Any(r => r.Name == name))
        {
            Message = $"A remote named '{name}' already exists.";
            return;
        }

        if (previous is not null && previous != name && Remotes.Any(r => r.Name == name))
        {
            Message = $"A remote named '{name}' already exists.";
            return;
        }

        await RunAsync(async () =>
        {
            RemoteItem? old = previous is null ? null : Remotes.FirstOrDefault(r => r.Name == previous);
            if (old is null)
            {
                await GitAsync("remote", "add", name, url);
            }
            else
            {
                if (old.Name != name)
                {
                    await GitAsync("remote", "rename", old.Name, name);
                }

                if (old.Url != url)
                {
                    await GitAsync("remote", "set-url", name, url);
                }
            }

            string oldPush = old?.PushUrl ?? string.Empty;
            if (pushUrl != oldPush)
            {
                if (pushUrl.Length == 0)
                {
                    await GitAsync("config", "--unset-all", $"remote.{name}.pushurl");
                }
                else
                {
                    await GitAsync("remote", "set-url", "--push", name, pushUrl);
                }
            }

            _reportStatus(old is null ? $"Added remote '{name}'." : $"Saved remote '{name}'.");
            await ReloadAsync(name);
        });
    }

    [RelayCommand]
    private void RequestRemove()
    {
        if (SelectedRemote is not null)
        {
            IsConfirmingRemove = true;
        }
    }

    [RelayCommand]
    private void CancelRemove() => IsConfirmingRemove = false;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task ConfirmRemoveAsync()
    {
        if (SelectedRemote is not { } remote)
        {
            return;
        }

        IsConfirmingRemove = false;
        await RunAsync(async () =>
        {
            await GitAsync("remote", "remove", remote.Name);
            _reportStatus($"Removed remote '{remote.Name}'.");
            await ReloadAsync(null);
        });
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task PruneAsync()
    {
        if (SelectedRemote is not { } remote)
        {
            Message = "Select a remote to prune.";
            return;
        }

        await RunAsync(async () =>
        {
            string output = await GitAsync("remote", "prune", remote.Name);
            Message = output.Length == 0 ? $"Nothing to prune for '{remote.Name}'." : output;
            _reportStatus($"Pruned remote '{remote.Name}'.");
            await LoadBranchesAsync(remote.Name);
        });
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task UpdateAllAsync()
    {
        await RunAsync(async () =>
        {
            await GitAsync("fetch", "--all");
            _reportStatus("Updated all remote branch information.");
            Message = "Fetched all remotes.";
            if (SelectedRemote is { } remote)
            {
                await LoadBranchesAsync(remote.Name);
            }
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        SaveCommand.NotifyCanExecuteChanged();
        ConfirmRemoveCommand.NotifyCanExecuteChanged();
        PruneCommand.NotifyCanExecuteChanged();
        UpdateAllCommand.NotifyCanExecuteChanged();
        try
        {
            Message = null;
            await action();
            await _changed();
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
            SaveCommand.NotifyCanExecuteChanged();
            ConfirmRemoveCommand.NotifyCanExecuteChanged();
            PruneCommand.NotifyCanExecuteChanged();
            UpdateAllCommand.NotifyCanExecuteChanged();
        }
    }

    // Runs git in the repository; a failure becomes an InvalidOperationException carrying git's message.
    private async Task<string> GitAsync(params string[] arguments)
    {
        string? dir = _workingDir();
        if (string.IsNullOrEmpty(dir))
        {
            throw new InvalidOperationException("No repository is open.");
        }

        GitCli.Result result = await GitCli.RunAsync(GitCliSettings.GitPath, dir, arguments);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.Error.Length > 0 ? result.Error : $"git {arguments[0]} failed.");
        }

        return string.IsNullOrEmpty(result.Error) ? result.Output : $"{result.Output}\n{result.Error}".Trim();
    }

    /// <summary>Reloads the remote list from git and selects <paramref name="select"/> (or nothing).</summary>
    public async Task ReloadAsync(string? select)
    {
        string? dir = _workingDir();
        if (string.IsNullOrEmpty(dir))
        {
            Remotes.Clear();
            return;
        }

        try
        {
            GitCli.Result names = await GitCli.RunAsync(GitCliSettings.GitPath, dir, "remote");
            GitCli.Result config = await GitCli.RunAsync(GitCliSettings.GitPath, dir, "config", "--local", "--get-regexp", @"^remote\..+\.(url|pushurl)$");
            IReadOnlyList<RemoteItem> items = ParseRemotes(names.Output, config.Output);
            Remotes.Clear();
            foreach (RemoteItem item in items)
            {
                Remotes.Add(item);
            }

            RemoteItem? target = select is null ? null : items.FirstOrDefault(i => i.Name == select);
            _editing = null;
            SelectedRemote = null;
            Branches.Clear();
            if (target is not null)
            {
                SelectedRemote = target;
            }
            else
            {
                Name = Url = PushUrl = string.Empty;
                OnPropertyChanged(nameof(IsNew));
                OnPropertyChanged(nameof(SaveText));
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            Message = $"Could not run git: {ex.Message}";
        }
    }

    /// <summary>Combines the output of <c>git remote</c> and <c>git config --get-regexp</c> into remotes.</summary>
    public static IReadOnlyList<RemoteItem> ParseRemotes(string names, string config)
    {
        Dictionary<string, string> urls = [];
        Dictionary<string, string> pushUrls = [];
        foreach (string line in config.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int space = line.IndexOf(' ');
            if (space < 0)
            {
                continue;
            }

            string key = line[..space];
            string value = line[(space + 1)..].Trim();
            if (key.EndsWith(".pushurl", StringComparison.OrdinalIgnoreCase))
            {
                pushUrls[key["remote.".Length..^".pushurl".Length]] = value;
            }
            else if (key.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            {
                urls[key["remote.".Length..^".url".Length]] = value;
            }
        }

        return [.. names.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .Select(n => new RemoteItem(n, urls.GetValueOrDefault(n, string.Empty), pushUrls.GetValueOrDefault(n, string.Empty)))];
    }

    private async Task LoadBranchesAsync(string remote)
    {
        int version = ++_loadVersion;
        string? dir = _workingDir();
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        try
        {
            GitCli.Result refs = await GitCli.RunAsync(GitCliSettings.GitPath, dir, "for-each-ref", "--format=%(refname)", $"refs/remotes/{remote}/");
            GitCli.Result locals = await GitCli.RunAsync(GitCliSettings.GitPath, dir, "for-each-ref", "--format=%(refname:short)%09%(upstream)", "refs/heads/");
            if (version != _loadVersion || SelectedRemote?.Name != remote)
            {
                return;
            }

            Branches.Clear();
            foreach (RemoteBranchRow row in ParseBranches(remote, refs.Output, locals.Output))
            {
                Branches.Add(row);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            Message = $"Could not run git: {ex.Message}";
        }
    }

    /// <summary>Pairs the remote branches with the local branches whose upstream they are.</summary>
    public static IReadOnlyList<RemoteBranchRow> ParseBranches(string remote, string remoteRefs, string localBranches)
    {
        string prefix = $"refs/remotes/{remote}/";
        Dictionary<string, List<string>> trackedBy = [];
        foreach (string line in localBranches.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Split('\t');
            if (parts.Length == 2 && parts[1].StartsWith(prefix, StringComparison.Ordinal))
            {
                string remoteBranch = parts[1][prefix.Length..];
                if (!trackedBy.TryGetValue(remoteBranch, out List<string>? list))
                {
                    trackedBy[remoteBranch] = list = [];
                }

                list.Add(parts[0]);
            }
        }

        return [.. remoteRefs.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(r => r.Trim())
            .Where(r => r.StartsWith(prefix, StringComparison.Ordinal))
            .Select(r => r[prefix.Length..])
            .Where(b => b != "HEAD")
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(b => new RemoteBranchRow(b, trackedBy.TryGetValue(b, out List<string>? list) ? string.Join(", ", list) : string.Empty))];
    }
}
