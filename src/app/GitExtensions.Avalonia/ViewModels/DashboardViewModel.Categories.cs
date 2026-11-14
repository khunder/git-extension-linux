using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands.UserRepositoryHistory;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>What the inline category editor of the dashboard is doing.</summary>
public enum CategoryEditorMode
{
    None,

    /// <summary>Choose or type the category of a repository.</summary>
    Assign,

    /// <summary>Rename a category.</summary>
    Rename,

    /// <summary>Confirm deleting a category.</summary>
    Delete,
}

// Categories (favorites groups) of the dashboard and pinning (kept out of the main file).
public sealed partial class DashboardViewModel
{
    private DashboardEntry? _editorEntry;
    private string? _editorCategory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorOpen))]
    [NotifyPropertyChangedFor(nameof(IsTextEditor))]
    [NotifyPropertyChangedFor(nameof(IsAssigning))]
    [NotifyPropertyChangedFor(nameof(IsDeleting))]
    private CategoryEditorMode _editorMode;

    [ObservableProperty]
    private string _editorTitle = string.Empty;

    [ObservableProperty]
    private string _editorText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditorError))]
    private string? _editorError;

    public bool IsEditorOpen => EditorMode != CategoryEditorMode.None;

    /// <summary>The editor asks for a name (assign or rename) rather than a confirmation.</summary>
    public bool IsTextEditor => EditorMode is CategoryEditorMode.Assign or CategoryEditorMode.Rename;

    public bool IsDeleting => EditorMode == CategoryEditorMode.Delete;

    public bool IsAssigning => EditorMode == CategoryEditorMode.Assign;

    public bool HasEditorError => !string.IsNullOrEmpty(EditorError);

    /// <summary>The existing favorite categories, offered when assigning a repository.</summary>
    public IReadOnlyList<string> ExistingCategories => [.. _favorites
        .Select(f => string.IsNullOrWhiteSpace(f.Category) ? DefaultFavoriteCategory : f.Category!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.OrdinalIgnoreCase)];

    [RelayCommand]
    private void BeginAssignCategory(DashboardEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        _editorEntry = entry;
        _editorCategory = null;
        EditorTitle = $"Category of {entry.Name}";
        EditorText = entry.FavoriteCategory ?? string.Empty;
        EditorError = null;
        OnPropertyChanged(nameof(ExistingCategories));
        EditorMode = CategoryEditorMode.Assign;
    }

    [RelayCommand]
    private void BeginRenameCategory(DashboardCategory? category)
    {
        if (category?.FavoriteKey is not { } key)
        {
            return;
        }

        _editorEntry = null;
        _editorCategory = key;
        EditorTitle = $"Rename category '{key}'";
        EditorText = key;
        EditorError = null;
        EditorMode = CategoryEditorMode.Rename;
    }

    [RelayCommand]
    private void BeginDeleteCategory(DashboardCategory? category)
    {
        if (category?.FavoriteKey is not { } key)
        {
            return;
        }

        _editorEntry = null;
        _editorCategory = key;
        EditorTitle = $"Delete category '{key}'?";
        EditorError = null;
        EditorMode = CategoryEditorMode.Delete;
    }

    [RelayCommand]
    private void CancelEditor()
    {
        EditorMode = CategoryEditorMode.None;
        EditorError = null;
    }

    /// <summary>Picks an existing category in the assign editor.</summary>
    [RelayCommand]
    private void PickCategory(string? category)
    {
        if (category is not null)
        {
            EditorText = category;
        }
    }

    [RelayCommand]
    private async Task ApplyEditorAsync()
    {
        string text = EditorText.Trim();
        try
        {
            switch (EditorMode)
            {
                case CategoryEditorMode.Assign when _editorEntry is { } entry:
                    if (text.Length == 0)
                    {
                        EditorError = "Enter a category name.";
                        return;
                    }

                    await Task.Run(() => _manager.AssignCategoryAsync(new Repository(entry.Path), text));
                    break;

                case CategoryEditorMode.Rename when _editorCategory is { } old:
                    if (text.Length == 0)
                    {
                        EditorError = "Enter a category name.";
                        return;
                    }

                    if (!string.Equals(text, old, StringComparison.Ordinal))
                    {
                        await ChangeCategoryAsync(old, text);
                    }

                    break;

                case CategoryEditorMode.Delete when _editorCategory is { } doomed:
                    await ChangeCategoryAsync(doomed, null);
                    break;
            }
        }
        catch (Exception ex)
        {
            EditorError = $"Could not change the categories: {ex.Message}";
            return;
        }

        EditorMode = CategoryEditorMode.None;
        await LoadAsync();
    }

    // Moves every favorite of a category to another one (null drops them from the favorites), as the original dashboard does.
    private Task ChangeCategoryAsync(string from, string? to) => Task.Run(async () =>
    {
        IList<Repository> favorites = await _manager.LoadFavouriteHistoryAsync();
        foreach (Repository repository in favorites.Where(f => string.Equals(string.IsNullOrWhiteSpace(f.Category) ? DefaultFavoriteCategory : f.Category!.Trim(), from, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            await _manager.AssignCategoryAsync(repository, to);
        }
    });

    [RelayCommand]
    private async Task TogglePinAsync(DashboardEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        try
        {
            await Task.Run(async () =>
            {
                IList<Repository> recent = await _manager.LoadRecentHistoryAsync();
                Repository? repository = recent.FirstOrDefault(r => string.Equals(Normalize(r.Path), Normalize(entry.Path), StringComparison.OrdinalIgnoreCase));
                if (repository is null)
                {
                    return;
                }

                repository.Anchor = entry.IsPinned ? Repository.RepositoryAnchor.None : Repository.RepositoryAnchor.AnchoredInTop;
                await _manager.SaveRecentHistoryAsync(recent);
            });
        }
        catch (Exception ex)
        {
            _reportError($"Could not pin {entry.Path}: {ex.Message}");
        }

        await LoadAsync();
    }
}
