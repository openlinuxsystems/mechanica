using Mechanica.Core.Net;
using Mechanica.Core.Remote;

namespace Mechanica.Core.Tests.Net;

public class NetEndpointTests
{
    [Theory]
    [InlineData("192.168.1.20:1234", "192.168.1.20", 1234)]
    [InlineData("  example.com  ", "example.com", 27460)]
    [InlineData("localhost:65535", "localhost", 65535)]
    [InlineData("[::1]:9000", "::1", 9000)]
    [InlineData("[fe80::1]", "fe80::1", 27460)]
    [InlineData("fe80::1", "fe80::1", 27460)]
    public void ParsesValidForms(string text, string address, int port)
    {
        Assert.True(NetEndpoint.TryParse(text, NetProtocol.DefaultPort, out var endpoint, out var error), error);
        Assert.Equal(new NetEndpoint(address, port), endpoint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("host:")]
    [InlineData("host:0")]
    [InlineData("host:65536")]
    [InlineData("host:-1")]
    [InlineData("host:12ab")]
    [InlineData(":1234")]
    [InlineData("[::1")]
    [InlineData("[::1]x")]
    [InlineData("my host:1234")]
    public void RejectsInvalidForms(string? text)
    {
        Assert.False(NetEndpoint.TryParse(text, NetProtocol.DefaultPort, out var endpoint, out var error));
        Assert.Null(endpoint);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void FormatsWithBracketsForIpv6()
    {
        Assert.Equal("[::1]:5", new NetEndpoint("::1", 5).ToString());
        Assert.Equal("a.b:5", new NetEndpoint("a.b", 5).ToString());
    }
}

public class LaunchOptionsTests
{
    private static LaunchOptions Parse(params string[] args)
    {
        Assert.True(LaunchOptions.TryParse(args, out var options, out var error), error);
        return options;
    }

    [Fact]
    public void ParsesALoadAndAName()
    {
        var options = Parse("--host", "--load", "saves/world.json", "--name", "Ada");
        Assert.Equal("saves/world.json", options.LoadPath);
        Assert.Equal("Ada", options.PlayerName);
        Assert.Equal("Bo", Parse("--join", "a", "--name", "Bo").PlayerName);
    }

    [Fact]
    public void NoArgumentsIsOffline() => Assert.Equal(LaunchOptions.Offline, Parse());

    [Fact]
    public void UnrelatedArgumentsAreIgnored() => Assert.Equal(SessionMode.Offline, Parse("--fullscreen", "x").Mode);

    [Fact]
    public void HostUsesDefaultPort() =>
        Assert.Equal(LaunchOptions.ForHost(NetProtocol.DefaultPort), Parse("--host"));

    [Fact]
    public void HostWithPortAndMaxPlayers() =>
        Assert.Equal(LaunchOptions.ForHost(4000, 2), Parse("--host", "4000", "--max-players", "2"));

    [Fact]
    public void Join() =>
        Assert.Equal(LaunchOptions.ForJoin(new NetEndpoint("10.0.0.2", 4000)), Parse("--join", "10.0.0.2:4000"));

    [Fact]
    public void AiEndpointAndRecordReplay()
    {
        var options = Parse("--host", "--ai", "--record", "run.json", "--replay", "old.json", "--ai-token", "t");
        Assert.Equal((NetProtocol.DefaultAiPort, "127.0.0.1", "t", "run.json", "old.json"),
            (options.AiPort, options.AiBind, options.AiToken, options.RecordPath, options.ReplayPath));
        Assert.Equal((5000, "0.0.0.0"), (Parse("--ai", "5000", "--ai-bind", "0.0.0.0").AiPort, Parse("--ai-bind", "0.0.0.0").AiBind));
        Assert.Null(Parse("--host").AiPort);
        Assert.True(Parse("--host", "--ai", "--ai-allow-reload-with-clients").AiAllowReloadWithClients);
        Assert.False(Parse("--host", "--ai").AiAllowReloadWithClients);
    }

    [Fact]
    public void AiTokensMapToScopes()
    {
        static string? NoEnvironment(string _) => null;
        Assert.False(Parse("--ai").HasAiToken(NoEnvironment));

        var single = Parse("--ai", "--ai-token", "t").AiAccess(NoEnvironment);
        Assert.Equal(EndpointAccess.All, single.ScopesFor("t"));

        var split = Parse("--ai", "--ai-token", "p", "--ai-admin-token", "a", "--ai-observe-token", "o").AiAccess(NoEnvironment);
        Assert.Equal(new HashSet<AgentScope> { AgentScope.Observe, AgentScope.Play }, split.ScopesFor("p"));
        Assert.Equal(EndpointAccess.All, split.ScopesFor("a"));
        Assert.Equal(new HashSet<AgentScope> { AgentScope.Observe }, split.ScopesFor("o"));
        Assert.Null(split.ScopesFor("x"));

        var fromEnvironment = Parse("--ai").AiAccess(name => name == "MECHANICA_AI_ADMIN_TOKEN" ? "env" : null);
        Assert.Equal(EndpointAccess.All, fromEnvironment.ScopesFor("env"));
        Assert.Equal("cli", Parse("--ai-admin-token", "cli").AiAdminToken);
    }

    [Fact]
    public void Seed() => Assert.Equal(LaunchOptions.ForHost(4000) with { Seed = -42 }, Parse("--host", "4000", "--seed", "-42"));

    [Fact]
    public void GameMode()
    {
        Assert.Equal(Mechanica.Core.Survival.GameMode.Survival, Parse("--host").GameMode);
        Assert.Equal(Mechanica.Core.Survival.GameMode.Creative, Parse("--creative", "--host").GameMode);
        Assert.Equal(Mechanica.Core.Survival.GameMode.Survival, Parse("--creative", "--survival").GameMode);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.1.2.3", true)]
    [InlineData("localhost", true)]
    [InlineData("::1", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("192.168.1.2", false)]
    public void LoopbackAddresses(string address, bool loopback) => Assert.Equal(loopback, LaunchOptions.IsLoopback(address));

    [Theory]
    [InlineData("--ai", "0")]
    [InlineData("--ai-token")]
    [InlineData("--ai-admin-token")]
    [InlineData("--ai-observe-token")]
    [InlineData("--record")]
    [InlineData("--join", "a", "--ai")]
    [InlineData("--join", "a", "--replay", "x.json")]
    [InlineData("--join")]
    [InlineData("--join", "--host")]
    [InlineData("--host", "--join", "a")]
    [InlineData("--join", "a", "--host")]
    [InlineData("--host", "99999")]
    [InlineData("--max-players", "0")]
    [InlineData("--max-players", "17")]
    [InlineData("--max-players")]
    [InlineData("--seed", "x")]
    [InlineData("--seed")]
    [InlineData("--load")]
    [InlineData("--join", "a", "--load", "world.json")]
    public void RejectsBadArguments(params string[] args)
    {
        Assert.False(LaunchOptions.TryParse(args, out _, out var error));
        Assert.NotEmpty(error);
    }
}

public class ContentManifestTests
{
    private static ContentManifest M(string fingerprint, params (string Id, string Version)[] mods) =>
        new([.. mods.Select(m => new ModStamp(m.Id, m.Version))], fingerprint);

    [Fact]
    public void IdenticalContentMatches() =>
        Assert.Empty(ContentManifest.Differences(M("f", ("vanilla", "1.0.0"), ("cars", "2.0.0")), M("f", ("vanilla", "1.0.0"), ("cars", "2.0.0"))));

    [Fact]
    public void NamesMissingExtraAndOutdatedMods()
    {
        var host = M("a", ("vanilla", "1.0.0"), ("cars", "2.0.0"), ("wings", "1.1.0"));
        var client = M("b", ("vanilla", "1.0.0"), ("cars", "1.9.0"), ("guns", "0.1.0"));
        Assert.Equal(
            ["cars: host has 2.0.0, you have 1.9.0", "missing wings 1.1.0", "guns 0.1.0 is not on the host"],
            ContentManifest.Differences(host, client));
    }

    [Fact]
    public void ReportsLoadOrderOnlyWhenModsAgree() =>
        Assert.Equal(["load order differs: the host loads vanilla, cars, wings"],
            ContentManifest.Differences(M("a", ("vanilla", "1"), ("cars", "1"), ("wings", "1")), M("b", ("vanilla", "1"), ("wings", "1"), ("cars", "1"))));

    [Fact]
    public void ReportsChangedFilesWhenOnlyFingerprintDiffers() =>
        Assert.Contains("files differ", Assert.Single(ContentManifest.Differences(M("a", ("vanilla", "1")), M("b", ("vanilla", "1")))));

    [Fact]
    public void RoundTripsThroughJson()
    {
        var manifest = M("abc", ("vanilla", "1.0.0"));
        var decoded = ContentManifest.FromJson(manifest.ToJson());
        Assert.NotNull(decoded);
        Assert.Empty(ContentManifest.Differences(manifest, decoded));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"mods": null, "fingerprint": ""}""")]
    [InlineData("""{"mods": [null], "fingerprint": ""}""")]
    [InlineData("""{"mods": [], "fingerprint": "", "more": 1}""")]
    public void RejectsMalformedJson(string json) => Assert.Null(ContentManifest.FromJson(json));
}

public class ContentManifestFromLoaderTests
{
    [Fact]
    public void ListsLoadedModsInOrderWithTheLoaderFingerprint()
    {
        var content = Mechanica.Core.Data.ModLoader.LoadAll(Data.TestMods.FixtureRoot);
        var manifest = ContentManifest.From(content);
        Assert.Equal(content.Mods.Select(m => m.Id), manifest.Mods.Select(m => m.Id));
        Assert.Equal(content.Fingerprint, manifest.Fingerprint);
        Assert.Empty(ContentManifest.Differences(manifest, ContentManifest.From(Mechanica.Core.Data.ModLoader.LoadAll(Data.TestMods.FixtureRoot))));
    }
}
