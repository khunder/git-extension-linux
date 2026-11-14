using GitExtensions.Desktop.Diff;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class UnifiedDiffParserTests
{
    private const string Sample =
        "diff --git a/a.txt b/a.txt\n" +
        "index 1111111..2222222 100644\n" +
        "--- a/a.txt\n" +
        "+++ b/a.txt\n" +
        "@@ -1,3 +1,3 @@ void M()\n" +
        " keep\n" +
        "--- looks like a header\n" +
        "+++ also does\n" +
        " tail\n" +
        "\\ No newline at end of file\n";

    [Test]
    public void Parse_classifies_lines_and_numbers_them()
    {
        IReadOnlyList<DiffLine> lines = UnifiedDiffParser.Parse(Sample);

        lines.Select(l => l.Kind).Should().Equal(
            DiffLineKind.Header, DiffLineKind.Header, DiffLineKind.Header, DiffLineKind.Header,
            DiffLineKind.Hunk,
            DiffLineKind.Context, DiffLineKind.Removed, DiffLineKind.Added, DiffLineKind.Context,
            DiffLineKind.Info);
        lines[5].OldLineNumber.Should().Be(1);
        lines[5].NewLineNumber.Should().Be(1);
        lines[6].OldLineNumber.Should().Be(2);
        lines[6].NewLineNumber.Should().BeNull();
        lines[7].NewLineNumber.Should().Be(2);
        lines[8].OldLineNumber.Should().Be(3);
        lines[8].NewLineNumber.Should().Be(3);
    }

    [Test]
    public void Parse_handles_multiple_hunks_and_single_line_counts()
    {
        IReadOnlyList<DiffLine> lines = UnifiedDiffParser.Parse("@@ -1 +1 @@\n-a\n+b\n@@ -10,2 +10,2 @@\n x\n-y\n+z\n");

        lines.Select(l => l.Kind).Should().Equal(
            DiffLineKind.Hunk, DiffLineKind.Removed, DiffLineKind.Added,
            DiffLineKind.Hunk, DiffLineKind.Context, DiffLineKind.Removed, DiffLineKind.Added);
        lines[4].OldLineNumber.Should().Be(10);
        lines[6].NewLineNumber.Should().Be(11);
    }

    [Test]
    public void Parse_strips_carriage_returns_and_ignores_empty_input()
    {
        UnifiedDiffParser.Parse("@@ -1 +1 @@\r\n-a\r\n+b\r\n").Select(l => l.Text).Should().Equal("@@ -1 +1 @@", "-a", "+b");
        UnifiedDiffParser.Parse(null).Should().BeEmpty();
        UnifiedDiffParser.Parse(string.Empty).Should().BeEmpty();
    }

    [Test]
    public void FromNewFileContent_marks_every_line_as_added()
    {
        IReadOnlyList<DiffLine> lines = UnifiedDiffParser.FromNewFileContent("c.txt", "one\ntwo\n");

        lines.Select(l => l.Kind).Should().Equal(DiffLineKind.Header, DiffLineKind.Hunk, DiffLineKind.Added, DiffLineKind.Added);
        lines[1].Text.Should().Be("@@ -0,0 +1,2 @@");
        lines[3].Text.Should().Be("+two");
        lines[3].NewLineNumber.Should().Be(2);
    }
}
