namespace GitExtensions.Desktop.ViewModels;

public sealed class SubmoduleItemViewModel(string name, string path, string? url, string commit, SubmoduleState state, string? describe)
{
    public string Name { get; } = name;

    public string Path { get; } = path;

    public string? Url { get; } = url;

    public string Commit { get; } = commit;

    public string ShortCommit => Commit.Length > 8 ? Commit[..8] : Commit;

    public SubmoduleState State { get; } = state;

    public string? Describe { get; } = describe;

    public bool IsInitialized => State != SubmoduleState.Uninitialized && State != SubmoduleState.Missing;

    public string StateText => State switch
    {
        SubmoduleState.Uninitialized => "not initialized",
        SubmoduleState.UpToDate => "up to date",
        SubmoduleState.Modified => "modified (new commits)",
        SubmoduleState.Dirty => "dirty (uncommitted changes)",
        SubmoduleState.Conflict => "conflict",
        _ => "missing",
    };

    public bool IsProblem => State is SubmoduleState.Conflict or SubmoduleState.Missing;

    public bool IsClean => State == SubmoduleState.UpToDate;

    public bool IsChanged => State is SubmoduleState.Modified or SubmoduleState.Dirty;
}
