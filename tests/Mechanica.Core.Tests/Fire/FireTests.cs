using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Fire;
using Mechanica.Core.Quests;
using Mechanica.Core.Saves;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.Tests.Survival;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Core.Tests.Fire;

public class FireTests
{
    private const string Poi = "vanilla:crash_site";

    private readonly SurvivalFixture _f = new();

    private FireActions Fire => _f.Fire;

    [Fact]
    public void ShippedSettingsLoadAndBadOnesAreRefused()
    {
        var settings = FireSettings.FromJson(File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "fire.json")));

        Assert.Equal("fire", settings.SiteMarker);
        Assert.Equal("burn_wood", settings.Sound);
        Assert.Contains("spread_at", Assert.Throws<FormatException>(() => FireSettings.FromJson("""{ "spread_at": 2 }""")).Message);
        Assert.Contains("max_fires", Assert.Throws<FormatException>(() => FireSettings.FromJson("""{ "max_fires": 0 }""")).Message);
        Assert.Throws<FormatException>(() => FireSettings.FromJson("""{ "smoke": 1 }"""));
    }

    [Fact]
    public void SiteFiresBurnUntilWaterPutsThemOut()
    {
        LightCrashSite();
        var outs = new List<FireView>();
        Fire.SiteOut += outs.Add;

        Assert.Equal(["site:0:0", "site:0:1"], Fire.Fires().Select(f => f.Id));
        Assert.All(Fire.Fires(), f => Assert.Equal(1f, f.Strength));
        Assert.Equal(Poi, Fire.Fires()[0].Poi);

        var reached = Assert.Single(Fire.Douse(new Vector3(3, 0, 13), 0.5f, 0.4f));
        Assert.Equal(0.6f, reached.Strength, 3);
        Tick(1);
        Assert.Equal(0.68f, Fire.Fires()[0].Strength, 2); // it grows back while it burns

        Fire.Douse(new Vector3(3, 0, 13), 0.5f, 1);
        Assert.Equal("site:0:0", Assert.Single(outs).Id);
        Assert.Equal(1, Fire.SitesOut(Poi, "fire"));
        Assert.Equal(0, Fire.SitesOut(Poi, "npc"));
        Tick(10);
        Assert.Equal(0f, Fire.Fires()[0].Strength); // out stays out
        Assert.Equal(["site:0:1"], Fire.Near(new Vector3(0, 0, 20), 30).Select(f => f.Id));
        Assert.Empty(Fire.Douse(new Vector3(3, 0, 13), 0.5f, 1));

        // Lighting again (a new world generator for the same world) leaves known sites as they are.
        LightCrashSite();
        Assert.Equal(0f, Fire.Fires()[0].Strength);
    }

    [Fact]
    public void SiteFiresSurviveASaveAndLoad()
    {
        LightCrashSite();
        Fire.Douse(new Vector3(3, 0, 13), 0.5f, 1);
        var saved = Fire.SaveSites();

        // The save is read before the world lights its sites, as when a world loads.
        var loaded = new FireActions(new FireSettings(), _f.Survival, _f.World);
        loaded.LoadSites(saved);
        loaded.LightSites([Site()]);

        Assert.Equal([0f, 1f], loaded.Fires().Select(f => f.Strength));
        Assert.Equal(1, loaded.SitesOut(Poi, "fire"));

        // And after, as when a save is applied to a running world.
        var later = new FireActions(new FireSettings(), _f.Survival, _f.World);
        later.LightSites([Site()]);
        later.LoadSites([new SavedSiteFire(0, 1, 0.25f)]);
        Assert.Equal([1f, 0.25f], later.Fires().Select(f => f.Strength));
    }

    [Fact]
    public void ASiteFireSetsCloseFlammablePartsAlightAndBurnsThemAway()
    {
        LightCrashSite();
        var plank = _f.Add(Plank, new Vector3(3, 0, 14));
        var block = _f.Add(Block, new Vector3(3.5f, 0, 13));
        var far = _f.Add(Plank, new Vector3(3, 0, 20));

        Tick(Fire.Settings.Tick);
        Assert.Equal(Fire.Settings.Ignite, _f.World.Creations[plank].FireOf(1));
        Assert.Equal(0f, _f.World.Creations[block].FireOf(1)); // not flammable
        Assert.Equal(0f, _f.World.Creations[far].FireOf(1));
        Assert.Contains(Fire.Fires(), f => f.Id == $"part:{plank}:1" && f.Creation == plank && f.Part == 1);

        // 30 health at up to 4 a second: it burns away within a minute.
        for (var i = 0; i < 240 && _f.World.Creations.ContainsKey(plank); i++)
            Tick(0.25f);
        Assert.False(_f.World.Creations.ContainsKey(plank), "the plank burnt away");
        Assert.DoesNotContain(Fire.Fires(), f => f.Creation == plank);
        Assert.True(_f.World.Creations.ContainsKey(block));
    }

    [Fact]
    public void FireSpreadsToAttachedFlammablePartsOnly()
    {
        var creation = new Creation(_f.World.NextCreationId(), _f.Catalog);
        foreach (var (part, x) in new[] { (Plank, 0), (Plank, 1), (Block, 2), (Plank, 3) })
            Assert.True(creation.TryPlacePart(part, new GridCell(x, 0, 0), default, out _, out var error), error);
        creation.SetAllBodyPoses(new Pose(new Vector3(0, 0, -20), Quaternion.Identity));
        _f.World.Added(creation);

        Assert.Equal("The Block does not burn.", Fire.Ignite(creation, 3));
        Assert.Null(Fire.Ignite(creation, 1));
        Tick(1);
        Assert.Equal(0f, creation.FireOf(2)); // too weak to spread yet
        for (var i = 0; i < 24; i++)
            Tick(0.25f);
        Assert.True(creation.FireOf(2) > 0, "the plank next to it caught");
        for (var i = 0; i < 40; i++)
            Tick(0.25f);
        Assert.Equal(0f, creation.FireOf(4)); // the block between stops it
    }

    [Fact]
    public void WaterPutsBurningPartsOut()
    {
        var plank = _f.World.Creations[_f.Add(Plank, new Vector3(0, 0, -20))];
        Assert.Null(Fire.Ignite(plank, 1, 0.5f));

        var reached = Assert.Single(Fire.Douse(new Vector3(0, 0.1f, -20), 0.2f, 0.3f));
        Assert.Equal(0.2f, reached.Strength, 3);
        Fire.Douse(new Vector3(0, 0.1f, -20), 0.2f, 0.3f);
        Assert.Equal(0f, plank.FireOf(1));
        Assert.Empty(Fire.Fires());

        // A part that sinks into water goes out too.
        Assert.Null(Fire.Ignite(plank, 1));
        _f.World.WaterLevel = 5;
        Tick(1);
        Assert.Equal(0f, plank.FireOf(1));
    }

    [Fact]
    public void PlayersInAFireAreBurntButNotInCreative()
    {
        LightCrashSite();
        _f.World.Feet[Me.Peer] = new Vector3(3, 0, 13.5f);
        Tick(1);
        Assert.Equal(90f, _f.Survival.HealthOf(Me.Peer)!.Value, 1);
        Assert.Equal(100f, _f.Survival.HealthOf(You.Peer)); // standing elsewhere

        var creative = new SurvivalFixture(mode: GameMode.Creative);
        creative.Fire.LightSites([Site()]);
        creative.World.Feet[Me.Peer] = new Vector3(3, 0, 13.5f);
        creative.Fire.Tick(1);
        Assert.Equal(100f, creative.Survival.HealthOf(Me.Peer));
    }

    [Fact]
    public void NoMoreThanMaxFiresBurn()
    {
        var fire = new FireActions(new FireSettings { MaxFires = 2 }, _f.Survival, _f.World);
        fire.LightSites([Site()]);
        var plank = _f.World.Creations[_f.Add(Plank, new Vector3(0, 0, -20))];

        Assert.Equal("Too many fires are burning already.", fire.Ignite(plank, 1));
        fire.Douse(new Vector3(3, 0, 13), 0.5f, 1);
        Assert.Null(fire.Ignite(plank, 1));
    }

    [Fact]
    public void ReachLimitsPlayersButNotScripts()
    {
        Assert.Null(Fire.CheckReach(Me, new Vector3(0, 1.6f, -2), 1));
        Assert.StartsWith("That is ", Fire.CheckReach(Me, new Vector3(0, 1.6f, -20), 1));
        Assert.Null(Fire.CheckReach(new ActionCaller(1, ActionSource.Script), new Vector3(0, 0, -200), 0));
    }

    [Fact]
    public void GetFiresListsEveryFire()
    {
        Fire.Register(_f.Dispatcher);
        LightCrashSite();

        var fires = _f.Ok<List<FireView>>(Me, FireActions.GetFires, new { });

        Assert.Equal(["site:0:0", "site:0:1"], fires.Select(f => f.Id));
        Assert.Equal(new Vec3(3, 0, 13), fires[0].Position);
    }

    [Fact]
    public void FireIsSavedOnThePartButNotInBlueprints()
    {
        var creation = _f.World.Creations[_f.Add(Plank, new Vector3(0, 0, -20))];
        Fire.Ignite(creation, 1, 0.75f);

        var json = CreationJson.Write(creation);
        var read = CreationJson.Read(json, _f.Catalog, "c", []);

        Assert.Contains("\"fire\": 0.75", json);
        Assert.Equal(0.75f, read!.FireOf(1));
        Assert.DoesNotContain("\"fire\"", Blueprint.Write(creation, "b", DateTimeOffset.UnixEpoch));
        var bad = new List<DataDiagnostic>();
        Assert.Null(CreationJson.Read(json.Replace("\"fire\": 0.75", "\"fire\": 3"), _f.Catalog, "c", bad));
        Assert.Contains(bad, d => d.JsonPath!.EndsWith(".fire", StringComparison.Ordinal));
    }

    [Fact]
    public void ASaveKeepsItsSiteFires()
    {
        var save = new SaveGame { Seed = 1, Fires = [new SavedSiteFire(0, 2, 0), new SavedSiteFire(0, 1, 0.5f)] };

        var read = SaveGame.Read(save.Write(), "s.json", [])!;

        Assert.Equal([new SavedSiteFire(0, 1, 0.5f), new SavedSiteFire(0, 2, 0)], read.Fires);
        Assert.Null(SaveGame.Read(new SaveGame { Seed = 1 }.Write(), "s.json", [])!.Fires);
        var bad = new List<DataDiagnostic>();
        Assert.Null(SaveGame.Read(save.Write().Replace("\"strength\": 0.5", "\"strength\": 7"), "s.json", bad));
        Assert.Contains(bad, d => d.JsonPath == "$.fires[0].strength");
    }

    private void LightCrashSite() => Fire.LightSites([Site()]);

    // The crash site's first two fires and its battery, which is not a fire.
    private static QuestSite Site() => new(0, Poi, Vector3.Zero, 80,
        [new QuestMarker(0, "fire", null, new Vector3(3, 0, 13)), new QuestMarker(1, "fire", null, new Vector3(-6, 0, 29)),
            new QuestMarker(2, "item", "master_battery", new Vector3(-2, 0.5f, 12))]);

    private void Tick(float seconds) => Fire.Tick(seconds);
}
