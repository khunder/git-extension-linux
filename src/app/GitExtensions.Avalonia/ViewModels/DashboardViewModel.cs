using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands.UserRepositoryHistory;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  A repository on the dashboard: name (bold), path and the branch that is checked out (read in the background).
/// </summary>
public sealed partial class DashboardEntry : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBranch))]
    private string? _branch;

    public DashboardEntry(string path, string? favoriteCategory)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;
        FavoriteCategory = favoriteCategory;
    }

    public string Path { get; }

    public string Name { get; }

    /// <summary>The category of the favorites list the entry is shown in; <see langword="null"/> for the recent list.</summary>
    public string? FavoriteCategory { get; }

    public bool HasBranch => !string.IsNullOrEmpty(Branch);

    /// <summary>Set once the entry's repository is known to be a favorite (also for its entry in the recent list).</summary>
    public bool IsFavorite { get; init; }

    public string FavoriteMenuText => IsFavorite ? "Remove from favorites" : "Add to favorites";
}

/// <summary>
///  A titled list of repositories on the dashboard ("Favorites", a user category, "Recent repositories").
/// </summary>
public sealed class DashboardCategory(string title, IReadOnlyList<DashboardEntry> entries)
{
    public string Title { get; } = title;

    public IReadOnlyList<DashboardEntry> Entries { get; } = entries;

    public string Header => $"{Title} ({Entries.Count})";
}

/// <summary>
///  The dashboard (start page): the repository history in categories, with the "create / open / clone" links.
///  Shown when no repository is open and again after a repository was closed.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    /// <summary>The category favorites are put into when no other one is chosen.</summary>
    public const string DefaultFavoriteCategory = "Favorites";

    public const string RecentTitle = "Recent repositories";

    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    private readonly ILocalRepositoryManager _manager;
    private readonly Func<string, Task> _open;
    private readonly Func<Task> _browse;
    private readonly Action _requestInit;
    private readonly Action _requestClone;
    private readonly Action<string> _reportError;
    private readonly Dictionary<string, string?> _branches = new(PathComparer);
    private IReadOnlyList<Repository> _recent = [];
    private IReadOnlyList<Repository> _favorites = [];
    private int _loadVersion;

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoaded;

    public DashboardViewModel(
        ILocalRepositoryManager manager,
        Func<string, Task> open,
        Func<Task> browse,
        Action requestInit,
        Action requestClone,
        Action<string> reportError)
    {
        _manager = manager;
        _open = open;
        _browse = browse;
        _requestInit = requestInit;
        _requestClone = requestClone;
        _reportError = reportError;
        Categories.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<DashboardCategory> Categories { get; } = [];

    /// <summary>Set by the host: shows a folder in the file manager.</summary>
    public Func<string, bool> RevealFolder { get; set; } = FolderLauncher.TryOpen;

    /// <summary>Set by the host: reads the branch of a repository (<see langword="null"/> if it is not one). Replaceable in tests.</summary>
    public Func<string, Task<string?>> ReadBranch { get; set; } = ReadBranchWithGitAsync;

    public IClipboardService? Clipboard { get; set; }

    /// <summary>True once the history has loaded and nothing is listed (or nothing matches the filter).</summary>
    public bool IsEmpty => IsLoaded && Categories.Count == 0;

    /// <summary>
    ///  Reads the recent and favorite repositories (off the UI thread) and rebuilds the lists.
    /// </summary>
    public async Task LoadAsync()
    {
        int version = ++_loadVersion;
        try
        {
            IList<Repository> recent = await Task.Run(_manager.LoadRecentHistoryAsync);
            IList<Repository> favorites = await Task.Run(_manager.LoadFavouriteHistoryAsync);
            if (version != _loadVersion)
            {
                return;
            }

            _recent = [.. recent];
            _favorites = [.. favorites];
        }
        catch (Exception ex)
        {
            _reportError($"Could not read the recent repositories: {ex.Message}");
        }

        Rebuild();
        IsLoaded = true;
    }

    partial void OnFilterChanged(string value) => Rebuild();

    [RelayCommand]
    private Task OpenAsync(DashboardEntry? entry) => entry is null ? Task.CompletedTask : _open(entry.Path);

    [RelayCommand]
    private Task BrowseAsync() => _browse();

    [RelayCommand]
    private void CreateNew() => _requestInit();

    [RelayCommand]
    private void CloneRepository() => _requestClone();

    [RelayCommand]
    private void OpenInExplorer(DashboardEntry? entry)
    {
        if (entry is not null && !RevealFolder(entry.Path))
        {
            _reportError($"Could not open {entry.Path} in the file manager.");
        }
    }

    [RelayCommand]
    private async Task CopyPathAsync(DashboardEntry? entry)
    {
        if (entry is not null && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(entry.Path);
        }
    }

    [RelayCommand]
    private async Task RemoveFromListAsync(DashboardEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        try
        {
            if (entry.FavoriteCategory is null)
            {
                await Task.Run(() => _manager.RemoveRecentAsync(entry.Path));
            }
            else
            {
                await Task.Run(() => _manager.RemoveFavouriteAsync(entry.Path));
            }
        }
        catch (Exception ex)
        {
            _reportError($"Could not remove {entry.Path} from the list: {ex.Message}");
        }

        await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(DashboardEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        try
        {
            string? category = entry.IsFavorite ? null : DefaultFavoriteCategory;
            await Task.Run(() => _manager.AssignCategoryAsync(new Repository(entry.Path), category));
        }
        catch (Exception ex)
        {
            _reportError($"Could not change the favorites: {ex.Message}");
        }

        await LoadAsync();
    }

    private void Rebuild()
    {
        string text = Filter.Trim();
        HashSet<string> favoritePaths = new(_favorites.Select(f => Normalize(f.Path)), PathComparer);
        List<DashboardCategory> categories = [];

        foreach (IGrouping<string, Repository> group in _favorites
            .GroupBy(f => string.IsNullOrWhiteSpace(f.Category) ? DefaultFavoriteCategory : f.Category!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddCategory(categories, group.Key, group.Select(r => r.Path), group.Key, favoritePaths, text);
        }

        AddCategory(categories, RecentTitle, _recent.Select(r => r.Path), null, favoritePaths, text);

        Categories.Clear();
        foreach (DashboardCategory category in categories)
        {
            Categories.Add(category);
        }

        _ = LoadBranchesAsync([.. categories.SelectMany(c => c.Entries)]);
    }

    private static void AddCategory(List<DashboardCategory> categories, string title, IEnumerable<string> paths, string? favoriteCategory, HashSet<string> favoritePaths, string filter)
    {
        List<DashboardEntry> entries = [];
        foreach (string path in paths)
        {
            if (filter.Length > 0 && !path.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries.Add(new DashboardEntry(path, favoriteCategory) { IsFavorite = favoritePaths.Contains(Normalize(path)) });
        }

        // An empty list stays out of the way (and "no match" shows the empty state), as in the original dashboard.
        if (entries.Count > 0)
        {
            categories.Add(new DashboardCategory(title, entries));
        }
    }

    private static string Normalize(string path) => System.IO.Path.TrimEndingDirectorySeparator(path);

    // The branch of each entry is read once (a git call each); the answers are cached for later rebuilds.
    private async Task LoadBranchesAsync(IReadOnlyList<DashboardEntry> entries)
    {
        foreach (DashboardEntry entry in entries)
        {
            string key = Normalize(entry.Path);
            if (_branches.TryGetValue(key, out string? known))
            {
                entry.Branch = known;
            }
        }

        DashboardEntry[] missing = [.. entries.Where(e => !_branches.ContainsKey(Normalize(e.Path)))];
        using SemaphoreSlim limit = new(4);
        await Task.WhenAll(missing.Select(async entry =>
        {
            await limit.WaitAsync();
            try
            {
                string? branch;
                try
                {
                    branch = await Task.Run(() => ReadBranch(entry.Path));
                }
                catch (Exception)
                {
                    branch = null;
                }

                _branches[Normalize(entry.Path)] = branch;
                entry.Branch = branch;
            }
            finally
            {
                limit.Release();
            }
        }));
    }

    private static async Task<string?> ReadBranchWithGitAsync(string path)
    {
        if (!Directory.Exists(path))
        {
            return null;
        }

        string git = string.IsNullOrWhiteSpace(GitCommands.AppSettings.GitCommand) ? "git" : GitCommands.AppSettings.GitCommand;
        GitCli.Result result = await GitCli.RunAsync(git, path, "rev-parse", "--abbrev-ref", "HEAD");
        return result.Succeeded && result.Output.Length > 0 ? result.Output : null;
    }
}
