using GitExtensions.Desktop.ViewModels;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class ToolsParserTests
{
    [Test]
    public void ReflogParser_reads_fields_and_skips_garbage()
    {
        string output =
            "1111111111111111111111111111111111111111\u001fHEAD@{0}\u001freset: moving to HEAD~1\u001f2024-05-01T10:20:30+02:00\n" +
            "\n" +
            "garbage line\n" +
            "2222222222222222222222222222222222222222\u001fHEAD@{1}\u001fcommit: Add: colon\u001fnot a date\r\n";

        IReadOnlyList<ReflogEntry> entries = ReflogParser.Parse(output);

        entries.Should().HaveCount(2);
        entries[0].Hash.Should().Be("1111111111111111111111111111111111111111");
        entries[0].ShortHash.Should().Be("11111111");
        entries[0].Selector.Should().Be("HEAD@{0}");
        entries[0].Message.Should().Be("reset: moving to HEAD~1");
        entries[0].Date.Should().Be(new DateTimeOffset(2024, 5, 1, 10, 20, 30, TimeSpan.FromHours(2)));
        entries[1].Message.Should().Be("commit: Add: colon");
        entries[1].Date.Should().BeNull();
    }

    [TestCase("-1111111111111111111111111111111111111111 sub/uninit", SubmoduleState.Uninitialized, "sub/uninit", null)]
    [TestCase(" 1111111111111111111111111111111111111111 lib (heads/main)", SubmoduleState.UpToDate, "lib", "heads/main")]
    [TestCase("+1111111111111111111111111111111111111111 my lib (v1.0-2-gabcdef0)", SubmoduleState.Modified, "my lib", "v1.0-2-gabcdef0")]
    [TestCase("U0000000000000000000000000000000000000000 conflicted", SubmoduleState.Conflict, "conflicted", null)]
    [TestCase(" 1111111111111111111111111111111111111111 dir (with parens) (heads/x)", SubmoduleState.UpToDate, "dir (with parens)", "heads/x")]
    public void SubmoduleStatusParser_parses_each_flag(string line, SubmoduleState state, string path, string? describe)
    {
        IReadOnlyList<SubmoduleStatusLine> result = SubmoduleStatusParser.ParseStatus(line + "\n");

        result.Should().ContainSingle();
        result[0].State.Should().Be(state);
        result[0].Path.Should().Be(path);
        result[0].Describe.Should().Be(describe);
        result[0].Commit.Should().HaveLength(40);
    }

    [Test]
    public void SubmoduleStatusParser_handles_recursive_output_and_empty_input()
    {
        SubmoduleStatusParser.ParseStatus(string.Empty).Should().BeEmpty();
        SubmoduleStatusParser.ParseStatus(" 1111111111111111111111111111111111111111 a (heads/m)\n 2222222222222222222222222222222222222222 a/b/c (heads/m)\n")
            .Select(l => l.Path).Should().Equal("a", "a/b/c");
    }

    [Test]
    public void GitModulesParser_joins_path_and_url_by_name()
    {
        string output = "submodule.lib.path lib\nsubmodule.lib.url https://example.com/lib.git\nsubmodule.a.b.path vendor/ab\nsubmodule.a.b.url ../ab\nsubmodule.nopath.url x\n";

        IReadOnlyList<GitModulesEntry> entries = SubmoduleStatusParser.ParseGitModules(output);

        entries.Should().Equal(new GitModulesEntry("lib", "lib", "https://example.com/lib.git"), new GitModulesEntry("a.b", "vendor/ab", "../ab"));
    }

    [Test]
    public void WorktreeParser_reads_branch_detached_locked_and_prunable()
    {
        string output =
            "worktree /repo\nHEAD aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\nbranch refs/heads/main\n\n" +
            "worktree /repo-wt\nHEAD bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\ndetached\nlocked in use\n\n" +
            "worktree /gone\nHEAD cccccccccccccccccccccccccccccccccccccccc\nbranch refs/heads/feature/x\nprunable gitdir file points to non-existent location\n";

        IReadOnlyList<WorktreeInfo> list = WorktreeParser.Parse(output);

        list.Should().HaveCount(3);
        list[0].IsMain.Should().BeTrue();
        list[0].Branch.Should().Be("main");
        list[0].Flags.Should().Be("main");
        list[1].IsMain.Should().BeFalse();
        list[1].IsDetached.Should().BeTrue();
        list[1].BranchText.Should().Be("(detached)");
        list[1].IsLocked.Should().BeTrue();
        list[1].LockReason.Should().Be("in use");
        list[1].ShortHead.Should().Be("bbbbbbbb");
        list[2].Branch.Should().Be("feature/x");
        list[2].IsPrunable.Should().BeTrue();
        list[2].Flags.Should().Be("prunable");
    }

    [Test]
    public void WorktreeParser_marks_bare_main()
    {
        IReadOnlyList<WorktreeInfo> list = WorktreeParser.Parse("worktree /bare.git\nbare\n\n");

        list.Should().ContainSingle();
        list[0].IsBare.Should().BeTrue();
        list[0].BranchText.Should().Be("(bare)");
    }
}
