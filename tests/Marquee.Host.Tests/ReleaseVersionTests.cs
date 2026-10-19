using Marquee.Host.Updates;

namespace Marquee.Host.Tests;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("v0.2.0", 0, 2, 0)]
    [InlineData("0.2.0", 0, 2, 0)]
    [InlineData("v10.20.30", 10, 20, 30)]
    public void A_tag_is_a_plain_version_with_an_optional_v(string tag, int major, int minor, int patch)
    {
        Assert.True(ReleaseVersion.TryParseTag(tag, out var version));
        Assert.Equal(new Version(major, minor, patch), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v")]
    [InlineData("v0.2")]
    [InlineData("v0.2.0.1")]
    [InlineData("v0.2.0-rc.1")]
    [InlineData("v0.2.0+abc")]
    [InlineData("V0.2.0")]
    [InlineData("vv0.2.0")]
    [InlineData(" v0.2.0")]
    [InlineData("v0.2.0 ")]
    [InlineData("v01.2.0")]
    [InlineData("v-1.2.0")]
    [InlineData("v99999999999.0.0")]
    public void Anything_else_is_not_a_release_tag(string? tag)
    {
        Assert.False(ReleaseVersion.TryParseTag(tag, out var version));
        Assert.Null(version);
    }

    [Theory]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("0.1.0+4393804e1c2b", "0.1.0")]
    [InlineData("0.2.0-rc.1", "0.2.0-rc.1")]
    [InlineData("0.2.0-rc.1+4393804e1c2b", "0.2.0-rc.1")]
    public void The_running_version_drops_the_commit(string informational, string expected)
    {
        Assert.True(ReleaseVersion.TryParseRunning(informational, out var running));
        Assert.Equal(expected, running.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dev")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("+abc")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-rc 1")]
    public void A_running_version_that_isnt_SemVer_is_refused(string? informational)
    {
        Assert.False(ReleaseVersion.TryParseRunning(informational, out var running));
        Assert.Null(running);
    }

    [Theory]
    [InlineData("0.1.0", "v0.2.0", true)]
    [InlineData("0.1.0", "v0.1.1", true)]
    [InlineData("0.9.0", "v0.10.0", true)]
    [InlineData("0.1.0", "v1.0.0", true)]
    [InlineData("0.1.0", "v0.1.0", false)]
    [InlineData("0.1.0+4393804e1c2b", "v0.1.0", false)]
    [InlineData("0.2.0", "v0.1.9", false)]
    [InlineData("0.10.0", "v0.9.0", false)]
    [InlineData("0.2.0-rc.1", "v0.2.0", true)]
    [InlineData("0.2.0-rc.1+4393804e1c2b", "v0.2.0", true)]
    [InlineData("0.2.0-rc.1", "v0.1.9", false)]
    public void A_release_is_newer_by_SemVer_precedence(string running, string tag, bool newer)
    {
        Assert.True(ReleaseVersion.TryParseRunning(running, out var current));
        Assert.True(ReleaseVersion.TryParseTag(tag, out var release));

        Assert.Equal(newer, current.IsOlderThan(release));
    }
}
