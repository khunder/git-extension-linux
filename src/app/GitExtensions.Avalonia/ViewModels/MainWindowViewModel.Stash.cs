namespace GitExtensions.Desktop.ViewModels;

// Stash list and operations (kept out of the main file).
public sealed partial class MainWindowViewModel
{
    private StashViewModel? _stash;

    /// <summary>Stash list, apply/pop/drop and "stash changes". Reloads whenever the branch is (re)loaded.</summary>
    public StashViewModel Stash
    {
        get
        {
            if (_stash is null)
            {
                _stash = new StashViewModel(() => _module, () => OpenAsync(RepositoryPath), message => ErrorMessage = message, message => Status = message);
                PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(Branch) && Branch is not null)
                    {
                        _ = _stash.LoadAsync();
                    }
                };
            }

            return _stash;
        }
    }
}
