namespace GitExtensionsDesktopTests;

/// <summary>
///  A throwaway repository with a merge, a tag, a remote, a rename, a deletion, a binary file and a big file.
///  Commits (oldest first): Root (a.txt, tag v1.0), Add b and c, Feature work (on feature), Main work, Merge feature, Rework.
/// </summary>
internal sealed class RevisionTabsRepository : IDisposable
{
    public RevisionTabsRepository()
    {
        Repository = new TestRepository();

        // The base repository has two commits and uncommitted changes: start over on top of it with a clean tree.
        Repository.RunGit("checkout", "--", "a.txt");
        File.Delete(System.IO.Path.Combine(Path, "c.txt"));
        Repository.RunGit("rm", "--quiet", "b.txt");
        Repository.RunGit("-c", "user.name=Test Author", "-c", "user.email=test@example.com", "commit", "--quiet", "-m", "Remove b");
        Write("c.txt", "ccc\nccc\nccc\nccc\nccc\n");
        Write("src/inner.txt", "inner\n");
        Write("src/deep/x.txt", "x\n");
        Commit("Add c and src");
        Repository.RunGit("checkout", "--quiet", "-b", "feature");
        Write("f.txt", "feature\n");
        Commit("Feature work");
        Repository.RunGit("checkout", "--quiet", "main");
        Write("g.txt", "g\n");
        Commit("Main work");
        Repository.RunGit("-c", "user.name=Test Author", "-c", "user.email=test@example.com", "merge", "--no-ff", "--quiet", "-m", "Merge feature", "feature");
        Repository.RunGit("remote", "add", "origin", "https://github.com/octo/repo.git");

        Write("a.txt", "rewritten\n");
        Repository.RunGit("rm", "--quiet", "g.txt");
        Repository.RunGit("mv", "c.txt", "cc.txt");
        File.WriteAllBytes(System.IO.Path.Combine(Path, "bin.dat"), [1, 2, 0, 3, 4, 0, 255]);
        File.WriteAllText(System.IO.Path.Combine(Path, "large.txt"), new string('x', 3 * 1024 * 1024));
        Repository.RunGit("add", "-A");
        Repository.RunGit("-c", "user.name=Test Author", "-c", "user.email=test@example.com", "commit", "--quiet", "-m", "Rework files", "-m", "Fixes #42, see https://example.com/spec.");
    }

    public TestRepository Repository { get; }

    public string Path => Repository.Path;

    public string Sha(string revision) => Repository.GitOutput("rev-parse", revision).Trim();

    public void Dispose() => Repository.Dispose();

    private void Write(string file, string content)
    {
        string full = System.IO.Path.Combine(Path, file);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void Commit(string message)
    {
        Repository.RunGit("add", "-A");
        Repository.RunGit("-c", "user.name=Test Author", "-c", "user.email=test@example.com", "commit", "--quiet", "-m", message);
    }
}
