using System.Text;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>
///  Turns raw process output chunks into display lines. A carriage return rewinds the current line, so git's
///  progress meter ("Receiving objects: 10%\r... 20%\r...") collapses into one line that updates in place.
///  Not thread-safe; callers synchronize.
/// </summary>
public sealed class OutputAccumulator
{
    private readonly List<string> _lines = [];
    private readonly StringBuilder _current = new();
    private readonly int _maxLines;
    private bool _rewind;

    public OutputAccumulator(int maxLines = 5000)
        => _maxLines = maxLines;

    /// <summary>
    ///  Gets the finished lines followed by the line still being written, if any.
    /// </summary>
    public IReadOnlyList<string> Lines
        => _current.Length == 0 ? _lines.ToArray() : [.. _lines, _current.ToString()];

    public string Text => string.Join('\n', Lines);

    public void Append(string chunk)
    {
        foreach (char c in chunk)
        {
            switch (c)
            {
                case '\r':
                    _rewind = true;
                    break;
                case '\n':
                    if (_current.Length > 0)
                    {
                        AddLine(_current.ToString());
                    }

                    _current.Clear();
                    _rewind = false;
                    break;
                default:
                    if (_rewind)
                    {
                        _current.Clear();
                        _rewind = false;
                    }

                    _current.Append(c);
                    break;
            }
        }
    }

    /// <summary>
    ///  Appends a complete line (e.g. the command line or a summary), finishing any partial line first.
    /// </summary>
    public void AppendLine(string line)
    {
        Append("\n");
        Append(line);
        Append("\n");
    }

    public void Clear()
    {
        _lines.Clear();
        _current.Clear();
        _rewind = false;
    }

    private void AddLine(string line)
    {
        _lines.Add(line);
        if (_lines.Count > _maxLines)
        {
            _lines.RemoveRange(0, _lines.Count - _maxLines);
        }
    }
}
