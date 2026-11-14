using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands.UserRepositoryHistory;

namespace GitExtensions.Desktop.ViewModels;

public sealed class RecentRepositoryItem(string path)
{
    public string Path { get; } = path;

    public string Name { get; } = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path)) is { Length: > 0 } name ? name : path;
}

public sealed partial class RecentRepositoriesViewModel : ObservableObject
{
    private readonly IRepositoryManager _manager;
    private readonly Func<string, Task> _open;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoaded;

    public RecentRepositoriesViewModel(IRepositoryManager manager, Func<string, Task> open)
    {
        _manager = manager;
        _open = open;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<RecentRepositoryItem> Items { get; } = [];

    /// <summary>
    ///  True once the history has loaded and contains nothing: shows the friendly empty state.
    /// </summary>
    public bool IsEmpty => IsLoaded && Items.Count == 0;

    /// <summary>
    ///  Raised when an entry has been chosen (lets a hosting flyout close).
    /// </summary>
    public event Action? Chosen;

    /// <summary>
    ///  Loads the history (most recent first) without blocking the UI thread.
    /// </summary>
    public async Task LoadAsync()
    {
        IList<Repository> history = await Task.Run(_manager.LoadRecentHistoryAsync);
        Items.Clear();
        foreach (Repository repository in history)
        {
            Items.Add(new RecentRepositoryItem(repository.Path));
        }

        IsLoaded = true;
    }

    [RelayCommand]
    private async Task OpenAsync(RecentRepositoryItem? item)
    {
        if (item is null)
        {
            return;
        }

        Chosen?.Invoke();
        await _open(item.Path);
    }
}
