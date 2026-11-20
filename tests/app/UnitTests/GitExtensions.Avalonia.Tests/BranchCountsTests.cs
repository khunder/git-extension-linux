using GitExtensions.Desktop.ViewModels;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class BranchCountsTests
{
    [Test]
    public void Counts_are_written_like_the_original()
    {
        new UpstreamTrack("origin/main", 0, 0, Gone: false).CountsText().Should().Be("(0↑↓)");
        new UpstreamTrack("origin/main", 0, 0, Gone: false).CountsText(remoteSide: true).Should().Be("(0↓↑)");
        new UpstreamTrack("origin/main", 2, 0, Gone: false).CountsText().Should().Be("(2↑)");
        new UpstreamTrack("origin/main", 0, 3, Gone: false).CountsText().Should().Be("(3↓)");
        new UpstreamTrack("origin/main", 2, 1, Gone: false).CountsText().Should().Be("(2↑ 1↓)");
        new UpstreamTrack("origin/main", 0, 0, Gone: true).CountsText().Should().Be("(gone)");
    }

    [Test]
    public void A_remote_branch_shows_the_counts_of_its_local_branch_from_the_other_side()
    {
        List<RefEntry> entries =
        [
            new(RefNodeKind.LocalBranch, "main", Track: new UpstreamTrack("origin/main", Ahead: 2, Behind: 1, Gone: false)),
            new(RefNodeKind.RemoteBranch, "origin/main"),
            new(RefNodeKind.RemoteBranch, "origin/other"),
        ];

        IReadOnlyList<RefNodeViewModel> roots = RefsTreeBuilder.Build(entries, currentBranch: "main", filter: null);

        RefNodeViewModel local = roots.Single(r => r.Name == "Branches").Children.Single();
        local.CountsText.Should().Be("(2↑ 1↓)");

        RefNodeViewModel origin = roots.Single(r => r.Name == "Remotes").Children.Single();
        origin.Children.Single(c => c.Name == "main").CountsText.Should().Be("(1↑ 2↓)");
        origin.Children.Single(c => c.Name == "other").HasCounts.Should().BeFalse("a remote branch nobody tracks has no counts");
    }
}
