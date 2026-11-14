using GitExtensions.Desktop.Revision;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class RevisionParserTests
{
    [Test]
    public void DescribeParser_reads_the_long_format()
    {
        DescribeParser.Description? result = DescribeParser.Parse("v3.00.00-rc2-186-gabcdef0\n");

        result.Should().NotBeNull();
        result!.Value.Tag.Should().Be("v3.00.00-rc2");
        result.Value.Commits.Should().Be(186);
        result.Value.ToString().Should().Be("v3.00.00-rc2 + 186 commits");
        DescribeParser.Parse("v1.0-1-g1234567")!.Value.ToString().Should().Be("v1.0 + 1 commit");
        DescribeParser.Parse("v1.0-0-g1234567")!.Value.Commits.Should().Be(0);
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("fatal: No names found")]
    [TestCase("v1.0")]
    public void DescribeParser_rejects_other_output(string? output)
        => DescribeParser.Parse(output).Should().BeNull();

    [Test]
    public void MessageLinker_finds_urls_and_issue_references_in_order()
    {
        IReadOnlyList<MessageLink> links = MessageLinker.Find(
            "Fix #12 and see (https://example.com/a?b=1#frag). Also http://x.org/y, again #12 and #7.",
            "https://github.com/o/r");

        links.Select(l => (l.Text, l.Url)).Should().Equal(
            ("Issue 12", "https://github.com/o/r/issues/12"),
            ("https://example.com/a?b=1#frag", "https://example.com/a?b=1#frag"),
            ("http://x.org/y", "http://x.org/y"),
            ("Issue 7", "https://github.com/o/r/issues/7"));
    }

    [Test]
    public void MessageLinker_ignores_issue_references_without_a_known_repository()
    {
        MessageLinker.Find("Fix #12 https://a.org/x", null).Select(l => l.Text).Should().Equal("https://a.org/x");
        MessageLinker.Find(null).Should().BeEmpty();
        MessageLinker.Find("no links here", "https://github.com/o/r").Should().BeEmpty();
    }

    [Test]
    public void MessageLinker_does_not_treat_an_anchor_or_a_word_hash_as_an_issue()
        => MessageLinker.Find("see https://a.org/p#12 and C#5 and a#6", "https://github.com/o/r").Select(l => l.Text).Should().Equal("https://a.org/p#12");

    [TestCase("https://github.com/o/r.git", "https://github.com/o/r")]
    [TestCase("git@github.com:o/r.git", "https://github.com/o/r")]
    [TestCase("ssh://git@gitlab.com/g/s/r.git", "https://gitlab.com/g/s/r")]
    [TestCase("https://host/o/r", "https://host/o/r")]
    [TestCase("/local/path/repo", null)]
    [TestCase("", null)]
    public void MessageLinker_derives_the_web_address(string remote, string? expected)
        => MessageLinker.GetRepositoryWebUrl(remote).Should().Be(expected);

    [Test]
    public void MessageLinker_names_the_hosting_service()
    {
        MessageLinker.GetCommitLink("https://github.com/o/r", "abc")!.Should().Be(new MessageLink("View on GitHub", "https://github.com/o/r/commit/abc"));
        MessageLinker.GetCommitLink(null, "abc").Should().BeNull();
    }

    [Test]
    public void RefListParser_keeps_branches_and_drops_pointers()
    {
        string output = "refs/heads/main\nrefs/heads/feature/x\nrefs/remotes/origin/main\nrefs/remotes/origin/HEAD\n(HEAD detached at abc)\n";

        RefListParser.ParseBranches(output).Should().Equal("main", "feature/x", "origin/main");
        RefListParser.ParseBranches("").Should().BeEmpty();
        RefListParser.ParseTags("v1\nv2\n\n").Should().Equal("v1", "v2");
    }

    [Test]
    public void RefListParser_caps_long_lists()
    {
        List<string> names = [.. Enumerable.Range(1, 80).Select(i => $"t{i}")];

        string text = RefListParser.Join(names);

        text.Should().Contain("t50").And.NotContain("t51").And.EndWith("… and 30 more");
    }

    [Test]
    public void RelativeTime_formats_like_the_original()
    {
        DateTime now = new(2019, 2, 18, 12, 0, 0);

        RelativeTime.Format(now, now.AddDays(-1)).Should().Be("1 day ago");
        RelativeTime.Format(now, now.AddHours(-23)).Should().Be("23 hours ago");
        RelativeTime.Format(now, now.AddMinutes(-5)).Should().Be("5 minutes ago");
        RelativeTime.Format(now, now.AddDays(-20)).Should().Be("3 weeks ago");
        RelativeTime.Format(now, now.AddDays(-400)).Should().Be("13 months ago");
        RelativeTime.Format(now, now.AddDays(-2000)).Should().Be("5 years ago");
        RelativeTime.FormatWithDate(now, now.AddDays(-1)).Should().StartWith("1 day ago (").And.EndWith(")");
    }

    [Test]
    public void CommitAvatar_derives_initials_and_a_stable_colour()
    {
        CommitAvatar.GetInitials("Philippe Miossec", "p@x.com").Should().Be("PM");
        CommitAvatar.GetInitials("Donatas", "d@x.com").Should().Be("D");
        CommitAvatar.GetInitials("", "jane.doe@x.com").Should().Be("JD");
        CommitAvatar.GetInitials("", "").Should().Be("?");
        ((Avalonia.Media.SolidColorBrush)CommitAvatar.GetBrush("a@b.c")).Color.Should().Be(((Avalonia.Media.SolidColorBrush)CommitAvatar.GetBrush("A@B.C")).Color);
    }

    [Test]
    public void TreeEntry_sorts_folders_first_and_builds_paths()
    {
        TreeEntry root = TreeEntry.Build(["b.txt", "src/z.cs", "src/deep/x.txt", "A.txt", "docs/r.md"]);

        root.Children.Select(c => c.Name).Should().Equal("docs", "src", "A.txt", "b.txt");
        TreeEntry src = root.Children.Single(c => c.Name == "src");
        src.Children.Select(c => (c.Name, c.Path, c.IsFolder)).Should().Equal(("deep", "src/deep", true), ("z.cs", "src/z.cs", false));
    }

    [Test]
    public void RevisionGit_splits_file_content_and_detects_binary_files()
    {
        RevisionGit.ToLines("a\r\nb\n").Lines.Should().Equal("a", "b");
        RevisionGit.ToLines("a\nb\n\n").Lines.Should().Equal("a", "b", string.Empty);
        RevisionGit.ToLines("a\0b").Message.Should().Be("Binary file");
        RevisionGit.ToLines(string.Empty).Message.Should().Contain("empty");
        FileContentResult big = RevisionGit.ToLines(string.Join('\n', Enumerable.Range(0, RevisionGit.MaxFileLines + 5)));
        big.Lines.Should().HaveCount(RevisionGit.MaxFileLines);
        big.Message.Should().Contain("first");
    }
}
