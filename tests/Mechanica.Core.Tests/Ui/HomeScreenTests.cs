using Mechanica.Core.Ui;

namespace Mechanica.Core.Tests.Ui;

public class HomeScreenTests
{
    [Fact]
    public void LinksNeedALabelAndAnHttpsAddress()
    {
        var links = HomeScreen.ParseLinks("""
            {
              // comments are fine
              "links": [
                { "label": "Site", "url": "https://example.com" },
                { "label": "Plain", "url": "http://example.com" },
                { "label": "", "url": "https://example.com" },
                { "label": "Bad", "url": "not a url" },
              ]
            }
            """);
        Assert.Equal([new HomeLink("Site", "https://example.com")], links);
        Assert.Empty(HomeScreen.ParseLinks("{"));
        Assert.Empty(HomeScreen.ParseLinks("""{"links": []}"""));
    }

    [Theory]
    [InlineData(10, "just now")]
    [InlineData(90, "a minute ago")]
    [InlineData(5 * 60, "5 minutes ago")]
    [InlineData(3 * 3600, "3 hours ago")]
    [InlineData(30 * 3600, "yesterday")]
    [InlineData(4 * 86400, "4 days ago")]
    public void AgoReadsNaturally(int seconds, string text)
    {
        var now = new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(text, HomeScreen.Ago(now.AddSeconds(-seconds), now));
    }
}
