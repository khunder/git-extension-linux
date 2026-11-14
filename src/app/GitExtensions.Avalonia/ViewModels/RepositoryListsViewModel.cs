using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitCommands.UserRepositoryHistory;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>The settings of the repository lists, persisted through <see cref="AppSettings"/> as the WinForms app does.</summary>
public sealed record RepositoryListOptions(
    int MaxTopRepositories,
    int HistorySize,
    bool HideTopFromRecent,
    bool SortTop,
    bool SortRecent,
    ShorteningRecentRepoPathStrategy Shortening)
{
    public static RepositoryListOptions FromAppSettings() => new(
        AppSettings.MaxTopRepositories,
        AppSettings.RecentRepositoriesHistorySize,
        AppSettings.HideTopRepositoriesFromRecentList.Value,
        AppSettings.SortTopRepos,
        AppSettings.SortRecentRepos,
        AppSettings.ShorteningRecentRepoPathStrategy);

    public static void ToAppSettings(RepositoryListOptions options)
    {
        AppSettings.MaxTopRepositories = options.MaxTopRepositories;
        AppSettings.RecentRepositoriesHistorySize = options.HistorySize;
        AppSettings.HideTopRepositoriesFromRecentList.Value = options.HideTopFromRecent;
        AppSettings.SortTopRepos = options.SortTop;
        AppSettings.SortRecentRepos = options.SortRecent;
        AppSettings.ShorteningRecentRepoPathStrategy = options.Shortening;
    }
}

/// <summary>A repository row of the "Favorite repositories" dialog.</summary>
public sealed partial class RepositoryListRow : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinText))]
    private bool _isPinned;

    public RepositoryListRow(Repository repository, bool isFavorite)
    {
        Repository = repository;
        IsFavorite = isFavorite;
        IsPinned = repository.Anchor == Repository.RepositoryAnchor.AnchoredInTop;
        Exists = Directory.Exists(repository.Path);
    }

    public Repository Repository { get; }

    public string Path => Repository.Path;

    public bool IsFavorite { get; }

    public bool Exists { get; }

    public string Category => Repository.Category ?? string.Empty;

    public string PinText => IsPinned ? "Unpin" : "Pin";
}

/// <summary>
///  The "Favorite repositories" dialog (the counterpart of the original repository list settings): how many
///  repositories are kept and shown on top, sorting and path shortening, pinning, and removing repositories from
///  the recent and favorite lists. Nothing is stored before "Save".
/// </summary>
public sealed partial class RepositoryListsViewModel : ObservableObject
{
    private readonly ILocalRepositoryManager _manager;
    private readonly Func<RepositoryListOptions> _load;
    private readonly Action<RepositoryListOptions> _save;
    private readonly Action<string> _reportError;
    private readonly Func<Task> _saved;
    private IList<Repository> _recent = [];
    private IList<Repository> _favorites = [];

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private decimal? _maxTopRepositories;

    [ObservableProperty]
    private decimal? _historySize;

    [ObservableProperty]
    private bool _hideTopFromRecent;

    [ObservableProperty]
    private bool _sortTop;

    [ObservableProperty]
    private bool _sortRecent;

    [ObservableProperty]
    private int _shorteningIndex;

    [ObservableProperty]
    private string? _message;

    public RepositoryListsViewModel(
        ILocalRepositoryManager manager,
        Action<string> reportError,
        Func<Task>? saved = null,
        Func<RepositoryListOptions>? load = null,
        Action<RepositoryListOptions>? save = null)
    {
        _manager = manager;
        _reportError = reportError;
        _saved = saved ?? (static () => Task.CompletedTask);
        _load = load ?? RepositoryListOptions.FromAppSettings;
        _save = save ?? RepositoryListOptions.ToAppSettings;
    }

    public static IReadOnlyList<string> ShorteningNames { get; } = ["Do not shorten", "Most significant directory", "Middle dots"];

    public ObservableCollection<RepositoryListRow> Recent { get; } = [];

    public ObservableCollection<RepositoryListRow> Favorites { get; } = [];

    public async Task OpenAsync()
    {
        RepositoryListOptions options = _load();
        MaxTopRepositories = options.MaxTopRepositories;
        HistorySize = options.HistorySize;
        HideTopFromRecent = options.HideTopFromRecent;
        SortTop = options.SortTop;
        SortRecent = options.SortRecent;
        ShorteningIndex = (int)options.Shortening;
        Message = null;
        IsOpen = true;
        try
        {
            _recent = await Task.Run(_manager.LoadRecentHistoryAsync);
            _favorites = await Task.Run(_manager.LoadFavouriteHistoryAsync);
        }
        catch (Exception ex)
        {
            Message = $"Could not read the repository lists: {ex.Message}";
        }

        Refill();
    }

    private void Refill()
    {
        Recent.Clear();
        foreach (Repository repository in _recent.OrderBy(r => r.Anchor == Repository.RepositoryAnchor.AnchoredInTop ? 0 : 1))
        {
            Recent.Add(new RepositoryListRow(repository, isFavorite: false));
        }

        Favorites.Clear();
        foreach (Repository repository in _favorites.OrderBy(r => r.Category ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            Favorites.Add(new RepositoryListRow(repository, isFavorite: true));
        }
    }

    [RelayCommand]
    private void Cancel() => IsOpen = false;

    [RelayCommand]
    private void TogglePin(RepositoryListRow? row)
    {
        if (row is null || row.IsFavorite)
        {
            return;
        }

        row.Repository.Anchor = row.IsPinned ? Repository.RepositoryAnchor.None : Repository.RepositoryAnchor.AnchoredInTop;
        Refill();
    }

    [RelayCommand]
    private void Remove(RepositoryListRow? row)
    {
        if (row is null)
        {
            return;
        }

        (row.IsFavorite ? _favorites : _recent).Remove(row.Repository);
        Refill();
    }

    [RelayCommand]
    private void RemoveMissing()
    {
        int removed = _recent.Count(r => !Directory.Exists(r.Path)) + _favorites.Count(r => !Directory.Exists(r.Path));
        _recent = [.. _recent.Where(r => Directory.Exists(r.Path))];
        _favorites = [.. _favorites.Where(r => Directory.Exists(r.Path))];
        Message = removed == 0 ? "All listed repositories exist." : $"{removed} missing repositories will be removed when you save.";
        Refill();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            _save(new RepositoryListOptions(
                (int)Math.Clamp(MaxTopRepositories ?? 0, 0, 100),
                (int)Math.Clamp(HistorySize ?? 30, 1, 500),
                HideTopFromRecent,
                SortTop,
                SortRecent,
                Enum.IsDefined((ShorteningRecentRepoPathStrategy)ShorteningIndex) ? (ShorteningRecentRepoPathStrategy)ShorteningIndex : ShorteningRecentRepoPathStrategy.MostSignDir));
            await Task.Run(() => _manager.SaveRecentHistoryAsync(_recent));
            await Task.Run(() => _manager.SaveFavouriteHistoryAsync(_favorites));
            IsOpen = false;
            await _saved();
        }
        catch (Exception ex)
        {
            Message = $"Could not save: {ex.Message}";
            _reportError(Message);
        }
    }
}
