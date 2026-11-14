using System.Text;

namespace GitExtensionsDesktopTests;

/// <summary>
///  A temp repository (built on <see cref="TestRepository"/>) whose committed file <c>m.txt</c> has 30 lines and whose
///  working tree modifies lines 3, 15 and 27, i.e. three separate hunks.
/// </summary>
internal sealed class MultiHunkRepository : IDisposable
{
    public MultiHunkRepository()
    {
        Repository = new TestRepository();

        // Start from a clean working tree.
        Repository.RunGit("checkout", "--quiet", "a.txt");
        File.Delete(FullPath("c.txt"));

        Write("m.txt", string.Join('\n', Enumerable.Range(1, 30).Select(i => "line " + i)) + "\n");
        CommitAll("Add m.txt");

        string[] lines = [.. Enumerable.Range(1, 30).Select(i => "line " + i)];
        lines[2] = "changed 3";
        lines[14] = "changed 15";
        lines[26] = "changed 27";
        Write("m.txt", string.Join('\n', lines) + "\n");
    }

    public TestRepository Repository { get; }

    public string Path => Repository.Path;

    public string FullPath(string name) => System.IO.Path.Combine(Repository.Path, name);

    public void Write(string name, string content) => File.WriteAllText(FullPath(name), content);

    public void WriteBytes(string name, byte[] content) => File.WriteAllBytes(FullPath(name), content);

    public string Read(string name) => File.ReadAllText(FullPath(name), Encoding.Latin1);

    public string Git(params string[] arguments) => Repository.GitOutput(arguments);

    public void CommitAll(string message)
    {
        Repository.RunGit("add", "-A");
        Repository.RunGit("-c", "user.name=Test Author", "-c", "user.email=test@example.com", "commit", "--quiet", "-m", message);
    }

    public void Dispose() => Repository.Dispose();
}
