using Mechanica.Core.Data;

namespace Mechanica.Core.Tests.Data;

public class SemVersionTests
{
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("0.0.0")]
    [InlineData("10.20.30-beta.2")]
    public void ValidVersionsRoundTrip(string text) => Assert.Equal(text, SemVersion.Parse(text).ToString());

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("v1.2.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3+build")]
    [InlineData(" ")]
    public void InvalidVersionsAreRejected(string text) => Assert.False(SemVersion.TryParse(text, out _));

    [Theory]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-beta")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("1.9.9", "1.10.0")]
    public void OrderingFollowsSemver(string lower, string higher) =>
        Assert.True(SemVersion.Parse(lower) < SemVersion.Parse(higher));

    [Theory]
    [InlineData("*", "3.4.5", true)]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.4", false)]
    [InlineData("=1.2.3", "1.2.3", true)]
    [InlineData(">=1.2.0 <2.0.0", "1.9.0", true)]
    [InlineData(">=1.2.0 <2.0.0", "2.0.0", false)]
    [InlineData(">1.2.0", "1.2.0", false)]
    [InlineData("<=1.2.0", "1.2.0", true)]
    [InlineData("^1.2.0", "1.9.0", true)]
    [InlineData("^1.2.0", "2.0.0", false)]
    [InlineData("^1.2.0", "1.1.0", false)]
    [InlineData("^0.1.0", "0.1.5", true)]
    [InlineData("^0.1.0", "0.2.0", false)]
    [InlineData("~1.2.0", "1.2.9", true)]
    [InlineData("~1.2.0", "1.3.0", false)]
    public void RangesMatch(string range, string version, bool expected)
    {
        Assert.True(VersionRange.TryParse(range, out var r));
        Assert.Equal(expected, r.IsSatisfiedBy(SemVersion.Parse(version)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("=>1.0.0")]
    [InlineData(">=1.0")]
    [InlineData("latest")]
    public void InvalidRangesAreRejected(string text) => Assert.False(VersionRange.TryParse(text, out _));
}
