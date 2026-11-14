using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The "Tools" flyout: tabs for the reflog, the submodules and the worktrees. A tab loads when it is selected (or when the flyout opens);
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
    private bool _isOpen;

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

        await OpenRepository(path);
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

    partial void OnIsOpenChanged(bool value)
    {
        if (value)
        {
            _ = EnsureLoadedAsync();
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
    public async Task EnsureLoadedAsync()
    {
        int tab = SelectedTabIndex;
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
