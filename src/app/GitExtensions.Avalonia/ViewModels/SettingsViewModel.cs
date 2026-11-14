// The tasks awaited here (_switchTask and its predecessors) are started by this class itself, on the UI thread.
#pragma warning disable VSTHRD003

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The settings dialog, modelled on the original: a searchable tree of pages on the left, the selected page on the right.
///  Pages save instantly (when switching pages, on Apply and on OK) and Cancel does not revert what was saved.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly Func<string?> _repositoryDirectory;
    private readonly BehaviorSettings _behavior;
    private Task _switchTask = Task.CompletedTask;
    private bool _historyDirty;
    private bool _resetting;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(ShowSourceSelector))]
    [NotifyPropertyChangedFor(nameof(IsRowPage))]
    private SettingsPageViewModel? _selectedPage;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGlobalSource))]
    [NotifyPropertyChangedFor(nameof(IsLocalSource))]
    [NotifyPropertyChangedFor(nameof(IsEffectiveSource))]
    private SettingsSource _source;

    public SettingsViewModel(Func<string?> repositoryDirectory, BehaviorSettings? behavior = null)
    {
        _repositoryDirectory = repositoryDirectory;
        _behavior = behavior ?? BehaviorSettings.Current;
    }

    /// <summary>
    ///  Raised after a page was saved; the argument tells whether history-related options changed.
    /// </summary>
    public event Action<bool>? Saved;

    /// <summary>Set by the view once it is attached to a window.</summary>
    public IFilePicker? FilePicker { get; set; }

    /// <summary>Set by the view once it is attached to a window.</summary>
    public IFolderPicker? FolderPicker { get; set; }

    /// <summary>The top-level nodes of the tree.</summary>
    public ObservableCollection<SettingsPageViewModel> Pages { get; } = [];

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasRepository => !string.IsNullOrEmpty(_repositoryDirectory());

    public string WindowTitle => SelectedPage is null ? "Settings" : "Settings - " + (SelectedPage.IsChecklist ? "Checklist" : SelectedPage.Title);

    /// <summary>Only git config pages can be edited per repository or viewed as the effective result.</summary>
    public bool ShowSourceSelector => SelectedPage?.UsesGitConfig == true;

    public bool IsRowPage => SelectedPage is { IsChecklist: false };

    public bool IsGlobalSource
    {
        get => Source == SettingsSource.Global;
        set
        {
            if (value)
            {
                Source = SettingsSource.Global;
            }
        }
    }

    public bool IsLocalSource
    {
        get => Source == SettingsSource.Local;
        set
        {
            if (value)
            {
                Source = SettingsSource.Local;
            }
        }
    }

    public bool IsEffectiveSource
    {
        get => Source == SettingsSource.Effective;
        set
        {
            if (value)
            {
                Source = SettingsSource.Effective;
            }
        }
    }

    /// <summary>Every page of the tree, in tree order.</summary>
    public IEnumerable<SettingsPageViewModel> AllPages => Pages.SelectMany(p => p.Descendants());

    public SettingsPageViewModel? FindPage(string id) => AllPages.FirstOrDefault(p => p.Id == id);

    /// <summary>
    ///  Shows the dialog with freshly loaded values, on the page with the given id (the checklist by default).
    /// </summary>
    [RelayCommand]
    public Task OpenAsync() => OpenAsync(null);

    public async Task OpenAsync(string? pageId)
    {
        ErrorMessage = null;
        SearchText = string.Empty;
        Source = SettingsSource.Global;
        _historyDirty = false;
        OnPropertyChanged(nameof(HasRepository));

        Pages.Clear();
        SettingsContext context = new(_repositoryDirectory, _behavior, () => _historyDirty = true);
        foreach (SettingsPageViewModel page in SettingsPageCatalog.Build(context))
        {
            Pages.Add(page);
        }

        _resetting = true;
        SelectedPage = null;
        _resetting = false;
        IsOpen = true;
        await SelectPageAsync(FindPage(pageId ?? SettingsPageIds.Checklist) ?? Pages[0]);
    }

    /// <summary>Saves the page being left, then shows (and loads) <paramref name="page"/>.</summary>
    public async Task SelectPageAsync(SettingsPageViewModel page)
    {
        SelectedPage = page;
        await _switchTask;
    }

    partial void OnSelectedPageChanged(SettingsPageViewModel? oldValue, SettingsPageViewModel? newValue)
    {
        if (_resetting)
        {
            return;
        }

        Task previous = _switchTask;
        _switchTask = SwitchAsync(previous, oldValue, newValue);
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter(value.Trim());

    partial void OnSourceChanged(SettingsSource value)
    {
        if (SelectedPage is { UsesGitConfig: true } page)
        {
            Task previous = _switchTask;
            _switchTask = ChangeSourceAsync(previous, page);
        }
    }

    /// <summary>Waits until the page switch or source change in progress has finished.</summary>
    public Task WhenIdleAsync() => _switchTask;

    private async Task SwitchAsync(Task previous, SettingsPageViewModel? oldPage, SettingsPageViewModel? newPage)
    {
        await previous;
        IsBusy = true;
        try
        {
            if (oldPage is not null)
            {
                await CommitAsync(oldPage);
            }

            if (newPage is not null)
            {
                await LoadAsync(newPage);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ChangeSourceAsync(Task previous, SettingsPageViewModel page)
    {
        await previous;
        IsBusy = true;
        try
        {
            // Edits made under the previous source belong to it: save them first.
            await CommitAsync(page);
            await LoadAsync(page);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadAsync(SettingsPageViewModel page)
    {
        try
        {
            if (page.GitConfig is { } store)
            {
                store.Source = Source;
                await store.LoadAsync();
            }

            foreach (SettingRow row in page.Rows)
            {
                row.Load();
                if (page.GitConfig is { } git && row is not HeaderRow and not NoticeRow)
                {
                    row.IsEnabled = !git.IsReadOnly;
                }
            }

            if (page.Checklist is { } checklist)
            {
                await checklist.RescanAsync();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not load the settings: {ex.Message}";
        }
    }

    /// <summary>Saves the page; false (with <see cref="ErrorMessage"/> set) when something was refused or failed.</summary>
    private async Task<bool> CommitAsync(SettingsPageViewModel page)
    {
        try
        {
            if (page.Validate is not null && await page.Validate() is { } refusal)
            {
                ErrorMessage = refusal;
                return false;
            }

            foreach (SettingRow row in page.Rows)
            {
                await row.SaveAsync();
            }

            if (page.GitConfig is { } store)
            {
                await store.FlushAsync();
            }

            page.Checklist?.PersistStartupCheck();
            AppSettings.SaveSettings();
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not save the settings: {ex.Message}";
            return false;
        }

        if (_historyDirty)
        {
            _historyDirty = false;
            Saved?.Invoke(true);
        }

        return true;
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (SelectedPage is null)
        {
            return;
        }

        await _switchTask;
        IsBusy = true;
        try
        {
            await CommitAsync(SelectedPage);
            if (SelectedPage.Checklist is { } checklist)
            {
                await checklist.RescanAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Saves the selected page and closes the dialog; stays open when saving failed.</summary>
    [RelayCommand]
    public async Task OkAsync()
    {
        await _switchTask;
        IsBusy = true;
        try
        {
            if (SelectedPage is null || await CommitAsync(SelectedPage))
            {
                IsOpen = false;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Closes the dialog. Like the original, this does not revert pages that were saved already.</summary>
    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        ErrorMessage = null;
    }

    [RelayCommand]
    private async Task RescanChecklistAsync()
    {
        if (FindPage(SettingsPageIds.Checklist) is { Checklist: { } checklist } page)
        {
            await CommitAsync(page);
            await checklist.RescanAsync();
        }
    }

    [RelayCommand]
    private Task GoToPageAsync(string? pageId)
        => FindPage(pageId ?? string.Empty) is { } page ? SelectPageAsync(page) : Task.CompletedTask;

    [RelayCommand]
    private async Task RunActionAsync(ActionRow? row)
    {
        if (row is not null)
        {
            await row.RunAsync();
        }
    }

    [RelayCommand]
    private async Task BrowseAsync(TextRow? row)
    {
        if (row is null)
        {
            return;
        }

        string? picked = row.Browse switch
        {
            BrowseKind.File when FilePicker is not null => await FilePicker.PickFileAsync(row.Label),
            BrowseKind.Folder when FolderPicker is not null => await FolderPicker.PickFolderAsync(row.Label),
            _ => null,
        };
        if (picked is not null)
        {
            row.Value = picked;
        }
    }

    private void ApplyFilter(string query)
    {
        bool searching = query.Length > 0;
        foreach (SettingsPageViewModel page in AllPages)
        {
            page.IsTitleMatch = searching && page.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
            foreach (SettingRow row in page.Rows)
            {
                row.IsMatch = searching && row is not NoticeRow && row.Matches(query);
            }
        }

        foreach (SettingsPageViewModel top in Pages)
        {
            UpdateVisibility(top, searching);
        }

        bool currentIsMatch = SelectedPage is { IsVisible: true } current && (current.IsTitleMatch || current.Rows.Any(r => r.IsMatch));
        if (searching && !currentIsMatch)
        {
            SettingsPageViewModel? first = AllPages.FirstOrDefault(p => p.IsTitleMatch) ?? AllPages.FirstOrDefault(p => p.Rows.Any(r => r.IsMatch));
            if (first is not null)
            {
                SelectedPage = first;
            }
        }
    }

    private static bool UpdateVisibility(SettingsPageViewModel page, bool searching)
    {
        bool childVisible = false;
        foreach (SettingsPageViewModel child in page.Children)
        {
            childVisible |= UpdateVisibility(child, searching);
        }

        page.IsVisible = !searching || page.IsTitleMatch || childVisible || page.Rows.Any(r => r.IsMatch);
        if (searching)
        {
            page.IsExpanded = true;
        }

        return page.IsVisible;
    }
}
