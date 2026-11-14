namespace GitExtensions.Desktop.ViewModels;

public sealed partial class RemoteOperationsViewModel
{
    /// <summary>Gets whether the last command run through this view-model finished successfully.</summary>
    public bool LastRunSucceeded { get; private set; }

    /// <summary>
    ///  Runs an arbitrary network git command (e.g. push of one branch) through the same log, credential prompts
    ///  and error reporting as fetch/pull/push.
    /// </summary>
    public Task RunGitAsync(string title, IReadOnlyList<string> arguments) => RunAsync(title, [.. arguments]);
}
