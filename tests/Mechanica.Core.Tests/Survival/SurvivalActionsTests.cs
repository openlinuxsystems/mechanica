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
        Assert.Equal(new VitalsView(Me.Peer, 100, 100, false, 0, false, GameMode.Survival), vitals);
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
        Assert.Equal(0f, _f.Survival.Damage(Me.Peer, 10, "test")); // the dead take no more
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
        Assert.Equal(new VitalsView(2, 100, 100, false, 0, false, GameMode.Survival), _f.Survival.View(Me.Peer));
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

    [Fact]
    public void ACropGrowsOnlyWhileWateredAndIsHarvestedRipe()
    {
        var soil = _f.Add(Soil, new Vector3(0, 0, 1));
        _f.Pockets(Me).Add(Bucket, 3);
        var seed = _f.SlotOf(Me, Seed);
        var bucket = _f.SlotOf(Me, Bucket);

        Assert.Equal("Bucket cannot be planted.", _f.Refused(Me, SurvivalActions.Plant, new { creation = soil, part = 1, slot = bucket }));
        var planted = _f.Ok<SoilView>(Me, SurvivalActions.Plant, new { creation = soil, part = 1, slot = seed });
        Assert.Equal(new SoilView(soil, 1, Seed, Crop, 0, 0, 60), planted);
        Assert.Equal("Something is already growing there.", _f.Refused(Me, SurvivalActions.Plant, new { creation = soil, part = 1, slot = seed }));

        _f.Survival.Tick(100);
        Assert.Equal(0, View(soil).Stage); // dry soil: nothing grows

        Assert.Equal(30f, _f.Ok<SoilView>(Me, SurvivalActions.Water, new { creation = soil, part = 1, slot = bucket }).Water);
        Assert.Equal(60f, _f.Ok<SoilView>(Me, SurvivalActions.Water, new { creation = soil, part = 1, slot = bucket }).Water);
        Assert.Equal("The soil is already full of water.", _f.Refused(Me, SurvivalActions.Water, new { creation = soil, part = 1, slot = bucket }));

        _f.Survival.Tick(10); // growth 2: 20 of 40 s
        Assert.Equal(2, View(soil).Stage);
        Assert.Equal(50f, View(soil).Water);
        Assert.Equal("The Crop is not ripe yet.", _f.Refused(Me, SurvivalActions.Harvest, new { creation = soil, part = 1 }));
        Assert.Equal(3f, _f.Survival.CropValue());

        _f.Survival.Tick(100); // ripe after 10 more s; the rest of the water stays
        Assert.True(View(soil).Ripe);
        Assert.Equal(40f, View(soil).Water);
        var harvest = _f.Ok<HarvestResult>(Me, SurvivalActions.Harvest, new { creation = soil, part = 1 });
        Assert.Equal([new ItemAmount(Crop, 2), new ItemAmount(Seed, 1)], harvest.Items);
        Assert.Equal(2, _f.Pockets(Me).Count(Seed)); // 2 - 1 planted + 1 back
        Assert.Null(View(soil).Seed);
        Assert.Equal(0f, _f.Survival.CropValue());
        Assert.Equal("Nothing is growing there.", _f.Refused(Me, SurvivalActions.Harvest, new { creation = soil, part = 1 }));
    }

    [Fact]
    public void FarmingNeedsSoilInReach()
    {
        var far = _f.Add(Soil, new Vector3(0, 0, 30));
        var block = _f.Add(Block, new Vector3(0, 0, 1));
        var seed = _f.SlotOf(Me, Seed);
        Assert.StartsWith("That is ", _f.Refused(Me, SurvivalActions.Plant, new { creation = far, part = 1, slot = seed }));
        Assert.Equal("The Block is not soil.", _f.Refused(Me, SurvivalActions.Plant, new { creation = block, part = 1, slot = seed }));
        Assert.Equal("There is no part 1 in creation 99.", _f.Refused(Me, SurvivalActions.Plant, new { creation = 99, part = 1, slot = seed }));
    }

    [Fact]
    public void AHarvestNeedsRoom()
    {
        var soil = _f.Add(Soil, new Vector3(0, 0, 1));
        _f.Ok(Me, SurvivalActions.Plant, new { creation = soil, part = 1, slot = _f.SlotOf(Me, Seed) });
        _f.World.FindCreation(soil)!.SoilOf(1)!.Load(Seed, 40, 0);
        foreach (var slot in Enumerable.Range(0, 4))
            _f.Pockets(Me).SetSlot(slot, new ItemStack(Scrap, 50));
        Assert.Equal("Make room in your inventory first.", _f.Refused(Me, SurvivalActions.Harvest, new { creation = soil, part = 1 }));
    }

    [Fact]
    public void BucketsFillAtWater()
    {
        var settings = new SurvivalSettings { WaterItem = Bucket, Reach = 5, WaterSeconds = 1 };
        var f = new SurvivalFixture(settings);
        Assert.Equal("There is no water there.", f.Refused(Me, SurvivalActions.CollectWater, new { point = new Vec3(0, 0, 1) }));
        f.World.WaterLevel = 0.5f;
        f.Ok(Me, SurvivalActions.CollectWater, new { point = new Vec3(0, 0, 1) });
        Assert.Equal("You are still filling the last one.", f.Refused(Me, SurvivalActions.CollectWater, new { point = new Vec3(0, 0, 1) }));
        f.World.Now += 1;
        f.Ok(Me, SurvivalActions.CollectWater, new { point = new Vec3(0, 0, 1) });
        Assert.Equal(2, f.Pockets(Me).Count(Bucket));
        Assert.StartsWith("That is ", f.Refused(Me, SurvivalActions.CollectWater, new { point = new Vec3(0, 0, 40) }));
    }

    [Fact]
    public void SoilAndItsCropFollowThePartAndSave()
    {
        var soil = _f.Add(Soil, new Vector3(0, 0, 1));
        var creation = _f.World.FindCreation(soil)!;
        creation.SoilOf(1)!.Load(Seed, 12.5f, 20);

        var json = CreationJson.Write(creation);
        Assert.Contains("\"soil\": {", json);
        var read = CreationJson.Read(json, _f.Catalog, "test", [])!;
        Assert.Equal((Seed, 12.5f, 20f), (read.SoilOf(1)!.Seed, read.SoilOf(1)!.Grown, read.SoilOf(1)!.Water));
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
    public void WithPvpOffHitsDoNotHurtPlayers()
    {
        var f = new SurvivalFixture(new SurvivalSettings { Pvp = false });
        var hit = f.Survival.Hit(Me, new RayHit(Vector3.Zero, Player: You.Peer), 20, Vector3.Zero);
        Assert.Equal(0f, hit.Damage);
        Assert.Equal(100f, f.Survival.View(You.Peer)!.Health);
        // Bots (chunk 21) hurt players whatever PvP says.
        f.Survival.Hit(new ActionCaller(You.Peer, ActionSource.Bot), new RayHit(Vector3.Zero, Player: Me.Peer), 20, Vector3.Zero);
        Assert.Equal(80f, f.Survival.View(Me.Peer)!.Health);
        Assert.Equal("Players cannot hurt each other in this world.",
            f.Refused(Me, ToolActions.Sledge, new { player = You.Peer }));
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
        Assert.Equal(85f, _f.Survival.View(You.Peer)!.Health);
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
