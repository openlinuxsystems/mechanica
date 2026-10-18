using System.Numerics;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Core.Tests.Survival;

public class SurvivalActionsTests
{
    private readonly SurvivalFixture _f = new();

    [Fact]
    public void Bare_soil_views_survive_the_wire()
    {
        // Nulls are left out of the JSON clients are sent; the view must still read back.
        var bare = new SoilView(3, 4, null, null, 0, 0, 0);
        var json = System.Text.Json.JsonSerializer.Serialize(bare, ActionJson.Options);
        Assert.Equal(bare, System.Text.Json.JsonSerializer.Deserialize<SoilView>(json, ActionJson.Options));
    }

    // ---- Health, death and respawn ----

    [Fact]
    public void PlayersJoinAtFullHealthWithTheStartingItems()
    {
        var vitals = _f.Ok<VitalsView>(Me, SurvivalActions.GetVitals, new { });
        Assert.Equal(new VitalsView(Me.Peer, 100, 100, false, 0, false, GameMode.Survival, FriendlyFire: true), vitals);
        Assert.Equal(2, _f.Pockets(Me).Count(Seed));
    }

    [Fact]
    public void CreativePlayersGetNoStartingItemsAndTakeNoDamage()
    {
        var f = new SurvivalFixture(mode: GameMode.Creative);
        Assert.Equal(0, f.Pockets(Me).Count(Seed));
        Assert.Equal(100f, f.Survival.Damage(Me.Peer, 500, "test"));
        Assert.False(f.Survival.IsDead(Me.Peer));
    }

    [Fact]
    public void DamageKillsAtZeroAndTheBodyDropsItsInventory()
    {
        var hurt = new List<(int, float, string)>();
        _f.Survival.Hurt += (p, d, c) => hurt.Add((p, d, c));
        _f.Pockets(Me).Add(Scrap, 7);

        Assert.Equal(60f, _f.Survival.Damage(Me.Peer, 40, "test"));
        Assert.Equal(0f, _f.Survival.Damage(Me.Peer, 80, "test"));

        Assert.True(_f.Survival.IsDead(Me.Peer));
        Assert.Equal([(2, 40f, "test"), (2, 60f, "test")], hurt);
        Assert.True(_f.Pockets(Me).IsEmpty);
        var drop = Assert.Single(_f.Survival.Drops.Values);
        Assert.Equal(new Vec3(0, 0, 3), drop.Position);
        Assert.Equal([new ItemStack(Seed, 2), new ItemStack(Scrap, 7)], drop.Items);
        Assert.True(drop.KnockedOut); // shown as the knockout bag
        Assert.Equal(0f, _f.Survival.Damage(Me.Peer, 10, "test")); // the dead take no more
    }

    [Fact]
    public void ACraftedToolStaysInItsSlotWhenThePlayerDies()
    {
        _f.Pockets(Me).Add(Scrap, 7);
        _f.Pockets(Me).Add(Connector, 1);
        var slot = _f.SlotOf(Me, Connector);

        _f.Survival.Damage(Me.Peer, 1000, "test");

        Assert.Equal(new ItemStack(Connector, 1), _f.Pockets(Me).Slots[slot]);
        Assert.Equal(1, _f.Pockets(Me).Slots.Count(s => s is not null));
        Assert.Equal([new ItemStack(Seed, 2), new ItemStack(Scrap, 7)], Assert.Single(_f.Survival.Drops.Values).Items);
    }

    [Fact]
    public void ACraftedToolMustBeCarriedInSurvivalOnly()
    {
        IBuildCosts costs = _f.Survival;
        Assert.Equal("Craft a Connect Tool at a Fabricator first.", costs.CheckTool(Me, Connector));
        Assert.Null(costs.CheckTool(Me, HotbarTools.Paint)); // every player always has the others
        Assert.Null(costs.CheckTool(new ActionCaller(Me.Peer, ActionSource.Script), Connector));

        _f.Pockets(Me).Add(Connector, 1);
        Assert.Null(costs.CheckTool(Me, Connector));

        IBuildCosts creative = new SurvivalFixture(mode: GameMode.Creative).Survival;
        Assert.Null(creative.CheckTool(You, Connector));
    }

    [Fact]
    public void TheDeadCanOnlyRespawnAndOnlyAfterTheDelay()
    {
        _f.Survival.Damage(Me.Peer, 1000, "test");
        Assert.Equal("You are dead. Respawn first.", _f.Refused(Me, SurvivalActions.DropItems, new { slot = 0 }));
        Assert.True(_f.Dispatch(Me, SurvivalActions.GetVitals, new { }).Ok); // queries still work
        Assert.Equal("You can respawn in 5 s.", _f.Refused(Me, SurvivalActions.Respawn, new { }));

        _f.World.Now += 5;
        _f.Ok(Me, SurvivalActions.Respawn, new { });

        Assert.Equal((2, new Vector3(10, 0, 10)), _f.World.Teleports[^1]);
        Assert.Equal(new VitalsView(2, 100, 100, false, 0, false, GameMode.Survival, FriendlyFire: true), _f.Survival.View(Me.Peer));
    }

    [Fact]
    public void ALivingPlayerRespawningGoesHomeWithoutHealing()
    {
        _f.Survival.Damage(Me.Peer, 30, "test");
        _f.Ok(Me, SurvivalActions.Respawn, new { });
        Assert.Equal(new Vector3(10, 0, 10), _f.World.Feet[2]);
        Assert.Equal(70f, _f.Survival.View(Me.Peer)!.Health);
    }

    [Fact]
    public void PlayersRespawnOnTheBedTheySet()
    {
        var bed = _f.Add(Bed, new Vector3(0, 0, 0));
        Assert.Equal("You cannot respawn at the Block.", _f.Refused(Me, SurvivalActions.SetSpawn, new { creation = _f.Add(Block, new Vector3(1, 0, 0)), part = 1 }));
        _f.Ok(Me, SurvivalActions.SetSpawn, new { creation = bed, part = 1 });
        Assert.True(_f.Survival.View(Me.Peer)!.HasBed);

        _f.Ok(Me, SurvivalActions.Respawn, new { });
        Assert.Equal(new Vector3(0, 0.175f, 0), _f.World.Feet[2], new Vector3Comparer(1e-4f));

        _f.World.Removed(bed);
        Assert.False(_f.Survival.View(Me.Peer)!.HasBed);
        _f.Ok(Me, SurvivalActions.Respawn, new { });
        Assert.Equal(new Vector3(10, 0, 10), _f.World.Feet[2]);
    }

    [Fact]
    public void HardLandingsHurt()
    {
        _f.Survival.Landed(Me.Peer, 10f);
        Assert.Equal(100f, _f.Survival.View(Me.Peer)!.Health);
        _f.Survival.Landed(Me.Peer, 15f);
        Assert.Equal(100f - 4 * 8, _f.Survival.View(Me.Peer)!.Health);
    }

    // ---- Healing ----

    [Fact]
    public void HealingStopsAtFullHealthAndSkipsTheDead()
    {
        var changed = 0;
        _f.Survival.VitalsChanged += _ => changed++;
        _f.Survival.Damage(Me.Peer, 50, "test");
        Assert.Equal(80f, _f.Survival.Heal(Me.Peer, 30));
        Assert.Equal(100f, _f.Survival.Heal(Me.Peer, 30));
        Assert.Equal(100f, _f.Survival.HealthOf(Me.Peer));
        Assert.Equal(100f, _f.Survival.Heal(Me.Peer, float.NaN));
        Assert.Equal(3, changed);
        _f.Survival.Damage(Me.Peer, 500, "test");
        Assert.Equal(0f, _f.Survival.Heal(Me.Peer, 30));
        Assert.Equal(0f, _f.Survival.Heal(99, 30));
        Assert.Null(_f.Survival.HealthOf(99));
    }

    // ---- Farming ----

    // How crops grow and are harvested is the soil's Lua script (Mechanica.Scripting.Tests.SoilScriptTests).
    [Fact]
    public void SeedsAndWaterGoInThroughHostPrimitives()
    {
        var soil = _f.Add(Soil, new Vector3(0, 0, 1));
        var survival = _f.Survival;

        Assert.Null(survival.CheckSoil(Me, soil, 1));
        Assert.Equal("Bucket cannot be planted.", survival.CheckPlant(Me, soil, 1, Bucket));
        Assert.Throws<InvalidOperationException>(() => survival.PlantSeed(Me, soil, 1, Bucket));
        Assert.Equal(new SoilView(soil, 1, Seed, Crop, 0, 0, 60), survival.PlantSeed(Me, soil, 1, Seed));
        Assert.Equal("Something is already growing there.", survival.CheckPlant(Me, soil, 1, Seed));
        Assert.Equal(2, _f.Pockets(Me).Count(Seed)); // the seed's script uses one up, not the primitive
        Assert.Equal(3f, survival.CropValue());

        survival.Tick(100);
        Assert.Equal(0f, _f.World.FindCreation(soil)!.SoilOf(1)!.Grown); // C# grows nothing

        Assert.Throws<ArgumentOutOfRangeException>(() => survival.WaterSoil(Me, soil, 1, 0));
        Assert.Equal(30f, survival.WaterSoil(Me, soil, 1, 30).Water);
        Assert.Equal(60f, survival.WaterSoil(Me, soil, 1, 45).Water); // up to the capacity
        Assert.Equal("The soil is already full of water.", survival.CheckWater(Me, soil, 1));
        Assert.Throws<InvalidOperationException>(() => survival.WaterSoil(Me, soil, 1, 30));

        // What the soil's script stores is what players, raids and GetSoil see.
        _f.World.FindCreation(soil)!.SoilOf(1)!.Set(grown: 40, water: 40, stage: SoilState.Stages);
        Assert.True(View(soil).Ripe);
        Assert.Equal(new SoilView(soil, 1, Seed, Crop, SoilState.Stages, 40, 60, SoilWetness.Wet),
            _f.Ok<SoilView>(Me, SurvivalActions.GetSoil, new { creation = soil, part = 1 }));
        Assert.False(_f.Dispatch(Me, "Harvest", new { creation = soil, part = 1 }).Ok); // harvesting is ActivatePart now
    }

    [Fact]
    public void FarmingNeedsSoilInReach()
    {
        var far = _f.Add(Soil, new Vector3(0, 0, 30));
        var block = _f.Add(Block, new Vector3(0, 0, 1));
        Assert.StartsWith("That is ", _f.Survival.CheckPlant(Me, far, 1, Seed));
        Assert.StartsWith("That is ", _f.Survival.CheckWater(Me, far, 1));
        Assert.Null(_f.Survival.CheckSoil(new ActionCaller(Me.Peer, ActionSource.Script), far, 1)); // only players need reach
        Assert.Equal("The Block is not soil.", _f.Survival.CheckSoil(Me, block, 1));
        Assert.Equal("There is no part 1 in creation 99.", _f.Survival.CheckWater(Me, 99, 1));
        Assert.Equal("Unknown item 'test:nothing'.", _f.Survival.CheckPlant(Me, _f.Add(Soil, new Vector3(0, 0, 1)), 1, "test:nothing"));
    }

    [Fact]
    public void BucketsFillAtWater()
    {
        var settings = new SurvivalSettings { BucketItem = EmptyBucket, WaterItem = Bucket, Reach = 5, WaterSeconds = 1 };
        var f = new SurvivalFixture(settings);
        Assert.Equal("You need an empty Pail to fill.", f.Refused(Me, SurvivalActions.CollectWater, new { point = new Vec3(0, 0, 1) }));
        f.Pockets(Me).Add(EmptyBucket, 2);
        var slot = f.SlotOf(Me, EmptyBucket);
        Assert.Equal("There is no water there.", f.Refused(Me, SurvivalActions.CollectWater, new { point = new Vec3(0, 0, 1), slot }));
        f.World.WaterLevel = 0.5f;
        Assert.Equal("That slot holds no empty Pail.", f.Refused(Me, SurvivalActions.CollectWater, new { point = new Vec3(0, 0, 1), slot = slot + 1 }));

        // One of two: the full one goes elsewhere.
        var result = f.Dispatch(Me, SurvivalActions.CollectWater, new { point = new Vec3(0, 0, 1), slot });
        Assert.True(result.Ok, result.Message);
        Assert.Equal((1, 1), (f.Pockets(Me).Count(EmptyBucket), f.Pockets(Me).Count(Bucket)));
        Assert.Equal("You are still filling the last one.", f.Refused(Me, SurvivalActions.CollectWater, new { point = new Vec3(0, 0, 1), slot }));
        f.World.Now += 1;
        // The last one turns full where it is, so it stays in hand.
        f.Ok(Me, SurvivalActions.CollectWater, new { point = new Vec3(0, 0, 1), slot });
        Assert.Equal(Bucket, f.Pockets(Me)[slot]!.Item);
        Assert.Equal((0, 2), (f.Pockets(Me).Count(EmptyBucket), f.Pockets(Me).Count(Bucket)));
        f.World.Now += 1;
        f.Pockets(Me).Add(EmptyBucket, 1);
        Assert.StartsWith("That is ", f.Refused(Me, SurvivalActions.CollectWater, new { point = new Vec3(0, 0, 40) }));
    }

    [Fact]
    public void SoilNearAPointIsFoundNearestFirst()
    {
        var near = _f.Add(Soil, new Vector3(0, 0, 1));
        var further = _f.Add(Soil, new Vector3(0, 0, 2));
        _f.Add(Soil, new Vector3(0, 0, 9));
        _f.Add(Block, new Vector3(0, 0, 0.5f));
        Assert.Equal([(near, 1), (further, 1)], _f.Survival.SoilNear(new Vector3(0, 0, 0.8f), 1.5f));
        Assert.Empty(_f.Survival.SoilNear(new Vector3(0, 0, 0.8f), 0f));
    }

    [Fact]
    public void SoilAndItsCropFollowThePartAndSave()
    {
        var soil = _f.Add(Soil, new Vector3(0, 0, 1));
        var creation = _f.World.FindCreation(soil)!;
        creation.SoilOf(1)!.Load(Seed, 12.5f, 20, 1, 1234.5);

        var json = CreationJson.Write(creation);
        Assert.Contains("\"soil\": {", json);
        var read = CreationJson.Read(json, _f.Catalog, "test", [])!;
        var loaded = read.SoilOf(1)!;
        Assert.Equal((Seed, 12.5f, 20f, 1, 1234.5), (loaded.Seed, loaded.Grown, loaded.Water, loaded.Stage, loaded.Updated));
        Assert.Equal(json, CreationJson.Write(read));

        Assert.Null(creation.SoilOf(99));
        var block = _f.World.FindCreation(_f.Add(Block, Vector3.Zero))!;
        Assert.Null(block.SoilOf(1));
        var bad = json.Replace("\"part\": \"test:soil\"", "\"part\": \"test:block\"");
        var diagnostics = new List<DataDiagnostic>();
        Assert.Null(CreationJson.Read(bad, _f.Catalog, "test", diagnostics));
        Assert.Contains(diagnostics, d => d.Message.Contains("is not soil"));
    }

    // ---- Combat primitives (weapons are Lua item scripts, chunk 20b) ----

    [Fact]
    public void AHitscanHurtsThePlayerItHits()
    {
        _f.World.Ray = (_, _) => new RayHit(new Vector3(0, 1, 0), Player: You.Peer);
        var eye = _f.World.EyeOf(Me.Peer)!.Value;

        var hit = _f.Survival.Hitscan(Me, eye, new Vector3(0, 0, -2), 50, 20, 5);

        Assert.Equal(new ShotHitView("player", new Vec3(0, 1, 0), 20, Player: 3), hit);
        Assert.Equal(80f, _f.Survival.View(You.Peer)!.Health);
        Assert.Equal((new Vector3(0, 1.6f, 3), -Vector3.UnitZ), _f.World.Casts[^1]);
        _f.World.Ray = (_, _) => null;
        Assert.Null(_f.Survival.Hitscan(Me, eye, -Vector3.UnitZ, 50, 20, 5));
        Assert.Null(_f.Survival.Cast(eye, Vector3.Zero, 50, Me.Peer));
    }

    [Fact]
    public void WithFriendlyFireOffNoPlayerHurtsAnother()
    {
        var f = new SurvivalFixture(friendlyFire: false);
        // Players, external AIs and part scripts (a player's mounted gun) all count as players.
        foreach (var source in new[] { ActionSource.Player, ActionSource.External, ActionSource.Script })
        {
            var hit = f.Survival.Hit(new ActionCaller(Me.Peer, source), new RayHit(Vector3.Zero, Player: You.Peer), 20, Vector3.Zero);
            Assert.Equal(0f, hit.Damage);
        }
        f.World.Feet[3] = new Vector3(0, 0, -6f);
        Assert.Equal(0f, f.Survival.Splash(Me, new Vector3(0, 0.8f, -6), 4, 40, 0).Single(h => h.Player == You.Peer).Damage);
        Assert.Equal(100f, f.Survival.View(You.Peer)!.Health);
        // Bots (chunk 21) hurt players whatever friendly fire says.
        f.Survival.Hit(new ActionCaller(You.Peer, ActionSource.Bot), new RayHit(Vector3.Zero, Player: Me.Peer), 20, Vector3.Zero);
        Assert.Equal(80f, f.Survival.View(Me.Peer)!.Health);
        Assert.Equal("Friendly fire is off in this world: players cannot hurt each other.",
            f.Refused(Me, ToolActions.Sledge, new { player = You.Peer }));
        Assert.False(f.Survival.View(You.Peer)!.FriendlyFire);
    }

    [Fact]
    public void OnlyTheHostTurnsFriendlyFireOnAndEveryPlayerIsTold()
    {
        var f = new SurvivalFixture(friendlyFire: false);
        var host = new ActionCaller(Mechanica.Core.Creations.Creation.HostPeer, ActionSource.Player);
        var told = new List<int>();
        f.Survival.VitalsChanged += told.Add;

        Assert.Equal("Only the host can change friendly fire.", f.Refused(Me, SurvivalActions.SetFriendlyFire, new { on = true }));
        Assert.Equal("Only the host can change friendly fire.",
            f.Refused(new ActionCaller(Mechanica.Core.Creations.Creation.HostPeer, ActionSource.Script), SurvivalActions.SetFriendlyFire, new { on = true }));
        Assert.True(f.Dispatcher.Dispatch(host, SurvivalActions.SetFriendlyFire, """{ "on": true }""").Ok);

        Assert.True(f.Survival.FriendlyFire);
        Assert.Equal(f.Survival.Players.ToList(), told);
        Assert.True(f.Survival.View(You.Peer)!.FriendlyFire);
        Assert.Equal(20f, f.Survival.Hit(Me, new RayHit(Vector3.Zero, Player: You.Peer), 20, Vector3.Zero).Damage);
        Assert.Null(f.Survival.CheckStrikePlayer(Me, You.Peer));
    }

    [Fact]
    public void APartScriptsSplashDoesNotHurtTheHostAsItsOwn()
    {
        var f = new SurvivalFixture(friendlyFire: false);
        var host = Mechanica.Core.Creations.Creation.HostPeer;
        f.Survival.Join(host);
        f.World.Feet[host] = new Vector3(0, 0, -6f);
        f.World.Feet[Me.Peer] = new Vector3(0, 0, 6f);
        Assert.Equal(0f, f.Survival.Splash(new ActionCaller(host, ActionSource.Script), new Vector3(0, 0.8f, -6), 4, 40, 0).Single(h => h.Player == host).Damage);
        // A player's own splash still hurts them.
        Assert.True(f.Survival.Splash(Me, new Vector3(0, 0.8f, 6), 4, 40, 0).Single(h => h.Player == Me.Peer).Damage > 0);
    }

    [Fact]
    public void HitsBreakPartsAndPushTheirBody()
    {
        var block = _f.Add(Bed, new Vector3(0, 0, 0)); // durability 3: 30 health
        var at = new RayHit(new Vector3(0, 0.1f, 0.2f), Creation: block, Part: 1);

        Assert.Equal("part", _f.Survival.Hit(Me, at, 20, new Vector3(0, 0, -5)).Kind);
        Assert.True(_f.World.Creations.ContainsKey(block));
        Assert.Equal([new Vector3(0, 0, -5)], _f.World.Pushes);
        _f.Survival.Hit(Me, at, 20, new Vector3(0, 0, -5));
        Assert.False(_f.World.Creations.ContainsKey(block));
        Assert.Equal("ground", _f.Survival.Hit(Me, new RayHit(Vector3.Zero), 20, Vector3.Zero).Kind);
    }

    [Fact]
    public void PelletsFanOutEvenlyAndTheSameEveryTime()
    {
        var pellets = SurvivalActions.Pellets(-Vector3.UnitZ, 5, 10);
        Assert.Equal(5, pellets.Count);
        Assert.Equal(-Vector3.UnitZ, pellets[0]);
        foreach (var p in pellets.Skip(1))
            Assert.Equal(10f, MathF.Acos(Vector3.Dot(p, -Vector3.UnitZ)) * 180 / MathF.PI, 3);
        Assert.Equal(pellets, SurvivalActions.Pellets(-Vector3.UnitZ, 5, 10));
        var sideways = pellets.Skip(1).Aggregate(Vector3.Zero, (s, p) => s + p);
        Assert.Equal(0f, sideways.X, 5);
        Assert.Equal(0f, sideways.Y, 5);
        Assert.Equal(3, SurvivalActions.Pellets(-Vector3.UnitZ, 3, 0).Count);
    }

    [Fact]
    public void SplashHurtsEverythingNearbyLessFurtherOut()
    {
        _f.World.Feet[3] = new Vector3(0, 0, -6f); // centre at (0, 0.8, -6): 2 m from the blast
        var block = _f.Add(Block, new Vector3(-0.125f, -0.125f, -8.125f)); // centre 0 0 -8: 2.15 m off
        var tree = new WorldObjectId(0, 1, 1);
        _f.World.Objects[tree] = new WorldObjectPlacement(tree, new WorldObjectDefinition { Id = "tree", Health = 30, Drops = [new ObjectDrop { Item = Scrap, Count = 3 }] },
            new Vector3(0, 0, -8), 0, 1);

        var hits = _f.Survival.Splash(Me, new Vector3(0, 0.8f, -8), 4, 40, 0);

        Assert.Equal(80f, _f.Survival.View(You.Peer)!.Health, 2); // 40 × (1 - 2/4)
        Assert.Equal(100f, _f.Survival.View(Me.Peer)!.Health); // 8 m away
        Assert.Contains(hits, h => h is { Kind: "part", Creation: var c } && c == block);
        Assert.True(_f.World.State.IsDestroyed(tree)); // 0.8 m off: 32 damage
        Assert.Contains(_f.Survival.Drops.Values, d => d.Items.SequenceEqual([new ItemStack(Scrap, 3)]));
        Assert.Empty(_f.Survival.Splash(Me, Vector3.Zero, 0, 40, 0));
    }

    [Fact]
    public void CreativeHitsDoNotHurtPlayers()
    {
        var f = new SurvivalFixture(mode: GameMode.Creative);
        f.Survival.Hit(Me, new RayHit(Vector3.Zero, Player: You.Peer), 20, Vector3.Zero);
        f.Survival.Splash(Me, f.Survival.PlayerCentre(Me.Peer)!.Value, 4, 40, 0);
        Assert.Equal(100f, f.Survival.View(You.Peer)!.Health);
        Assert.Equal(100f, f.Survival.View(Me.Peer)!.Health);
    }

    [Fact]
    public void ProjectilesFlyAlongTheirArcUntilTheyHitOrTimeOut()
    {
        var launched = new List<ProjectileView>();
        var landed = new List<ProjectileLanding>();
        _f.Survival.ProjectileLaunched += launched.Add;
        _f.Survival.ProjectileLanded += landed.Add;
        var gravity = new Vector3(0, -10, 0);
        // The ground is at y = 0.
        _f.World.Ray = (from, direction) => direction.Y < 0 && from.Y / -direction.Y <= _f.World.LastRange ? new RayHit(from + direction * (from.Y / -direction.Y)) : null;

        var falling = _f.Survival.Launch(Me.Peer, new Vector3(0, 5, 0), new Vector3(2, 0, 0), gravity, 10);
        var flat = _f.Survival.Launch(Me.Peer, new Vector3(0, 5, 0), new Vector3(1, 0, 0), Vector3.Zero, 0.5f);
        Assert.Equal([falling, flat], launched);
        Assert.Equal(2, _f.Survival.Projectiles.Count());

        for (var i = 0; i < 60 && landed.Count < 2; i++)
            _f.Survival.Tick(1 / 30f);

        // Flat: no hit, so it ends where it got to after its lifetime.
        Assert.Equal(flat.Id, landed[0].Id);
        Assert.Null(landed[0].Hit);
        Assert.Equal(0.5f, landed[0].Point.X, 3);
        // Falling from 5 m at -10 m/s² lands after 1 s, 2 m along.
        Assert.Equal(falling.Id, landed[1].Id);
        Assert.Equal(Me.Peer, landed[1].Owner);
        Assert.NotNull(landed[1].Hit);
        Assert.Equal(2f, landed[1].Point.X, 2);
        Assert.Equal(0f, landed[1].Point.Y, 3);
        Assert.Empty(_f.Survival.Projectiles);
    }

    // ---- Sledgehammer on players and world objects ----

    [Fact]
    public void TheSledgehammerHitsPlayersAndBreaksWorldObjects()
    {
        _f.Ok(Me, ToolActions.Sledge, new { player = You.Peer });
        Assert.Equal(80f, _f.Survival.View(You.Peer)!.Health);
        Assert.Equal("You cannot hit yourself.", _f.Refused(Me, ToolActions.Sledge, new { player = Me.Peer }));
        Assert.Equal("Hit one thing: a creation and part, a player, an object or a bot.",
            _f.Refused(Me, ToolActions.Sledge, new { player = You.Peer, @object = "1.2.3" }));

        var rock = new WorldObjectId(2, 0, 0);
        _f.World.Objects[rock] = new WorldObjectPlacement(rock, new WorldObjectDefinition { Id = "rock_node", Health = 15, Drops = [new ObjectDrop { Item = Scrap, Count = 2 }] },
            new Vector3(0, 0, 2), 0, 1);
        var bush = new WorldObjectId(3, 0, 0);
        _f.World.Objects[bush] = new WorldObjectPlacement(bush, new WorldObjectDefinition { Id = "boulder" }, new Vector3(0, 0, 2), 0, 1);
        Assert.Equal("There is no object 3.0.0 that can break.", _f.Refused(Me, ToolActions.Sledge, new { @object = "3.0.0" }));

        _f.World.Now += 1;
        Assert.Equal(5f, _f.Ok<SledgeResult>(Me, ToolActions.Sledge, new { @object = "2.0.0" }).Health);
        _f.World.Now += 1;
        Assert.True(_f.Ok<SledgeResult>(Me, ToolActions.Sledge, new { @object = "2.0.0" }).Broken);
        Assert.True(_f.World.State.IsDestroyed(rock));
        var drop = Assert.Single(_f.Survival.Drops.Values);
        Assert.Equal([new ItemStack(Scrap, 2)], drop.Items);
    }

    [Fact]
    public void AHitKnocksThePlayerBackAlongTheSwing()
    {
        // Me stands at the origin looking at You, 3 m in front: 5000 N·s on an 80 kg body is far past the 4 m/s cap.
        _f.Ok(Me, ToolActions.Sledge, new { player = You.Peer });
        var (peer, velocity) = Assert.Single(_f.World.Knocks);
        Assert.Equal(You.Peer, peer);
        var sledge = new SledgeSettings();
        var flat = new Vector3(velocity.X, 0, velocity.Z);
        Assert.Equal(sledge.MaxKnockbackSpeed, flat.Length(), 3);
        Assert.Equal(sledge.KnockbackLift, velocity.Y, 3);
        var away = Vector3.Normalize(new Vector3(_f.World.Feet[You.Peer].X - _f.World.Feet[Me.Peer].X, 0, _f.World.Feet[You.Peer].Z - _f.World.Feet[Me.Peer].Z));
        Assert.True(Vector3.Dot(Vector3.Normalize(flat), away) > 0.99f, $"pushed away from the swing ({flat})");
    }

    [Fact]
    public void EachWorldObjectTakesItsOwnGatheringDamage()
    {
        // Stone takes 25 a hit (Chase's sledgehammer spec); an object without sledge_damage takes sledge.object_damage.
        var stone = new WorldObjectId(2, 0, 0);
        _f.World.Objects[stone] = new WorldObjectPlacement(stone, new WorldObjectDefinition { Id = "rock_node", Health = 100, SledgeDamage = 25 }, new Vector3(0, 0, 2), 0, 1);
        var log = new WorldObjectId(4, 0, 0);
        _f.World.Objects[log] = new WorldObjectPlacement(log, new WorldObjectDefinition { Id = "fallen_log", Health = 100 }, new Vector3(0, 0, 2), 0, 1);
        Assert.Equal(75f, _f.Ok<SledgeResult>(Me, ToolActions.Sledge, new { @object = "2.0.0" }).Health);
        _f.World.Now += 1;
        Assert.Equal(100f - new SledgeSettings().ObjectDamage, _f.Ok<SledgeResult>(Me, ToolActions.Sledge, new { @object = "4.0.0" }).Health);
    }

    [Fact]
    public void TheSwingTakesSixTenthsOfASecond()
    {
        _f.Ok(Me, ToolActions.Sledge, new { player = You.Peer });
        _f.World.Now += 0.3;
        Assert.Equal("The Sledgehammer is still swinging.", _f.Refused(Me, ToolActions.Sledge, new { player = You.Peer }));
        _f.World.Now += 0.3;
        _f.Ok(Me, ToolActions.Sledge, new { player = You.Peer });
    }

    [Fact]
    public void ABrokenContainerSpillsWhatItHeld()
    {
        var chest = _f.Add(Chest, new Vector3(0, 0, 1));
        _f.World.FindCreation(chest)!.InventoryOf(1)!.Add(Scrap, 9);
        foreach (var _ in Enumerable.Range(0, 3))
        {
            _f.Ok(Me, ToolActions.Sledge, new { creation = chest, part = 1 });
            _f.World.Now += 1;
        }
        Assert.False(_f.World.Creations.ContainsKey(chest));
        Assert.Equal([new ItemStack(Scrap, 9)], Assert.Single(_f.Survival.Drops.Values).Items);
    }

    // ---- Items on the ground ----

    [Fact]
    public void ItemsAreDroppedAndPickedUpAgain()
    {
        _f.Pockets(Me).Add(Scrap, 30);
        var slot = _f.SlotOf(Me, Scrap);
        Assert.Equal("count must be 1 to 30 (got 31).", _f.Refused(Me, SurvivalActions.DropItems, new { slot, count = 31 }));
        var dropped = _f.Ok<DropItemsResult>(Me, SurvivalActions.DropItems, new { slot, count = 10 });
        Assert.Equal(20, _f.Pockets(Me).Count(Scrap));
        Assert.Equal(new Vec3(0, 0, 2), _f.Survival.Drops[dropped.Drop].Position); // 1 m ahead (-z) on the ground
        Assert.False(_f.Survival.Drops[dropped.Drop].KnockedOut);

        Assert.StartsWith("That is 4.0 m away", _f.Refused(You, SurvivalActions.PickUp, new { drop = dropped.Drop }));
        _f.World.Feet[3] = new Vector3(0, 0, 2);
        foreach (var s in Enumerable.Range(0, 3))
            _f.Pockets(You).SetSlot(s, new ItemStack(Crop, 20));
        _f.Pockets(You).SetSlot(3, new ItemStack(Scrap, 45));
        Assert.Equal(new PickUpResult(5, 5), _f.Ok<PickUpResult>(You, SurvivalActions.PickUp, new { drop = dropped.Drop }));
        Assert.Equal([new ItemStack(Scrap, 5)], _f.Survival.Drops[dropped.Drop].Items);
        Assert.Equal("Make room in your inventory first.", _f.Refused(You, SurvivalActions.PickUp, new { drop = dropped.Drop }));

        Assert.Equal(new PickUpResult(5, 0), _f.Ok<PickUpResult>(Me, SurvivalActions.PickUp, new { drop = dropped.Drop }));
        Assert.Empty(_f.Survival.Drops);
        Assert.Contains("nothing to pick up", _f.Refused(Me, SurvivalActions.PickUp, new { drop = dropped.Drop }));
    }

    [Fact]
    public void OldPilesExpireAndTheOldestMakeWay()
    {
        var removed = new List<int>();
        _f.Survival.DropRemoved += removed.Add;
        for (var i = 0; i < 9; i++)
            _f.Survival.AddDrop(Vector3.Zero, [new ItemStack(Scrap, 1)]);
        Assert.Equal([1], removed);
        Assert.Equal(8, _f.Survival.Drops.Count);
        Assert.Null(_f.Survival.AddDrop(Vector3.Zero, []));

        _f.World.Now += 100;
        _f.Survival.Tick(0.1f);
        Assert.Empty(_f.Survival.Drops);
    }

    // ---- Building costs ----

    [Fact]
    public void InSurvivalPartsComeFromTheInventoryAndGoBack()
    {
        Assert.Equal("You have no Block to place.",
            _f.Refused(Me, BuildActions.PlacePart, new { part = Block, cell = new[] { 0, 0, 0 }, position = new Vec3(0, 0, 1) }));
        _f.Pockets(Me).Add(Block, 1);
        var placed = _f.Ok<PlacePartResult>(Me, BuildActions.PlacePart, new { part = Block, cell = new[] { 0, 0, 0 }, position = new Vec3(0, 0, 1) });
        Assert.Equal(0, _f.Pockets(Me).Count(Block));

        _f.Ok(Me, BuildActions.RemovePart, new { creation = placed.Creation, part = placed.Part });
        Assert.Equal(1, _f.Pockets(Me).Count(Block));
    }

    [Fact]
    public void APartPlacedFromAnotherItemUsesItUpAndGivesItBack()
    {
        // A mound is placed from a bag (a bare id is the mound's own mod) and is picked up as one.
        _f.Catalog.Add("test:bag", _f.Catalog.FindDefinition(Block)! with { Id = "bag", Name = "Bag", GroundPart = "mound" });
        _f.Catalog.Add("test:mound", _f.Catalog.FindDefinition(Block)! with { Id = "mound", Name = "Mound", PlacedFrom = "bag" });
        Assert.Equal("test:mound", PartItems.GroundPartFor(_f.Catalog, "test:bag"));
        Assert.Equal("test:bag", PartItems.ItemFor(_f.Catalog, "test:mound"));
        Assert.Equal("test:block", PartItems.ItemFor(_f.Catalog, "test:block"));
        Assert.False(_f.Catalog.FindDefinition("test:mound")!.IsItem);

        var mound = new { part = "test:mound", cell = new[] { 0, 0, 0 }, position = new Vec3(0, 0, 1) };
        Assert.Equal("You have no Bag to place.", _f.Refused(Me, BuildActions.PlacePart, mound));
        _f.Pockets(Me).Add("test:bag", 1);
        var placed = _f.Ok<PlacePartResult>(Me, BuildActions.PlacePart, mound);
        Assert.Equal(0, _f.Pockets(Me).Count("test:bag"));

        _f.Ok(Me, BuildActions.RemovePart, new { creation = placed.Creation, part = placed.Part });
        Assert.Equal((1, 0), (_f.Pockets(Me).Count("test:bag"), _f.Pockets(Me).Count("test:mound")));

        // A mound still carried from before it was placed from a bag is used up too.
        _f.Pockets(Me).Remove("test:bag", 1);
        _f.Pockets(Me).Add("test:mound", 1);
        _f.Ok<PlacePartResult>(Me, BuildActions.PlacePart, mound with { position = new Vec3(3, 0, 1) });
        Assert.Equal(0, _f.Pockets(Me).Count("test:mound"));
    }

    [Fact]
    public void ARemovedPartThatDoesNotFitIsDropped()
    {
        var block = _f.Add(Block, new Vector3(0, 0, 1));
        foreach (var s in Enumerable.Range(0, 4))
            _f.Pockets(Me).SetSlot(s, new ItemStack(Scrap, 50));
        _f.Ok(Me, BuildActions.RemovePart, new { creation = block, part = 1 });
        Assert.Equal([new ItemStack(Block, 1)], Assert.Single(_f.Survival.Drops.Values).Items);
    }

    [Fact]
    public void InCreativeBuildingIsFree()
    {
        var f = new SurvivalFixture(mode: GameMode.Creative);
        var placed = f.Ok<PlacePartResult>(Me, BuildActions.PlacePart, new { part = Block, cell = new[] { 0, 0, 0 }, position = new Vec3(0, 0, 1) });
        f.Ok(Me, BuildActions.RemovePart, new { creation = placed.Creation, part = placed.Part });
        Assert.Equal(0, f.Pockets(Me).Count(Block));
    }

    // ---- Impacts ----

    [Fact]
    public void ACrashDamagesTheLeadingParts()
    {
        var creation = new Creation(_f.World.NextCreationId(), _f.Catalog);
        Assert.True(creation.TryPlacePart(Block, new GridCell(0, 0, 0), default, out _, out _));
        Assert.True(creation.TryPlacePart(Block, new GridCell(0, 1, 0), default, out _, out _));
        creation.SetAllBodyPoses(Pose.Identity);
        _f.World.Added(creation);

        Assert.Empty(_f.Survival.Impact(creation, 0, new Vector3(0, 7, 0))); // under impact.min_speed
        Assert.Empty(_f.Survival.Impact(creation, 0, new Vector3(0, 10, 0))); // under 8 + durability 2
        Assert.Equal([1], _f.Survival.Impact(creation, 0, new Vector3(0, 12, 0))); // pushed up: the bottom part hit
        Assert.Equal(10f, creation.PartDamage(1));
        Assert.Equal(0f, creation.PartDamage(2));

        var creative = new SurvivalFixture(mode: GameMode.Creative);
        Assert.Empty(creative.Survival.Impact(creation, 0, new Vector3(0, 50, 0)));
    }

    private SoilView View(int creation) => _f.Survival.SoilViewOf(_f.World.FindCreation(creation)!, 1);

    private sealed class Vector3Comparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;

        public int GetHashCode(Vector3 v) => 0;
    }
}
