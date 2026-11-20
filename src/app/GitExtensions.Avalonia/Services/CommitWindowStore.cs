using System.Text.Json;

namespace GitExtensions.Desktop.Services;

/// <summary>
///  Small preferences of the Commit window: the most recent commit messages and whether the window closes after a
///  successful commit. Persisted as JSON next to the other UI settings; the path is overridable for tests.
/// </summary>
public sealed class CommitWindowStore
{
    public const int MaxRecentMessages = 10;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _lock = new();
    private List<string> _recent = [];

    public CommitWindowStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitExtensions", "avalonia-commit.json");
        Load();
    }

    /// <summary>
    ///  The store used by the UI. Replaceable so tests can point it at a temporary file.
    /// </summary>
    public static CommitWindowStore Current { get; set; } = new();

    public string FilePath { get; }

    /// <summary>
    ///  Close the Commit window after a successful commit (default <see langword="true"/>).
    /// </summary>
    public bool CloseAfterCommit { get; private set; } = true;

    /// <summary>
    ///  Recent messages, newest first.
    /// </summary>
    public IReadOnlyList<string> RecentMessages
    {
        get
        {
            lock (_lock)
            {
                return [.. _recent];
            }
        }
    }

    /// <summary>
    ///  Show the changed files as a folder tree instead of a flat list (default <see langword="true"/>).
    /// </summary>
    public bool ShowTree { get; private set; } = true;

    public void SetCloseAfterCommit(bool value)
    {
        CloseAfterCommit = value;
        Save();
    }

    public void SetShowTree(bool value)
    {
        ShowTree = value;
        Save();
    }

    /// <summary>
    ///  Remembers <paramref name="message"/> as the most recent one (moving an identical earlier entry to the front).
    /// </summary>
    public void AddRecent(string message)
    {
        string trimmed = message.TrimEnd();
        if (trimmed.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            _recent.RemoveAll(m => m == trimmed);
            _recent.Insert(0, trimmed);
            if (_recent.Count > MaxRecentMessages)
            {
                _recent.RemoveRange(MaxRecentMessages, _recent.Count - MaxRecentMessages);
            }
        }

        Save();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Document>(File.ReadAllText(FilePath), JsonOptions) is { } document)
            {
                _recent = [.. (document.Recent ?? []).Where(m => !string.IsNullOrWhiteSpace(m)).Take(MaxRecentMessages)];
                CloseAfterCommit = document.CloseAfterCommit;
                ShowTree = document.ShowTree;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
        }
    }

    private void Save()
    {
        try
        {
            Document document;
            lock (_lock)
            {
                document = new Document { Recent = [.. _recent], CloseAfterCommit = CloseAfterCommit, ShowTree = ShowTree };
            }

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(document, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a remembered message is not worth an error.
        }
    }

    private sealed class Document
    {
        public List<string>? Recent { get; set; }

        public bool CloseAfterCommit { get; set; } = true;

        public bool ShowTree { get; set; } = true;
    }
}
