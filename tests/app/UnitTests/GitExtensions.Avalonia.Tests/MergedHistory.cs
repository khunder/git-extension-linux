namespace GitExtensionsDesktopTests;

/// <summary>
///  Adds a history with a fork and a merge on top of <see cref="TestRepository"/>'s base history.
/// </summary>
internal static class MergedHistory
{
    /// <summary>
    ///  After the two base commits (a, b): <c>feature</c> gets "Feature work" and "Feature more", <c>main</c> gets "Main work",
    ///  they are merged (no fast-forward) as "Merge feature" and "After merge" follows. Ends on <c>main</c>.
    ///  Also sets a local committer identity so revert and cherry-pick work.
    /// </summary>
    public static void AddMergedHistory(this TestRepository repository)
    {
        repository.RunGit("config", "user.name", "Test Author");
        repository.RunGit("config", "user.email", "test@example.com");
        repository.RunGit("checkout", "--quiet", "-b", "feature");
        repository.CommitFile("f.txt", "f", "Feature work");
        repository.CommitFile("g.txt", "g", "Feature more");
        repository.RunGit("checkout", "--quiet", "main");
        repository.CommitFile("m.txt", "m", "Main work");
        repository.RunGit("merge", "--quiet", "--no-ff", "feature", "-m", "Merge feature");
        repository.CommitFile("s.txt", "s", "After merge");
    }

    public static void CommitFile(this TestRepository repository, string file, string content, string message)
    {
        File.WriteAllText(Path.Combine(repository.Path, file), content);
        repository.RunGit("add", file);
        repository.RunGit("commit", "--quiet", "-m", message);
    }
}
