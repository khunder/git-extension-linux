using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The repository tools: the reflog, the submodules and the worktrees, each in its own dialog. A tool loads when its dialog opens;
///  everything reloads when the repository changes.
/// </summary>
public sealed partial class RepositoryToolsViewModel : ObservableObject
{
    public const int ReflogTab = 0;
    public const int SubmodulesTab = 1;
    public const int WorktreesTab = 2;

    private readonly ToolsContext _context;
    private readonly bool[] _loaded = new bool[3];

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackPath), nameof(BackText))]
    private string? _backPath;

    public RepositoryToolsViewModel(ToolsContext context, Func<string, Task> openRepository)
    {
        _context = context;
        OpenRepository = openRepository;
        Reflog = new ReflogViewModel(context);
        Submodules = new SubmodulesViewModel(context);
        Worktrees = new WorktreesViewModel(context);
        Reflog.Owner = this;
        Submodules.Owner = this;
        Worktrees.Owner = this;
        Reflog.PropertyChanged += (_, e) => OnToolOpenChanged(e, ReflogTab, Reflog);
        Submodules.PropertyChanged += (_, e) => OnToolOpenChanged(e, SubmodulesTab, Submodules);
        Worktrees.PropertyChanged += (_, e) => OnToolOpenChanged(e, WorktreesTab, Worktrees);
        context.BusyChanged += () => IsBusy = context.IsBusy;
    }

    public ReflogViewModel Reflog { get; }

    public SubmodulesViewModel Submodules { get; }

    public WorktreesViewModel Worktrees { get; }

    public Func<string, Task> OpenRepository { get; }

    public Services.IClipboardService? Clipboard
    {
        get => _context.Clipboard;
        set => _context.Clipboard = value;
    }

    /// <summary>Gets a value indicating whether one of the tool dialogs is open.</summary>
    public bool IsOpen => Reflog.IsOpen || Submodules.IsOpen || Worktrees.IsOpen;

    public bool HasBackPath => BackPath is not null;

    public string BackText => BackPath is null ? string.Empty : $"◂ Back to {Path.GetFileName(BackPath.TrimEnd('/', '\\'))}";

    /// <summary>Remembers the repository that is open now, then opens <paramref name="path"/> (a submodule or worktree).</summary>
    public async Task OpenFromToolsAsync(string path)
    {
        string? current = _context.Module?.WorkingDir;
        if (current is not null && !string.Equals(Path.GetFullPath(current), Path.GetFullPath(path), StringComparison.Ordinal))
        {
            BackPath = current;
        }

        // The dialog is modal over the old repository: close it before switching.
        Reflog.IsOpen = false;
        Submodules.IsOpen = false;
        Worktrees.IsOpen = false;
        await OpenRepository(path);
    }

    /// <summary>Opens the dialog of a tool: <see cref="ReflogTab"/>, <see cref="SubmodulesTab"/> or <see cref="WorktreesTab"/>.</summary>
    public void Open(int tab)
    {
        if (tab is >= 0 and <= 2)
        {
            _loaded[tab] = false; // always show fresh data
        }

        SelectedTabIndex = tab;
        ToolDialogViewModel dialog = tab switch
        {
            SubmodulesTab => Submodules,
            WorktreesTab => Worktrees,
            _ => Reflog,
        };
        dialog.IsOpen = true;
    }

    [RelayCommand]
    public async Task GoBackAsync()
    {
        if (BackPath is { } path)
        {
            BackPath = null;
            await OpenRepository(path);
        }
    }

    partial void OnSelectedTabIndexChanged(int value) => _ = EnsureLoadedAsync();

    private void OnToolOpenChanged(System.ComponentModel.PropertyChangedEventArgs e, int tab, ToolDialogViewModel tool)
    {
        if (e.PropertyName != nameof(ToolDialogViewModel.IsOpen))
        {
            return;
        }

        OnPropertyChanged(nameof(IsOpen));
        if (tool.IsOpen)
        {
            SelectedTabIndex = tab;
            _ = EnsureLoadedAsync(tab);
        }
    }

    /// <summary>The repository changed: forget what was loaded; reload the visible tab when the flyout is open.</summary>
    public async Task OnRepositoryChangedAsync()
    {
        Array.Clear(_loaded);
        if (_context.Module is null)
        {
            Reflog.Clear();
            Submodules.Clear();
            Worktrees.Clear();
            return;
        }

        if (IsOpen)
        {
            await EnsureLoadedAsync();
        }
    }

    /// <summary>Loads the selected tab if it has not been loaded since the repository changed.</summary>
    public Task EnsureLoadedAsync() => EnsureLoadedAsync(SelectedTabIndex);

    private async Task EnsureLoadedAsync(int tab)
    {
        if (tab is < 0 or > 2 || _loaded[tab])
        {
            return;
        }

        _loaded[tab] = true;
        await ReloadTabAsync(tab);
    }

    public Task ReloadSelectedAsync() => ReloadTabAsync(SelectedTabIndex);

    private Task ReloadTabAsync(int tab) => tab switch
    {
        ReflogTab => Reflog.LoadAsync(),
        SubmodulesTab => Submodules.LoadAsync(),
        WorktreesTab => Worktrees.LoadAsync(),
        _ => Task.CompletedTask,
    };
}
