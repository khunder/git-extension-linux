using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  The commits the user selected, newest last, with a cursor: "Navigate backward/forward" move the cursor. Selecting a new commit
///  after going back drops the commits that were "forward".
/// </summary>
public sealed class NavigationHistory
{
    public const int Capacity = 100;

    private readonly List<ObjectId> _entries = [];
    private int _index = -1;

    public int Count => _entries.Count;

    public bool CanGoBack => _index > 0;

    public bool CanGoForward => _index >= 0 && _index < _entries.Count - 1;

    public ObjectId? Current => _index >= 0 ? _entries[_index] : null;

    public void Clear()
    {
        _entries.Clear();
        _index = -1;
    }

    /// <summary>Remembers a selection (the same commit twice in a row is one entry).</summary>
    public void Record(ObjectId id)
    {
        if (_index >= 0 && _entries[_index] == id)
        {
            return;
        }

        if (_index < _entries.Count - 1)
        {
            _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        }

        _entries.Add(id);
        if (_entries.Count > Capacity)
        {
            _entries.RemoveAt(0);
        }

        _index = _entries.Count - 1;
    }

    /// <summary>Moves one step back and returns that commit, or <see langword="null"/> at the start.</summary>
    public ObjectId? Back() => CanGoBack ? _entries[--_index] : null;

    /// <summary>Moves one step forward and returns that commit, or <see langword="null"/> at the end.</summary>
    public ObjectId? Forward() => CanGoForward ? _entries[++_index] : null;
}
