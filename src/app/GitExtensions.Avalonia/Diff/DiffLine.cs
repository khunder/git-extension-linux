namespace GitExtensions.Desktop.Diff;

public enum DiffLineKind
{
    /// <summary>File header lines (<c>diff --git</c>, <c>index</c>, <c>---</c>, <c>+++</c>, ...).</summary>
    Header,

    /// <summary>A hunk header (<c>@@ -1,3 +1,4 @@</c>).</summary>
    Hunk,

    Context,
    Added,
    Removed,

    /// <summary>Informational text such as <c>\ No newline at end of file</c>.</summary>
    Info
}

/// <summary>
///  One line of a unified diff. Line numbers are <see langword="null"/> where they do not apply.
/// </summary>
public sealed record DiffLine(DiffLineKind Kind, string Text, int? OldLineNumber, int? NewLineNumber)
{
    public string OldNumber => OldLineNumber?.ToString() ?? string.Empty;

    public string NewNumber => NewLineNumber?.ToString() ?? string.Empty;
}
