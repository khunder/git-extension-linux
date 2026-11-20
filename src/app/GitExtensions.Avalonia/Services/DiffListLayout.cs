namespace GitExtensions.Desktop.Services;

/// <summary>How the Diff tab lists the changed files.</summary>
public enum DiffGrouping
{
    /// <summary>A flat list of paths.</summary>
    None,

    /// <summary>A folder tree (the original's default).</summary>
    Path,

    /// <summary>One node per file extension, each with its folder tree (the original's "group by extension").</summary>
    Extension,
}

/// <summary>
///  The remembered layout of the Diff tab's file list (<c>avalonia-diff-layout.txt</c> next to the other UI settings).
///  The file is replaceable so tests do not touch the user's settings.
/// </summary>
public static class DiffListLayout
{
    private static DiffGrouping? _grouping;

    public static string FilePath { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitExtensions", "avalonia-diff-layout.txt");

    public static event EventHandler? Changed;

    public static DiffGrouping Grouping
    {
        get => _grouping ??= Load();
        set
        {
            if (Grouping == value)
            {
                return;
            }

            _grouping = value;
            Save(value);
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Points the layout at another file (tests) and forgets the loaded value.</summary>
    public static void UseFile(string filePath)
    {
        FilePath = filePath;
        _grouping = null;
    }

    private static DiffGrouping Load()
    {
        try
        {
            return File.Exists(FilePath) && Enum.TryParse(File.ReadAllText(FilePath).Trim(), out DiffGrouping value) ? value : DiffGrouping.Path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DiffGrouping.Path;
        }
    }

    private static void Save(DiffGrouping value)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, value.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a preference.
        }
    }
}
