namespace GitExtensions.Desktop.ViewModels;

// Repository tools: reflog, submodules, worktrees (kept out of the main file).
public sealed partial class MainWindowViewModel
{
    private RepositoryToolsViewModel? _tools;

    /// <summary>Reflog / submodules / worktrees. Reloads whenever the branch is (re)loaded.</summary>
    public RepositoryToolsViewModel Tools
    {
        get
        {
            if (_tools is null)
            {
                ToolsContext context = new(() => _module, () => OpenAsync(RepositoryPath), message => ErrorMessage = message, message => Status = message, path => Tools.OpenFromToolsAsync(path));
                _tools = new RepositoryToolsViewModel(context, path => OpenAsync(path));
                PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(Branch) && Branch is not null)
                    {
                        _ = _tools.OnRepositoryChangedAsync();
                    }
                };
            }

            return _tools;
        }
    }
}
