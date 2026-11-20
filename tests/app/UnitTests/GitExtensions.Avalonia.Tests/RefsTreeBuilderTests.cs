using GitExtensions.Desktop.ViewModels;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class RefsTreeBuilderTests
{
    private static readonly RefEntry[] Entries =
    [
        new(RefNodeKind.LocalBranch, "main"),
        new(RefNodeKind.LocalBranch, "feature/x"),
        new(RefNodeKind.LocalBranch, "feature/deep/z"),
        new(RefNodeKind.LocalBranch, "develop"),
        new(RefNodeKind.RemoteBranch, "origin/main"),
        new(RefNodeKind.RemoteBranch, "origin/feature/y"),
        new(RefNodeKind.RemoteBranch, "upstream/main"),
        new(RefNodeKind.Tag, "v1.0"),
        new(RefNodeKind.Tag, "release/2.0"),
    ];

    private static string Dump(IEnumerable<RefNodeViewModel> nodes, int depth = 0)
        => string.Concat(nodes.Select(n => new string(' ', depth * 2) + n.Name + (n.IsCurrent ? "*" : string.Empty) + "\n" + Dump(n.Children, depth + 1)));

    [Test]
    public void Build_groups_by_kind_remote_and_slash_folders()
    {
        IReadOnlyList<RefNodeViewModel> roots = RefsTreeBuilder.Build(Entries, "main", filter: null);

        Dump(roots).Should().Be(
            """
            Branches
              feature
                deep
                  z
                x
              develop
              main*
            Remotes
              origin
                feature
                  y
                main
              upstream
                main
            Tags
              release
                2.0
              v1.0
            Stashes

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public void Build_keeps_the_full_name_for_leaves()
    {
        IReadOnlyList<RefNodeViewModel> roots = RefsTreeBuilder.Build(Entries, "main", null);

        RefNodeViewModel leaf = roots[0].Children[0].Children[1];
        leaf.Name.Should().Be("x");
        leaf.FullName.Should().Be("feature/x");
        roots[1].Children[0].Children[1].FullName.Should().Be("origin/main");
    }

    [Test]
    public void Build_filters_case_insensitively_and_drops_empty_groups()
    {
        IReadOnlyList<RefNodeViewModel> roots = RefsTreeBuilder.Build(Entries, "main", "FEATURE");

        Dump(roots).Should().Be(
            """
            Branches
              feature
                deep
                  z
                x
            Remotes
              origin
                feature
                  y

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public void Build_with_a_filter_matching_nothing_returns_no_groups()
        => RefsTreeBuilder.Build(Entries, "main", "nope").Should().BeEmpty();

    [Test]
    public void Build_without_entries_still_shows_the_main_groups_and_stashes()
        => RefsTreeBuilder.Build([], null, null).Select(n => n.Name).Should().Equal("Branches", "Remotes", "Tags", "Stashes");

    [Test]
    public void Only_the_current_local_branch_is_marked()
    {
        IReadOnlyList<RefNodeViewModel> roots = RefsTreeBuilder.Build(Entries, "main", null);

        // The remote 'origin/main' must not be marked just because a local 'main' is checked out.
        Flatten(roots).Where(n => n.IsCurrent).Select(n => n.FullName).Should().Equal("main");
        Flatten(roots).Single(n => n.IsCurrent).Marker.Should().NotBeEmpty();
    }

    private static IEnumerable<RefNodeViewModel> Flatten(IEnumerable<RefNodeViewModel> nodes)
        => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));
}
