namespace GitExtensions.Desktop.History;

/// <summary>
///  Assigns lanes to commits fed one at a time in log order (children before their parents).
///  Only the set of currently open lanes is kept, so appending a batch costs time proportional to the batch.
/// </summary>
public sealed class CommitGraphBuilder
{
    private readonly List<OpenLane> _lanes = [];
    private int _nextColor;

    /// <summary>
    ///  Lanes from this index on are drawn in one shared column, so only one pass-through segment is kept for them.
    ///  A repository with hundreds of open branches would otherwise store hundreds of segments in every row.
    /// </summary>
    public int CollapseFromLane { get; init; } = int.MaxValue;

    /// <summary>The largest number of columns any appended row needed.</summary>
    public int MaxLaneCount { get; private set; }

    public void Reset()
    {
        _lanes.Clear();
        _nextColor = 0;
        MaxLaneCount = 0;
    }

    /// <summary>
    ///  Adds the next commit; returns the row to draw for it.
    /// </summary>
    /// <param name="id">The commit id (any string key, e.g. the sha).</param>
    /// <param name="parents">The parent ids, first parent first.</param>
    public CommitGraphRow Append(string id, IReadOnlyList<string> parents)
    {
        List<GraphSegment> segments = [];
        int highest = -1;
        bool collapsedPassAdded = false;

        // Lanes that were waiting for this commit converge into its node.
        int nodeLane = -1;
        for (int i = 0; i < _lanes.Count; i++)
        {
            if (_lanes[i].Waiting == id)
            {
                nodeLane = nodeLane < 0 ? i : nodeLane;
            }
        }

        int nodeColor;
        if (nodeLane < 0)
        {
            // A branch tip nobody has referred to yet: start a new lane.
            nodeLane = AllocateLane();
            nodeColor = _nextColor++;
            _lanes[nodeLane] = new OpenLane(null, nodeColor);
        }
        else
        {
            nodeColor = _lanes[nodeLane].Color;
        }

        for (int i = 0; i < _lanes.Count; i++)
        {
            OpenLane lane = _lanes[i];
            if (lane.Waiting == id)
            {
                segments.Add(new GraphSegment(GraphSegmentKind.In, i, lane.Color));
                highest = Math.Max(highest, i);
                _lanes[i] = new OpenLane(null, lane.Color);
            }
            else if (lane.Waiting is not null)
            {
                if (i < CollapseFromLane || !collapsedPassAdded)
                {
                    segments.Add(new GraphSegment(GraphSegmentKind.Pass, i, lane.Color));
                    collapsedPassAdded |= i >= CollapseFromLane;
                }

                highest = Math.Max(highest, i);
            }
        }

        highest = Math.Max(highest, nodeLane);

        // Link to the parents: the first parent continues the node's lane (or joins a lane already heading there).
        for (int p = 0; p < parents.Count; p++)
        {
            string parent = parents[p];

            // The first parent always continues the node's own lane, so the main line keeps its column even when
            // another lane already heads to the same commit (both then converge into it later).
            int existing = p == 0 ? -1 : _lanes.FindIndex(l => l.Waiting == parent);
            if (existing >= 0)
            {
                segments.Add(new GraphSegment(GraphSegmentKind.Out, existing, _lanes[existing].Color));
                highest = Math.Max(highest, existing);
                continue;
            }

            int target;
            int color;
            if (p == 0)
            {
                target = nodeLane;
                color = nodeColor;
            }
            else
            {
                target = AllocateLane();
                color = _nextColor++;
            }

            _lanes[target] = new OpenLane(parent, color);
            segments.Add(new GraphSegment(GraphSegmentKind.Out, target, color));
            highest = Math.Max(highest, target);
        }

        // The node's own lane is free again unless a parent took it; drop trailing free lanes.
        while (_lanes.Count > 0 && _lanes[^1].Waiting is null)
        {
            _lanes.RemoveAt(_lanes.Count - 1);
        }

        int laneCount = highest + 1;
        MaxLaneCount = Math.Max(MaxLaneCount, laneCount);
        return new CommitGraphRow(nodeLane, nodeColor, segments, laneCount);
    }

    // A lane is free when nothing waits in it; a lane that merely converged in this row is only reusable from the next row on,
    // which is why free slots are looked up after the row's In segments were recorded (their Waiting was cleared).
    private int AllocateLane()
    {
        int free = _lanes.FindIndex(l => l.Waiting is null);
        if (free >= 0)
        {
            return free;
        }

        _lanes.Add(new OpenLane(null, 0));
        return _lanes.Count - 1;
    }

    private readonly record struct OpenLane(string? Waiting, int Color);
}
