namespace GitExtensionsDesktopTests;

/// <summary>
///  Stash helpers for <see cref="TestRepository"/> (kept out of the shared class).
/// </summary>
internal static class StashTestExtensions
{
    /// <summary>Stash needs a committer identity, which a CI machine may not have.</summary>
    public static void ConfigureIdentity(this TestRepository repository)
    {
        repository.RunGit("config", "user.name", "Test Author");
        repository.RunGit("config", "user.email", "test@example.com");
    }

    /// <summary>Stashes the current changes of tracked files under <paramref name="message"/>.</summary>
    public static void AddStash(this TestRepository repository, string message, string file = "a.txt", string? content = null)
    {
        repository.ConfigureIdentity();
        File.WriteAllText(Path.Combine(repository.Path, file), content ?? "content of " + message);
        repository.RunGit("stash", "push", "-m", message);
    }

    /// <summary>Creates three stashes (oldest first): "first", "second", "third on b".</summary>
    public static void AddSeveralStashes(this TestRepository repository)
    {
        repository.AddStash("first");
        repository.AddStash("second");
        repository.AddStash("third on b", "b.txt", "bbb\nline two\n");
    }
}
