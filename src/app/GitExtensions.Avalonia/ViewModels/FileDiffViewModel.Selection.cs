using GitExtensions.Desktop.Diff;

namespace GitExtensions.Desktop.ViewModels;

// Drag and keyboard selection of changed lines (the click selection is in FileDiffViewModel.cs).
public sealed partial class FileDiffViewModel
{
    private DiffLine? _selectionCursor;

    /// <summary>
    ///  Drag selection: selects the changed lines between the line that was pressed and <paramref name="line"/>.
    /// </summary>
    public void ExtendSelectionTo(DiffLine line)
    {
        if (!_canSelectLines)
        {
            return;
        }

        if (_selectionAnchor is not { } anchor || IndexOf(anchor) < 0)
        {
            if (line.IsChange)
            {
                SelectLine(line, ctrl: false, shift: false);
            }

            return;
        }

        SelectRange(IndexOf(anchor), IndexOf(line));
        _selectionCursor = line;
        UpdateSelectionState();
    }

    /// <summary>
    ///  Keyboard selection: moves to the previous/next changed line (<paramref name="delta"/> is -1 or 1), extending the selection
    ///  from the anchor when <paramref name="extend"/> (Shift) is set. Returns the line that now has the cursor.
    /// </summary>
    public DiffLine? MoveSelection(int delta, bool extend)
    {
        if (!_canSelectLines || Lines.Count == 0)
        {
            return null;
        }

        int start = _selectionCursor is { } cursor && IndexOf(cursor) is >= 0 and int known
            ? known
            : delta > 0 ? -1 : Lines.Count;
        int next = start + delta;
        while (next >= 0 && next < Lines.Count && !Lines[next].IsChange)
        {
            next += delta;
        }

        if (next < 0 || next >= Lines.Count)
        {
            return _selectionCursor;
        }

        DiffLine line = Lines[next];
        if (extend && _selectionAnchor is { } anchor && IndexOf(anchor) >= 0)
        {
            SelectRange(IndexOf(anchor), next);
        }
        else
        {
            ClearSelectionCore();
            line.IsSelected = true;
            _selectionAnchor = line;
        }

        _selectionCursor = line;
        UpdateSelectionState();
        return line;
    }

    /// <summary>
    ///  Selects all changed lines (Ctrl+A).
    /// </summary>
    public void SelectAllLines()
    {
        if (!_canSelectLines)
        {
            return;
        }

        foreach (DiffLine line in Lines.Where(l => l.IsChange))
        {
            line.IsSelected = true;
        }

        _selectionAnchor ??= Lines.FirstOrDefault(l => l.IsChange);
        UpdateSelectionState();
    }

    private void SelectRange(int from, int to)
    {
        ClearSelectionCore();
        for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++)
        {
            if (i >= 0 && i < Lines.Count && Lines[i].IsChange)
            {
                Lines[i].IsSelected = true;
            }
        }
    }
}
