using System.Text.RegularExpressions;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Desktop.ViewModels;

/// <summary>How a commit is marked during a bisect session.</summary>
public enum BisectMark
{
    None,
    Good,
    Bad,
    Skip,
}

/// <summary>What <c>git bisect</c> knows: the bad commit, the good ones and the skipped ones (the refs below <c>refs/bisect</c>).</summary>
public sealed record BisectState(bool IsActive, ObjectId? Bad, IReadOnlyList<ObjectId> Good, IReadOnlyList<ObjectId> Skipped)
{
    public static BisectState None { get; } = new(false, null, [], []);

    /// <summary>Every marked commit with its mark (for the revision grid).</summary>
    public IReadOnlyDictionary<ObjectId, BisectMark> Marks
    {
        get
        {
            Dictionary<ObjectId, BisectMark> marks = [];
            foreach (ObjectId id in Skipped)
            {
                marks[id] = BisectMark.Skip;
            }

            foreach (ObjectId id in Good)
            {
                marks[id] = BisectMark.Good;
            }

            if (Bad is { } bad)
            {
                marks[bad] = BisectMark.Bad;
            }

            return marks;
        }
    }

    /// <summary>One line: "bad abc1234, 2 good, 1 skipped".</summary>
    public string Summary
    {
        get
        {
            if (!IsActive)
            {
                return "Not bisecting.";
            }

            string bad = Bad is { } b ? $"bad {b.ToShortString()}" : "no bad commit yet";
            string good = Good.Count == 0 ? "no good commit yet" : $"{Good.Count} good";
            return Skipped.Count == 0 ? $"Bisecting: {bad}, {good}." : $"Bisecting: {bad}, {good}, {Skipped.Count} skipped.";
        }
    }
}

public static partial class BisectStateParser
{
    [GeneratedRegex(@"^(?<id>[0-9a-f]{40,64}) refs/bisect/(?<kind>bad|good|skip)(-[0-9a-f]+)?$")]
    private static partial Regex RefRegex { get; }

    [GeneratedRegex(@"(?<id>[0-9a-f]{40,64}) is the first (?:'[^']*'|\w+) commit")]
    private static partial Regex FirstBadRegex { get; }

    /// <summary>Reads the output of <c>git for-each-ref --format="%(objectname) %(refname)" refs/bisect</c>.</summary>
    public static BisectState Parse(bool isActive, string forEachRefOutput)
    {
        ObjectId? bad = null;
        List<ObjectId> good = [];
        List<ObjectId> skipped = [];
        foreach (string line in forEachRefOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Match match = RefRegex.Match(line);
            if (!match.Success || !ObjectId.TryParse(match.Groups["id"].Value, out ObjectId id))
            {
                continue;
            }

            switch (match.Groups["kind"].Value)
            {
                case "bad":
                    bad = id;
                    break;
                case "good":
                    good.Add(id);
                    break;
                default:
                    skipped.Add(id);
                    break;
            }
        }

        return new BisectState(isActive, bad, good, skipped);
    }

    /// <summary>The commit git names in "&lt;sha&gt; is the first bad commit", if the output has that line.</summary>
    public static ObjectId? ParseFirstBad(string output)
        => FirstBadRegex.Match(output) is { Success: true } match && ObjectId.TryParse(match.Groups["id"].Value, out ObjectId id) ? id : null;
}
