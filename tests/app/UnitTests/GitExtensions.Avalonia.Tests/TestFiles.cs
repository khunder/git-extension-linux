namespace GitExtensionsDesktopTests;

internal static class TestFiles
{
    /// <summary>
    ///  Deletes a folder tree that may hold a git repository. Git writes its object files read-only, which makes a plain
    ///  recursive delete throw on Windows, so the attributes are cleared first.
    /// </summary>
    public static void DeleteTree(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }
}
