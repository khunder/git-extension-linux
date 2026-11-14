using GitExtensions.Desktop.Services;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class CommandMatcherTests
{
    [TestCase("", "Anything", true)]
    [TestCase("   ", "Anything", true)]
    [TestCase("fetch", "Fetch", true)]
    [TestCase("FETCH", "fetch", true)]
    [TestCase("ftc", "Fetch", true)]
    [TestCase("sa", "Stage all changes", true)]
    [TestCase("tog out", "Toggle Output panel", true)]
    [TestCase("tgop", "Toggle Output panel", true)]
    [TestCase("xyz", "Fetch", false)]
    [TestCase("hcetf", "Fetch", false)]
    [TestCase("fetchh", "Fetch", false)]
    [TestCase("push", "Pull", false)]
    public void Matches_case_insensitive_subsequences(string query, string text, bool expected)
        => (CommandMatcher.Score(query, text) is not null).Should().Be(expected);

    [Test]
    public void Prefix_beats_word_start_beats_scattered_match()
    {
        string[] titles = ["Show recent repositories", "Stage all changes", "Refresh", "Rebase the current branch"];

        CommandMatcher.Rank(titles, "re", t => t).Should().ContainInOrder("Refresh", "Rebase the current branch", "Show recent repositories");
    }

    [Test]
    public void Word_starts_beat_a_match_in_the_middle_of_a_word()
    {
        string[] titles = ["Cycle theme", "Toggle theme", "Settings"];

        // "th" starts the word "theme" in both; "Settings" has t..h? No: it does not match at all.
        CommandMatcher.Rank(titles, "th", t => t).Should().Equal("Cycle theme", "Toggle theme");
        CommandMatcher.Rank(titles, "set", t => t).Should().Equal("Settings");
    }

    [Test]
    public void Contiguous_matches_beat_scattered_ones()
    {
        string[] titles = ["Open repository", "Push"];

        CommandMatcher.Rank(titles, "pus", t => t).Should().Equal("Push");
        CommandMatcher.Score("rep", "Open repository")!.Value.Should().BeGreaterThan(CommandMatcher.Score("rpy", "Open repository")!.Value);
    }

    [Test]
    public void Equal_scores_keep_the_original_order_and_an_empty_query_keeps_everything()
    {
        string[] titles = ["Pull", "Push", "Fetch"];

        CommandMatcher.Rank(titles, "p", t => t).Should().Equal("Pull", "Push");
        CommandMatcher.Rank(titles, string.Empty, t => t).Should().Equal(titles);
        CommandMatcher.Rank(titles, null, t => t).Should().Equal(titles);
    }

    [Test]
    public void Null_text_never_matches_a_query()
        => CommandMatcher.Score("a", null).Should().BeNull();
}
