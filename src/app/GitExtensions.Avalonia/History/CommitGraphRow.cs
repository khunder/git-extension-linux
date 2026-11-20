namespace GitExtensions.Desktop.History;

public enum GraphSegmentKind
{
    /// <summary>A lane that runs through the row untouched, from the top edge to the bottom edge of the same lane.</summary>
    Pass,

    /// <summary>A lane from the top edge converging into this row's node.</summary>
    In,

    /// <summary>A line from this row's node down to a lane at the bottom edge (a parent link).</summary>
    Out,
}

/// <summary>
///  One line to draw in a row. For <see cref="GraphSegmentKind.Pass"/> <see cref="Lane"/> is both ends;
///  for <see cref="GraphSegmentKind.In"/> it is the lane at the top; for <see cref="GraphSegmentKind.Out"/> the lane at the bottom.
///  The other end of In/Out is the node of the row.
/// </summary>
public readonly record struct GraphSegment(GraphSegmentKind Kind, int Lane, int Color, bool Dimmed = false);

/// <summary>
///  Everything needed to draw the graph cell of one commit; immutable, so a virtualized row can render it on its own.
/// </summary>
public sealed class CommitGraphRow
{
    public CommitGraphRow(int lane, int color, IReadOnlyList<GraphSegment> segments, int laneCount)
    {
        Lane = lane;
        Color = color;
        Segments = segments;
        LaneCount = laneCount;
    }

    /// <summary>The column of the commit node.</summary>
    public int Lane { get; }

    /// <summary>Palette index of the node.</summary>
    public int Color { get; }

    public IReadOnlyList<GraphSegment> Segments { get; }

    /// <summary>Number of columns this row touches (highest lane used + 1).</summary>
    public int LaneCount { get; }
}
