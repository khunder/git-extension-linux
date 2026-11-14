using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitCommands;
using GitExtensions.Extensibility;
using GitExtUtils;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>Submodules of the open repository (recursive) with init/update/sync and "open as repository".</summary>
public sealed partial class SubmodulesViewModel : ObservableObject
{
    private readonly ToolsContext _context;
    private int _loadVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private SubmoduleItemViewModel? _selectedSubmodule;

    [ObservableProperty]
    private string _emptyText = string.Empty;

    public SubmodulesViewModel(ToolsContext context) => _context = context;

    public ObservableCollection<SubmoduleItemViewModel> Items { get; } = [];

    public bool HasSelection => SelectedSubmodule is not null;

    public void Clear()
    {
        _loadVersion++;
        Items.Clear();
        SelectedSubmodule = null;
    }

    public async Task LoadAsync()
    {
        GitModule? module = _context.Module;
        if (module is null)
        {
            Clear();
            return;
        }

        int version = ++_loadVersion;
        try
        {
            List<SubmoduleItemViewModel> items = await _context.RunBusyAsync(() => Read(module));
            if (version != _loadVersion)
            {
                return;
            }

            string? selectedPath = SelectedSubmodule?.Path;
            Items.Clear();
            foreach (SubmoduleItemViewModel item in items)
            {
                Items.Add(item);
            }

            EmptyText = items.Count == 0 ? "This repository has no submodules." : string.Empty;
            SelectedSubmodule = selectedPath is null ? null : Items.FirstOrDefault(i => i.Path == selectedPath);
        }
        catch (Exception ex)
        {
            _context.ReportError($"Could not list the submodules: {ex.Message}");
        }
    }

    private static List<SubmoduleItemViewModel> Read(GitModule module)
    {
        ExecutionResult status = module.GitExecutable.Execute("submodule status --recursive", throwOnErrorExit: false);
        if (!status.ExitedSuccessfully)
        {
            return [];
        }

        // .gitmodules only knows the top-level submodules; nested ones get their URL from their own .gitmodules (not read here).
        ExecutionResult config = module.GitExecutable.Execute("config -f .gitmodules --get-regexp \"^submodule\\..*\\.(path|url)$\"", throwOnErrorExit: false);
        IReadOnlyList<GitModulesEntry> declared = config.ExitedSuccessfully ? SubmoduleStatusParser.ParseGitModules(config.StandardOutput) : [];

        List<SubmoduleItemViewModel> items = [];
        foreach (SubmoduleStatusLine line in SubmoduleStatusParser.ParseStatus(status.StandardOutput))
        {
            GitModulesEntry? entry = declared.FirstOrDefault(d => d.Path == line.Path);
            SubmoduleState state = line.State;
            string fullPath = Path.Combine(module.WorkingDir, line.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullPath))
            {
                state = SubmoduleState.Missing;
            }
            else if (state != SubmoduleState.Uninitialized && state != SubmoduleState.Conflict)
            {
                // "git submodule status" cannot see uncommitted changes inside the submodule.
                ExecutionResult dirty = module.GitExecutable.Execute($"-C {fullPath.Quote()} status --porcelain", throwOnErrorExit: false);
                if (dirty.ExitedSuccessfully && !string.IsNullOrWhiteSpace(dirty.StandardOutput))
                {
                    state = SubmoduleState.Dirty;
                }
            }

            items.Add(new SubmoduleItemViewModel(entry?.Name ?? line.Path, line.Path, entry?.Url, line.Commit, state, line.Describe));
        }

        return items;
    }

    // ---- commands ----

    [RelayCommand]
    public Task<bool> InitAsync(SubmoduleItemViewModel? item)
        => (item ?? SelectedSubmodule) is { } target
            ? RunAsync($"Initialized {target.Path}", new GitArgumentBuilder("submodule") { "init", "--", target.Path.Quote() })
            : Task.FromResult(false);

    [RelayCommand]
    public Task<bool> UpdateAsync(SubmoduleItemViewModel? item)
        => (item ?? SelectedSubmodule) is { } target
            ? RunAsync($"Updated {target.Path}", new GitArgumentBuilder("submodule") { "update", "--init", "--recursive", "--", target.Path.Quote() })
            : Task.FromResult(false);

    [RelayCommand]
    public Task<bool> UpdateAllAsync()
        => RunAsync("Updated all submodules", new GitArgumentBuilder("submodule") { "update", "--init", "--recursive" });

    [RelayCommand]
    public Task<bool> SyncAsync()
        => RunAsync("Synchronized the submodule URLs", new GitArgumentBuilder("submodule") { "sync", "--recursive" });

    [RelayCommand]
    public async Task<bool> OpenAsync(SubmoduleItemViewModel? item)
    {
        SubmoduleItemViewModel? chosen = item ?? SelectedSubmodule;
        if (chosen is null || _context.Module is not { } module)
        {
            return false;
        }

        if (!chosen.IsInitialized)
        {
            _context.ReportError("Initialize the submodule before opening it.");
            return false;
        }

        SubmoduleItemViewModel target = chosen;
        string path = Path.GetFullPath(Path.Combine(module.WorkingDir, target.Path.Replace('/', Path.DirectorySeparatorChar)));
        await _context.OpenRepositoryAsync(path);
        return true;
    }

    private async Task<bool> RunAsync(string successMessage, ArgumentString arguments)
    {
        GitModule? module = _context.Module;
        if (module is null)
        {
            return false;
        }

        try
        {
            ExecutionResult result = await _context.RunGitAsync(module, arguments);
            if (!result.ExitedSuccessfully)
            {
                _context.ReportError($"git {arguments.Arguments?.Split(' ')[0]} {arguments.Arguments?.Split(' ').ElementAtOrDefault(1)} failed: {ToolsContext.Describe(result)}");
                await LoadAsync();
                return false;
            }

            _context.ReportStatus(successMessage);
            await _context.RefreshAsync();
            await LoadAsync();
            return true;
        }
        catch (Exception ex)
        {
            _context.ReportError($"Git operation failed: {ex.Message}");
            return false;
        }
    }
}
