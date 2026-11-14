using GitExtensions.Desktop.Services;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>What the page catalog needs from its host: the repository, the behaviour settings and a way to report history changes.</summary>
public sealed class SettingsContext(Func<string?> repositoryDirectory, BehaviorSettings behavior, Action historyChanged)
{
    public Func<string?> RepositoryDirectory { get; } = repositoryDirectory;

    public BehaviorSettings Behavior { get; } = behavior;

    public Action HistoryChanged { get; } = historyChanged;
}
