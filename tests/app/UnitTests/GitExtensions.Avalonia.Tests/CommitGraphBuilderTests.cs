using GitExtensions.Desktop.History;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class CommitGraphBuilderTests
{
    private static List<CommitGraphRow> Build(params (string Id, string[] Parents)[] commits)
    {
        CommitGraphBuilder builder = new();
        return commits.Select(c => builder.Append(c.Id, c.Parents)).ToList();
    }

    [Test]
    public void Linear_history_stays_in_lane_zero()
    {
        List<CommitGraphRow> rows = Build(("c", ["b"]), ("b", ["a"]), ("a", []));

        rows.Should().OnlyContain(r => r.Lane == 0 && r.LaneCount == 1);
        rows[0].Segments.Should().Equal(new GraphSegment(GraphSegmentKind.Out, 0, rows[0].Color));
        rows[1].Segments.Select(s => s.Kind).Should().Equal(GraphSegmentKind.In, GraphSegmentKind.Out);
        rows[2].Segments.Select(s => s.Kind).Should().Equal(GraphSegmentKind.In);
        rows.Select(r => r.Color).Distinct().Should().HaveCount(1);
    }

    [Test]
    public void Fork_and_merge_uses_a_second_lane_and_converges()
    {
        // m merges f (second parent) into main; both descend from base.
        List<CommitGraphRow> rows = Build(
            ("m", ["x", "f"]),
            ("f", ["base"]),
            ("x", ["base"]),
            ("base", []));

        rows[0].Lane.Should().Be(0);
        rows[0].Segments.Where(s => s.Kind == GraphSegmentKind.Out).Select(s => s.Lane).Should().Equal(0, 1);
        rows[1].Lane.Should().Be(1, "f continues the lane opened by the merge");
        rows[1].Segments.Should().Contain(s => s.Kind == GraphSegmentKind.Pass && s.Lane == 0);
        rows[2].Lane.Should().Be(0);
        rows[2].Segments.Should().Contain(s => s.Kind == GraphSegmentKind.Pass && s.Lane == 1);

        // "base" is awaited by both lanes: it sits in lane 0 and lane 1 converges into it.
        rows[3].Lane.Should().Be(0);
        rows[3].Segments.Where(s => s.Kind == GraphSegmentKind.In).Select(s => s.Lane).Should().BeEquivalentTo([0, 1]);
        rows[3].Segments.Should().NotContain(s => s.Kind == GraphSegmentKind.Out);
        rows.Max(r => r.LaneCount).Should().Be(2);
        rows[1].Color.Should().NotBe(rows[0].Color);
    }

    [Test]
    public void Two_roots_use_separate_lanes_and_free_them()
    {
        List<CommitGraphRow> rows = Build(("b2", ["a2"]), ("b1", ["a1"]), ("a2", []), ("a1", []));

        rows[0].Lane.Should().Be(0);
        rows[1].Lane.Should().Be(1);
        rows[2].Lane.Should().Be(0);
        rows[3].Lane.Should().Be(1);
        rows[3].LaneCount.Should().Be(2);
    }

    [Test]
    public void Octopus_merge_opens_a_lane_per_extra_parent()
    {
        List<CommitGraphRow> rows = Build(("o", ["p1", "p2", "p3"]));

        rows[0].Segments.Where(s => s.Kind == GraphSegmentKind.Out).Select(s => s.Lane).Should().Equal(0, 1, 2);
        rows[0].Segments.Select(s => s.Color).Distinct().Should().HaveCount(3);
        rows[0].LaneCount.Should().Be(3);
    }

    [Test]
    public void Batch_incremental_equals_one_shot()
    {
        (string, string[])[] commits =
        [
            ("m2", ["m1", "d"]),
            ("d", ["c"]),
            ("m1", ["c", "e"]),
            ("e", ["b"]),
            ("c", ["b"]),
            ("b", ["a"]),
            ("a", []),
        ];

        List<CommitGraphRow> oneShot = Build(commits);

        // Fed in uneven batches through the same builder instance, as the streaming history does.
        CommitGraphBuilder builder = new();
        List<CommitGraphRow> incremental = [];
        foreach (Range batch in new[] { 0..2, 2..5, 5..7 })
        {
            foreach ((string id, string[] parents) in commits[batch])
            {
                incremental.Add(builder.Append(id, parents));
            }
        }

        incremental.Should().BeEquivalentTo(oneShot);
    }

    [Test]
    public void Reset_clears_the_open_lanes()
    {
        CommitGraphBuilder builder = new();
        builder.Append("a", ["x", "y"]);
        builder.MaxLaneCount.Should().Be(2);

        builder.Reset();
        CommitGraphRow row = builder.Append("b", []);

        row.Lane.Should().Be(0);
        row.Color.Should().Be(0);
        builder.MaxLaneCount.Should().Be(1);
    }
}
