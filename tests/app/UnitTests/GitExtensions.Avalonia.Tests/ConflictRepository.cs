namespace GitExtensionsDesktopTests;

/// <summary>Helpers that build diverged histories (with and without conflicts) on top of <see cref="TestRepository"/>.</summary>
internal static class ConflictRepository
{
    /// <summary>
    ///  Cleans the working tree and sets an identity. <c>main</c> and <c>topic</c> both start from "Base" (a.txt, b.txt, conflict.txt).
    ///  <c>topic</c> changes conflict.txt and adds t.txt; <c>main</c> changes conflict.txt and adds m.txt. Ends on <c>main</c>.
    /// </summary>
    public static void AddConflictingBranches(this TestRepository repository)
    {
        repository.Prepare();
        repository.RunGit("checkout", "--quiet", "-b", "topic");
        repository.CommitFile("conflict.txt", "one\ntopic version\nthree\n", "Topic changes conflict.txt");
        repository.CommitFile("t.txt", "t", "Topic adds t.txt");
        repository.RunGit("checkout", "--quiet", "main");
        repository.CommitFile("conflict.txt", "one\nmain version\nthree\n", "Main changes conflict.txt");
        repository.CommitFile("m.txt", "m", "Main adds m.txt");
    }

    /// <summary>Diverged branches that touch different files, so they merge cleanly. Ends on <c>main</c>.</summary>
    public static void AddCleanDivergedBranches(this TestRepository repository)
    {
        repository.Prepare();
        repository.RunGit("checkout", "--quiet", "-b", "topic");
        repository.CommitFile("t.txt", "t", "Topic adds t.txt");
        repository.RunGit("checkout", "--quiet", "main");
        repository.CommitFile("m.txt", "m", "Main adds m.txt");
    }

    /// <summary><c>topic</c> deletes conflict.txt, <c>main</c> modifies it. Ends on <c>main</c>.</summary>
    public static void AddDeleteVersusModifyBranches(this TestRepository repository)
    {
        repository.Prepare();
        repository.RunGit("checkout", "--quiet", "-b", "topic");
        repository.RunGit("rm", "--quiet", "conflict.txt");
        repository.RunGit("commit", "--quiet", "-m", "Topic deletes conflict.txt");
        repository.RunGit("checkout", "--quiet", "main");
        repository.CommitFile("conflict.txt", "one\nmain version\nthree\n", "Main changes conflict.txt");
    }

    public static string Head(this TestRepository repository) => repository.GitOutput("rev-parse", "HEAD").Trim();

    public static string[] Parents(this TestRepository repository, string revision = "HEAD")
        => repository.GitOutput("log", "-1", "--format=%P", revision).Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries);

    public static string WorkFile(this TestRepository repository, string name) => File.ReadAllText(Path.Combine(repository.Path, name));

    private static void Prepare(this TestRepository repository)
    {
        // Drop the uncommitted changes TestRepository starts with.
        repository.RunGit("checkout", "--quiet", "--", "a.txt");
        repository.RunGit("clean", "--quiet", "-fd");
        repository.RunGit("config", "user.name", "Test Author");
        repository.RunGit("config", "user.email", "test@example.com");
        repository.CommitFile("conflict.txt", "one\ntwo\nthree\n", "Base conflict.txt");
    }
}
