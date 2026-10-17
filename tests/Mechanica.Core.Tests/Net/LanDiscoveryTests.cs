using System.Text;
using Mechanica.Core.Net;

namespace Mechanica.Core.Tests.Net;

public class LanDiscoveryTests
{
    private static readonly LanSession World = new("My World", "Ada", 27460, 2, 4, "survival", NetProtocol.Version, ["vanilla 0.1.0"]);

    [Fact]
    public void AnAnswerRoundTrips()
    {
        var read = LanDiscovery.Decode(LanDiscovery.Encode(World));

        Assert.NotNull(read);
        Assert.Equal(World with { Mods = read.Mods }, read);
        Assert.Equal(["vanilla 0.1.0"], read.Mods);
        Assert.True(read.Compatible);
        Assert.True(read.HasRoom);
    }

    [Fact]
    public void OnlyTheQueryIsAQuery()
    {
        Assert.True(LanDiscovery.IsQuery(LanDiscovery.Query));
        Assert.False(LanDiscovery.IsQuery(Encoding.UTF8.GetBytes("Mechanica?lan2")));
        Assert.False(LanDiscovery.IsQuery(LanDiscovery.Encode(World)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "game": "Other", "session": { "world": "w", "host": "h", "port": 1, "players": 1, "max_players": 2, "mode": "survival", "protocol": 3, "mods": [] } }""")]
    [InlineData("""{ "game": "Mechanica", "session": { "world": "w", "host": "h", "port": 0, "players": 1, "max_players": 2, "mode": "survival", "protocol": 3, "mods": [] } }""")]
    [InlineData("""{ "game": "Mechanica", "session": { "world": "w", "host": "h", "port": 5, "players": 1, "max_players": 99, "mode": "survival", "protocol": 3, "mods": [] } }""")]
    [InlineData("""{ "game": "Mechanica", "session": { "host": "h", "port": 5, "players": 1, "max_players": 2, "mode": "survival", "protocol": 3, "mods": [] } }""")]
    [InlineData("""{ "game": "Mechanica", "session": "nope" }""")]
    public void NonsenseIsNotAnAnswer(string packet) => Assert.Null(LanDiscovery.Decode(Encoding.UTF8.GetBytes(packet)));

    [Fact]
    public void AnOversizedPacketIsIgnored()
    {
        var big = World with { Mods = [.. Enumerable.Range(0, 200).Select(i => $"mod_{i} 1.0.0")] };

        Assert.Null(LanDiscovery.Decode(LanDiscovery.Encode(big)));
    }

    [Fact]
    public void LongNamesAreClipped()
    {
        var read = LanDiscovery.Decode(LanDiscovery.Encode(World with { World = new string('w', 200) }))!;

        Assert.Equal(48, read.World.Length);
    }

    [Fact]
    public void AnOlderProtocolIsListedButNotCompatible()
    {
        var read = LanDiscovery.Decode(LanDiscovery.Encode(World with { Protocol = NetProtocol.Version - 1 }))!;

        Assert.False(read.Compatible);
    }

    [Fact]
    public void TheListKeepsHostsUntilTheyStopAnswering()
    {
        var list = new LanBrowserList();
        Assert.True(list.Heard("192.168.1.5", LanDiscovery.Encode(World), now: 0));
        Assert.True(list.Heard("192.168.1.9", LanDiscovery.Encode(World with { World = "Another" }), now: 1));
        Assert.False(list.Heard("192.168.1.7", Encoding.UTF8.GetBytes("junk"), now: 1));

        Assert.Equal(["Another", "My World"], list.Entries.Select(e => e.Session.World));
        Assert.Equal(new NetEndpoint("192.168.1.5", 27460), list.Entries[1].Endpoint);

        list.Expire(now: LanDiscovery.EntryLifetime + 0.5);
        Assert.Equal("Another", Assert.Single(list.Entries).Session.World);
        list.Clear();
        Assert.Empty(list.Entries);
    }

    [Fact]
    public void AHostHeardOnLoopbackAndTheLanIsListedOnce()
    {
        var list = new LanBrowserList();
        list.Heard("127.0.0.1", LanDiscovery.Encode(World), now: 0);
        list.Heard("192.168.1.5", LanDiscovery.Encode(World), now: 0);
        list.Heard("127.0.0.1", LanDiscovery.Encode(World), now: 0.5);

        Assert.Equal("192.168.1.5", Assert.Single(list.Entries).Address);
    }

    [Fact]
    public void TwoHostsOnOneMachineAreBothListed()
    {
        var list = new LanBrowserList();
        list.Heard("192.168.1.5", LanDiscovery.Encode(World), now: 0);
        list.Heard("192.168.1.5", LanDiscovery.Encode(World with { Port = 27470, World = "Second" }), now: 0);

        Assert.Equal(2, list.Entries.Count);
    }
}
