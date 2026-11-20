using GitExtensions.Desktop.Services;

namespace GitExtensionsDesktopTests;

[TestFixture]
public class RemoteHostsTests
{
    [TestCase("https://github.com/khunder/git-extensions-next.git", "github")]
    [TestCase("git@github.com:khunder/git-extensions-next.git", "github")]
    [TestCase("ssh://git@github.com/o/r", "github")]
    [TestCase("https://gitlab.com/group/project.git", "gitlab")]
    [TestCase("git@gitlab.thalesdigital.io:prs-sdp/ui/sdp-ui.git", "gitlab")]
    [TestCase("https://user@gitlab.example.org/a/b", "gitlab")]
    public void A_remote_url_on_github_or_gitlab_is_recognised(string url, string expected)
        => RemoteHosts.HostOf(url).Should().Be(expected);

    [TestCase("https://bitbucket.org/a/b.git")]
    [TestCase("git@example.com:a/b.git")]
    [TestCase("/home/user/repo")]
    [TestCase("")]
    [TestCase(null)]
    public void Other_remotes_have_no_known_host(string? url)
        => RemoteHosts.HostOf(url).Should().BeNull();

    [Test]
    public void Each_remote_of_a_config_gets_its_own_host()
    {
        const string config = """
            [core]
            	bare = false
            [remote "origin"]
            	url = https://github.com/o/r.git
            	fetch = +refs/heads/*:refs/remotes/origin/*
            [remote "mirror"]
            	url = git@gitlab.com:o/r.git
            [remote "other"]
            	url = https://example.com/o/r.git
            """;

        Dictionary<string, string> hosts = RemoteHosts.FromConfig(config);

        hosts.Should().Equal(new Dictionary<string, string> { ["origin"] = "github", ["mirror"] = "gitlab" });
    }
}
