using CommunityToolkit.Mvvm.Input;

namespace GitExtensions.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private SettingsViewModel? _settings;

    /// <summary>
    ///  The settings panel. Saving history-related options reloads the open repository so they take effect.
    /// </summary>
    public SettingsViewModel Settings
    {
        get
        {
            if (_settings is null)
            {
                _settings = new SettingsViewModel(() => _module?.WorkingDir);
                _settings.Saved += historyChanged =>
                {
                    if (historyChanged && HasOpenedRepository)
                    {
                        _ = OpenAsync(RepositoryPath);
                    }
                };
            }

            return _settings;
        }
    }

    [RelayCommand]
    private Task OpenSettingsAsync() => Settings.OpenAsync();
}
