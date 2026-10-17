using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.World;
using static Mechanica.Core.Tests.Creations.TestParts;

namespace Mechanica.Core.Tests.Building;

public class ToolActionsTests
{
    private static readonly ActionCaller Player = new(2, ActionSource.Player);
    private static readonly GridCell Up = new(0, 1, 0);

    private readonly FakeWorld _world = new();
    private readonly ActionDispatcher _dispatcher = new();
    private readonly ToolActions _tools;
    private readonly BuildSettings _settings = new()
    {
        Reach = 6f,
        MaxPartsPerCreation = 10,
        Lift = new LiftSettings { PlatformSize = 2f, BaseHeight = 0.25f, LevelHeight = 0.25f, MaxLevel = 4 },
        Paint = new PaintSettings { MaxPartsPerAction = 3 },
        Sledge = new SledgeSettings { Damage = 10f, HealthPerDurability = 10f, SwingSeconds = 0.5f, Impulse = 20f },
    };

    public ToolActionsTests()
    {
        _tools = new ToolActions(_settings, _world);
        _tools.Register(_dispatcher);
        _world.Eyes[2] = new Vector3(0, 1.6f, 3);
    }

    // ---- Lift ----

    [Fact]
    public void PlacingALiftSnapsItToTheGrid()
    {
        var result = Dispatch(ToolActions.PlaceLift, new { position = new[] { 1.1f, 0f, -0.4f } });

        Assert.True(result.Ok, result.ToString());
        var lift = _tools.LiftOf(2)!;
        Assert.Equal(new Vec3(1f, 0f, -0.5f), lift.Position);
        Assert.Null(lift.Creation);
        Assert.Equal(["lift 2 at (1, 0, -0.5) level 0"], _world.Log);
    }

    [Fact]
    public void PickingUpSeatsTheCreationUprightOnThePlatform()
    {
        var id = Add(Tilted(Car()));

        var result = Dispatch(ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 0f }, creation = id });

        Assert.True(result.Ok, result.ToString());
        var creation = _world.Creations[id];
        Assert.True(creation.OnLift);
        var pose = creation.BodyPose(0);
        // Upright, squared to the grid: its grid +Y points up.
        Assert.True(Vector3.Distance(Vector3.UnitY, Vector3.Transform(Vector3.UnitY, pose.Rotation)) < 1e-4f, $"{pose.Rotation}");
        var box = CreationBounds.World(creation);
        Assert.Equal(0.25f, box.Min.Y, 1e-4f);
        Assert.Equal(0f, (box.Min.X + box.Max.X) / 2, 1e-4f);
        Assert.Equal(0f, (box.Min.Z + box.Max.Z) / 2, 1e-4f);
        Assert.Equal(["moved 1 on lift", "lift 2 at (0, 0, 0) level 0 holding 1"], _world.Log);
    }

    [Fact]
    public void RaisingAndTurningMoveTheCreationWithThePlatform()
    {
        var id = Add(Car());
        Dispatch(ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 0f }, creation = id });
        var creation = _world.Creations[id];

        Assert.True(Dispatch(ToolActions.SetLiftLevel, new { level = 4 }).Ok);
        Assert.Equal(1.25f, CreationBounds.World(creation).Min.Y, 1e-4f);

        var before = CreationBounds.World(creation);
        Assert.True(Dispatch(ToolActions.TurnLift, new { turns = 1 }).Ok);
        var after = CreationBounds.World(creation);
        // The 3-long car now lies across the other axis, still centred and resting on the platform.
        Assert.Equal(before.Max.X - before.Min.X, after.Max.Z - after.Min.Z, 1e-4f);
        Assert.Equal(1.25f, after.Min.Y, 1e-4f);
        Assert.Equal(0f, (after.Min.X + after.Max.X) / 2, 1e-4f);
    }

    [Fact]
    public void MovingTheLiftCarriesItsCreation()
    {
        var id = Add(Car());
        Dispatch(ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 0f }, creation = id });

        Assert.True(Dispatch(ToolActions.PlaceLift, new { position = new[] { 1f, 0f, 1f } }).Ok);

        Assert.Equal(id, _tools.LiftOf(2)!.Creation);
        var box = CreationBounds.World(_world.Creations[id]);
        Assert.Equal(1f, (box.Min.X + box.Max.X) / 2, 1e-4f);
    }

    [Fact]
    public void LiftingAGroundedCreationFreesItSoItDropsWhenLetGo()
    {
        var id = Add(Car());
        _world.Creations[id].SetAnchored(_world.Creations[id].Parts.First().Id, true);

        Assert.True(Dispatch(ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 0f }, creation = id }).Ok);
        Assert.True(Dispatch(ToolActions.RemoveLift, new { }).Ok);

        Assert.False(_world.Creations[id].IsGrounded);
        Assert.False(_world.Creations[id].OnLift);
    }

    [Fact]
    public void RemovingTheLiftDropsItsCreation()
    {
        var id = Add(Car());
        Dispatch(ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 0f }, creation = id });
        _world.Log.Clear();

        Assert.True(Dispatch(ToolActions.RemoveLift, new { }).Ok);

        Assert.False(_world.Creations[id].OnLift);
        Assert.Null(_tools.LiftOf(2));
        Assert.Equal(["moved 1", "lift 2 removed"], _world.Log);
    }

    [Fact]
    public void PickingUpAnotherCreationDropsTheFirst()
    {
        var first = Add(Car());
        var second = Add(Car(new Vector3(2, 0.125f, 0)));
        Dispatch(ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 0f }, creation = first });

        Assert.True(Dispatch(ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 0f }, creation = second }).Ok);

        Assert.False(_world.Creations[first].OnLift);
        Assert.True(_world.Creations[second].OnLift);
    }

    [Fact]
    public void LiftRequestsAreChecked()
    {
        var id = Add(Car());
        Assert.Contains("no lift", Dispatch(ToolActions.SetLiftLevel, new { level = 1 }).Message);
        Assert.Contains("no lift", Dispatch(ToolActions.RemoveLift, new { }).Message);
        Assert.Contains("you can reach", Dispatch(ToolActions.PlaceLift, new { position = new[] { 30f, 0f, 0f } }).Message);
        Assert.Contains("no creation 9", Dispatch(ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 0f }, creation = 9 }).Message);

        Dispatch(ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 0f } });
        Assert.Contains("holds no creation", Dispatch(ToolActions.TurnLift, new { turns = 1 }).Message);
        Assert.Contains("level must be 0 to 4", Dispatch(ToolActions.SetLiftLevel, new { level = 5 }).Message);

        _world.Eyes[3] = _world.Eyes[2];
        Dispatch(ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 0f }, creation = id });
        var other = _dispatcher.Dispatch(new ActionCaller(3, ActionSource.Player), ToolActions.PlaceLift,
            JsonSerializer.Serialize(new { position = new[] { 1f, 0f, 0f }, creation = id }));
        Assert.Contains("another player's lift", other.Message);

        _world.Blocked = "A player is in the way.";
        Assert.Equal("A player is in the way.", Dispatch(ToolActions.SetLiftLevel, new { level = 2 }).Message);
        Assert.Equal(0, _tools.LiftOf(2)!.Level);
    }

    [Fact]
    public void ReachToTheLiftCountsTheWholeColumn()
    {
        Dispatch(ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 0f } });
        Assert.True(Dispatch(ToolActions.SetLiftLevel, new { level = 4 }).Ok);

        // Out of reach of the base (6.3 m) but within reach of the raised deck (5.8 m), as a player riding it is.
        _world.Eyes[2] = new Vector3(0, 3f, 5.5f);
        Assert.True(Dispatch(ToolActions.SetLiftLevel, new { level = 3 }).Ok);
        _world.Eyes[2] = new Vector3(0, 3f, 7f);
        Assert.Contains("you can reach", Dispatch(ToolActions.SetLiftLevel, new { level = 2 }).Message);
    }

    [Fact]
    public void LiftClearanceIgnoresTheCarriedCreationAndOwnLift()
    {
        var id = Add(Car());
        Dispatch(ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 0f }, creation = id });

        Assert.Equal([id], _world.LastIgnore);
        Assert.Equal(2, _world.LastIgnoreLift);
    }

    // ---- Weld ----

    [Fact]
    public void WeldingMovesTheSourceOntoTheTargetAndMerges()
    {
        var target = Add(Car());
        var source = Add(Single(Capped, new Vector3(5, 3, 0)));
        _world.Log.Clear();

        var result = Dispatch(ToolActions.Weld, new { source, target, offset = new[] { 0, 1, 0 } });

        Assert.True(result.Ok, result.ToString());
        Assert.Equal(["removed 2", "changed 1"], _world.Log);
        var merged = _world.Creations[target];
        Assert.Equal(4, merged.Parts.Count);
        Assert.Single(merged.Bodies);
        // The welded part sits in the target's frame now.
        Assert.Equal(merged.BodyPose(0), Pose(merged, 4));
        Assert.Equal(target, result.Value!.Value.GetProperty("creation").GetInt32());
    }

    [Fact]
    public void WeldingKeepsTheSourceJointsInTheirBuiltShape()
    {
        var target = Add(Car());
        var source = Add(id =>
        {
            var tower = Tower(id, new Vector3(0, 5, 0));
            tower.SetBodyPose(1, new Pose(new Vector3(9, 9, 9), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f)));
            return tower;
        });

        Assert.True(Dispatch(ToolActions.Weld, new { source, target, offset = new[] { 2, 1, 0 } }).Ok);

        var merged = _world.Creations[target];
        Assert.Equal(2, merged.Bodies.Count);
        Assert.Equal(merged.BodyPose(0), merged.BodyPose(1));
    }

    [Fact]
    public void WeldRequestsAreChecked()
    {
        var target = Add(Car());
        var source = Add(Single(Block, new Vector3(0, 2, 0)));

        Assert.Contains("itself", Dispatch(ToolActions.Weld, new { source = target, target, offset = new[] { 0, 1, 0 } }).Message);
        Assert.Contains("no creation 9", Dispatch(ToolActions.Weld, new { source = 9, target, offset = new[] { 0, 1, 0 } }).Message);
        Assert.Contains("overlap", Dispatch(ToolActions.Weld, new { source, target, offset = new[] { 0, 0, 0 } }).Message);
        Assert.Contains("does not attach", Dispatch(ToolActions.Weld, new { source, target, offset = new[] { 0, 3, 0 } }).Message);
        _world.Eyes[2] = new Vector3(0, 0, 40);
        Assert.Contains("you can reach", Dispatch(ToolActions.Weld, new { source, target, offset = new[] { 0, 1, 0 } }).Message);
        _world.Eyes[2] = new Vector3(0, 1.6f, 3);
        _world.Blocked = "Creation 7 is in the way.";
        Assert.Equal("Creation 7 is in the way.", Dispatch(ToolActions.Weld, new { source, target, offset = new[] { 0, 1, 0 } }).Message);
        Assert.Equal([source, target], _world.LastIgnore);
        _world.Blocked = null;

        _world.Creations[source].AuthorityPeer = 2;
        Assert.Contains("different players", Dispatch(ToolActions.Weld, new { source, target, offset = new[] { 0, 1, 0 } }).Message);
        Assert.Equal(2, _world.Creations.Count);
    }

    [Fact]
    public void WeldPartsLocksABearing()
    {
        var id = Add(i => Tower(i, new Vector3(0, 0.125f, 0)));

        var result = Dispatch(ToolActions.WeldParts, new { creation = id, a = 3, b = 2 });

        Assert.True(result.Ok, result.ToString());
        Assert.Equal(1, result.Value!.Value.GetProperty("bodies").GetInt32());
        Assert.Contains("already one rigid body", Dispatch(ToolActions.WeldParts, new { creation = id, a = 3, b = 2 }).Message);
    }

    // ---- Paint ----

    [Fact]
    public void PaintingColoursPartsAndCanTakeItOff()
    {
        var id = Add(Car());
        _world.Log.Clear();

        var result = Dispatch(ToolActions.Paint, new { creation = id, parts = new[] { 1, 2 }, color = "#D03030" });

        Assert.True(result.Ok, result.ToString());
        Assert.Equal(2, result.Value!.Value.GetProperty("painted").GetInt32());
        Assert.Equal("#d03030", _world.Creations[id].FindPart(2)!.Color);
        Assert.Equal(["changed 1"], _world.Log);

        Assert.Equal(0, Dispatch(ToolActions.Paint, new { creation = id, parts = new[] { 1 }, color = "#d03030" }).Value!.Value.GetProperty("painted").GetInt32());
        Assert.Equal(["changed 1"], _world.Log);

        Assert.True(Dispatch(ToolActions.Paint, new { creation = id, parts = new[] { 1 } }).Ok);
        Assert.Null(_world.Creations[id].FindPart(1)!.Color);
    }

    [Fact]
    public void PaintRequestsAreChecked()
    {
        var catalog = Catalog().Add("test:glass", new PartDefinition
        {
            Id = "glass",
            Name = "Glass",
            Category = PartCategory.Block,
            Size = GridSize.One,
            Visual = new VisualDefinition { Paintable = false },
        });
        var id = Add(i =>
        {
            var creation = new Creation(i, catalog);
            creation.Place(Block, GridCell.Zero);
            creation.Place("test:glass", Up);
            creation.SetAllBodyPoses(new Pose(new Vector3(0, 0.125f, 0), Quaternion.Identity));
            return creation;
        });

        Assert.Contains("Glass cannot be painted", Dispatch(ToolActions.Paint, new { creation = id, parts = new[] { 2 }, color = "#ffffff" }).Message);
        Assert.Contains("#rrggbb", Dispatch(ToolActions.Paint, new { creation = id, parts = new[] { 1 }, color = "red" }).Message);
        Assert.Contains("twice", Dispatch(ToolActions.Paint, new { creation = id, parts = new[] { 1, 1 }, color = "#ffffff" }).Message);
        Assert.Contains("1 to 3 parts", Dispatch(ToolActions.Paint, new { creation = id, parts = Array.Empty<int>(), color = "#ffffff" }).Message);
        Assert.Contains("no part 7", Dispatch(ToolActions.Paint, new { creation = id, parts = new[] { 7 }, color = "#ffffff" }).Message);
        Assert.Null(_world.Creations[id].FindPart(1)!.Color);
    }

    // ---- Sledgehammer ----

    [Fact]
    public void SledgeHitsDamagePushAndFinallyBreak()
    {
        var id = Add(Car(durability: 2)); // 20 health: two hits

        var first = Dispatch(ToolActions.Sledge, new { creation = id, part = 2 });

        Assert.True(first.Ok, first.ToString());
        Assert.False(first.Value!.Value.GetProperty("broken").GetBoolean());
        Assert.Equal(10f, first.Value!.Value.GetProperty("health").GetSingle());
        var push = Assert.Single(_world.Pushes);
        Assert.Equal(20f, push.Length(), 1e-3f);
        Assert.True(push.Z < 0, "the push points away from the player");

        Assert.Contains("still swinging", Dispatch(ToolActions.Sledge, new { creation = id, part = 2 }).Message);
        _world.Now += 0.5;
        var second = Dispatch(ToolActions.Sledge, new { creation = id, part = 2 });

        Assert.True(second.Value!.Value.GetProperty("broken").GetBoolean());
        Assert.Equal(1, second.Value!.Value.GetProperty("split_off").GetArrayLength());
        Assert.Equal(2, _world.Creations.Count);
        Assert.Single(_world.Creations[id].Parts);
    }

    [Fact]
    public void PartsWithoutDurabilityOnlyGetPushed()
    {
        var id = Add(Car());

        var result = Dispatch(ToolActions.Sledge, new { creation = id, part = 1 });

        Assert.True(result.Ok);
        Assert.Equal(JsonValueKind.Null, result.Value!.Value.TryGetProperty("health", out var health) ? health.ValueKind : JsonValueKind.Null);
        Assert.Equal(0f, _world.Creations[id].PartDamage(1));
        Assert.Single(_world.Pushes);
    }

    [Fact]
    public void TheSledgehammerReachesItsOwnShorterDistance()
    {
        var id = Add(Car(durability: 5));
        _world.Eyes[2] = new Vector3(0, 1.6f, 5);     // within the build reach (6 m), past the swing's 3 m

        var far = Dispatch(ToolActions.Sledge, new { creation = id, part = 2 });

        Assert.False(far.Ok);
        Assert.Contains("the Sledgehammer reaches 3.0 m", far.Message);
        _world.Eyes[2] = new Vector3(0, 1.6f, 2.5f);
        Assert.True(Dispatch(ToolActions.Sledge, new { creation = id, part = 2 }).Ok);
    }

    [Fact]
    public void ASwingAtTheGroundThudsWhereItLands()
    {
        _world.Eyes[2] = new Vector3(0, 1.6f, 0);

        var hit = Dispatch(ToolActions.Sledge, new { at = new[] { 0.5f, 0f, -1.5f } });

        Assert.True(hit.Ok, hit.Message);
        Assert.Equal(["smash_hit@vanilla (0.5, 0, -1.5)"], _world.Sounds);
        Assert.Empty(_world.Pushes);
        // Out of reach is refused like any other swing, and so is a hit on nothing at all.
        _world.Now += 10;
        Assert.Contains("reaches 3.0 m", Dispatch(ToolActions.Sledge, new { at = new[] { 0f, 0f, -6f } }).Message);
        Assert.False(Dispatch(ToolActions.Sledge, new { }).Ok);
    }

    [Fact]
    public void AHitSoundsWhereTheHeadLandsOnThePart()
    {
        var id = Add(Car(durability: 5));
        _world.Eyes[2] = new Vector3(0, 1.6f, 2.5f);
        string Smash() => _world.Sounds.Single(s => s.StartsWith("smash_hit@vanilla", StringComparison.Ordinal));

        // Without a landing point the sound plays at the part's centre.
        Assert.True(Dispatch(ToolActions.Sledge, new { creation = id, part = 2 }).Ok);
        var centre = Smash();
        var xyz = centre[(centre.IndexOf('(') + 1)..^1].Split(", ").Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var face = new Vector3(xyz[0], xyz[1], xyz[2] + 0.125f);

        _world.Sounds.Clear();
        _world.Now += 10;
        Assert.True(Dispatch(ToolActions.Sledge, new { creation = id, part = 2, at = new[] { face.X, face.Y, face.Z } }).Ok);
        Assert.EndsWith(FormattableString.Invariant($"({face.X:0.###}, {face.Y:0.###}, {face.Z:0.###})"), Smash());

        // A landing point nowhere near the part falls back to its centre.
        _world.Sounds.Clear();
        _world.Now += 10;
        Assert.True(Dispatch(ToolActions.Sledge, new { creation = id, part = 2, at = new[] { 40f, 0f, 0f } }).Ok);
        Assert.Equal(centre, Smash());
    }

    [Fact]
    public void KnockbackIsCappedSoLightBodiesDoNotFly()
    {
        var settings = _settings with { Sledge = new SledgeSettings { Impulse = 5000f, MaxKnockbackSpeed = 4f } };
        var tools = new ToolActions(settings, _world);
        var dispatcher = new ActionDispatcher();
        tools.Register(dispatcher);
        var id = Add(Car(durability: 5));
        var mass = _world.Creations[id].Bodies[0].Mass;

        Assert.True(dispatcher.Dispatch(Player, ToolActions.Sledge, JsonSerializer.Serialize(new { creation = id, part = 2 })).Ok);

        Assert.Equal(12f, mass);
        Assert.Equal(mass * 4f, Assert.Single(_world.Pushes).Length(), 1e-3f);
        Assert.Equal(5000f, settings.Sledge.ImpulseFor(2000f));
        Assert.Equal(0f, settings.Sledge.ImpulseFor(0f));
    }

    [Fact]
    public void BotsSwingWithoutCooldownOrReach()
    {
        var id = Add(Car(durability: 1));
        _world.Eyes.Clear();
        var bot = new ActionCaller(1, ActionSource.Bot);

        Assert.True(_dispatcher.Dispatch(bot, ToolActions.Sledge, new SledgeArgs(id, 1)).Ok);
        Assert.True(_dispatcher.Dispatch(bot, ToolActions.Sledge, new SledgeArgs(id, 2)).Ok);
        Assert.Empty(_world.Pushes);
    }

    // ---- Sounds ----

    [Fact]
    public void LiftActionsPlayTheLiftSounds()
    {
        var id = Add(Car());
        Dispatch(ToolActions.PlaceLift, new { position = new[] { 1f, 0f, 0f }, creation = id });
        Dispatch(ToolActions.SetLiftLevel, new { level = 2 });
        Dispatch(ToolActions.SetLiftLevel, new { level = 2 });
        Dispatch(ToolActions.SetLiftLevel, new { level = 1 });
        Dispatch(ToolActions.TurnLift, new { turns = 1 });
        Dispatch(ToolActions.RemoveLift, new { });

        Assert.Equal(
        [
            "lift_place@vanilla (1, 0, 0)",
            "lift_raise@vanilla (1, 0.25, 0)", // from the platform top where it starts
            "lift_lower@vanilla (1, 0.75, 0)", // the unchanged level played nothing
            "lift_place@vanilla (1, 0.5, 0)",
            "lift_lower@vanilla (1, 0, 0)",
        ], _world.Sounds);
    }

    [Fact]
    public void WeldPaintAndSledgePlayTheirSounds()
    {
        var tower = Add(i => Tower(i, new Vector3(0, 0.125f, 0)));
        var car = Add(Car(durability: 1));

        Assert.True(Dispatch(ToolActions.WeldParts, new { creation = tower, a = 3, b = 2 }).Ok);
        Assert.True(Dispatch(ToolActions.Paint, new { creation = car, parts = new[] { 2, 3 }, color = "#ff0000" }).Ok);
        Assert.True(Dispatch(ToolActions.Paint, new { creation = car, parts = new[] { 3 } }).Ok);
        Assert.True(Dispatch(ToolActions.Sledge, new { creation = car, part = 3 }).Ok);

        Assert.Equal(
        [
            "weld_complete@vanilla (0, 0.375, 0)",
            "paint_spray@vanilla (0, 0.125, 0)",
            "paint_erase@vanilla (0.25, 0.125, 0)",
            "smash_break@vanilla (0.25, 0.125, 0)", // the test block has no sound material, so no break sound of its own
        ], _world.Sounds);
    }

    // ---- Helpers ----

    private static Func<int, Creation> Car(Vector3? at = null, int? durability = null) => id =>
    {
        var catalog = durability is null ? Catalog() : new PartCatalog().Add(Block, new PartDefinition
        {
            Id = "block",
            Name = "Block",
            Category = PartCategory.Block,
            Mass = 4,
            Size = GridSize.One,
            Durability = durability,
        });
        var creation = new Creation(id, catalog);
        creation.Place(Block, GridCell.Zero);
        creation.Place(Block, new GridCell(1, 0, 0));
        creation.Place(Block, new GridCell(2, 0, 0));
        creation.SetAllBodyPoses(new Pose(at ?? new Vector3(-0.25f, 0.125f, 0), Quaternion.Identity));
        return creation;
    };

    private static Func<int, Creation> Tilted(Func<int, Creation> make) => id =>
    {
        var creation = make(id);
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 2.5f) * Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f);
        creation.SetAllBodyPoses(new Pose(new Vector3(1, 0.4f, 0), q));
        return creation;
    };

    private static Func<int, Creation> Single(string part, Vector3 at) => id =>
    {
        var creation = new Creation(id, Catalog());
        creation.Place(part, GridCell.Zero);
        creation.SetAllBodyPoses(new Pose(at, Quaternion.Identity));
        return creation;
    };

    // Block, bearing, block: two Bodies joined by the bearing.
    private static Creation Tower(int id, Vector3 at)
    {
        var tower = new Creation(id, Catalog());
        tower.Place(Block, GridCell.Zero);
        tower.Place(Bearing, Up);
        tower.Place(Block, Up + Up);
        tower.SetAllBodyPoses(new Pose(at, Quaternion.Identity));
        return tower;
    }

    private static Pose Pose(Creation creation, int part) => creation.BodyPose(creation.BodyIndexOf(part));

    // Adds a Creation made with the world's next id.
    private int Add(Func<int, Creation> make)
    {
        var id = _world.NextCreationId();
        _world.Creations[id] = make(id);
        return id;
    }

    private ActionResult Dispatch(string action, object args) =>
        _dispatcher.Dispatch(Player, action, JsonSerializer.Serialize(args));

    private sealed class FakeWorld : IToolWorld
    {
        private int _nextId = 1;

        public List<string> Sounds { get; } = [];

        public Dictionary<int, Creation> Creations { get; } = [];
        public Dictionary<int, Vector3> Eyes { get; } = [];
        public List<string> Log { get; } = [];
        public List<Vector3> Pushes { get; } = [];
        public string? Blocked { get; set; }
        public IReadOnlyCollection<int>? LastIgnore { get; private set; }
        public int? LastIgnoreLift { get; private set; }
        public double Now { get; set; } = 100;

        public Creation? FindCreation(int id) => Creations.GetValueOrDefault(id);

        public Vector3? EyeOf(int peer) => Eyes.TryGetValue(peer, out var eye) ? eye : null;

        public int NextCreationId() => _nextId++;

        public string? CheckClear(Creation? target, PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose) => Blocked;

        public string? CheckPartClear(PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose,
            IReadOnlyCollection<int> ignoreCreations, int? ignoreLiftOf)
        {
            LastIgnore = ignoreCreations;
            LastIgnoreLift = ignoreLiftOf;
            return Blocked;
        }

        public string? CheckLiftClear(LiftState lift, IReadOnlyCollection<int> ignoreCreations) => Blocked;

        public void Added(Creation creation)
        {
            Creations.Add(creation.Id, creation);
            Log.Add($"added {creation.Id}");
        }

        public void Changed(Creation creation) => Log.Add($"changed {creation.Id}");

        public void Removed(int creationId)
        {
            Creations.Remove(creationId);
            Log.Add($"removed {creationId}");
        }

        public void Moved(Creation creation) => Log.Add($"moved {creation.Id}{(creation.OnLift ? " on lift" : "")}");

        public void LiftChanged(int owner, LiftState? lift) =>
            Log.Add(lift is null
                ? $"lift {owner} removed"
                : $"lift {owner} at ({lift.Position.X}, {lift.Position.Y}, {lift.Position.Z}) level {lift.Level}{(lift.Creation is { } c ? $" holding {c}" : "")}");

        public void Push(Creation creation, int bodyIndex, Vector3 point, Vector3 impulse) => Pushes.Add(impulse);

        public void PlaySound(string sound, string ownerModId, Vector3 position) =>
            Sounds.Add(FormattableString.Invariant($"{sound}@{ownerModId} ({position.X:0.###}, {position.Y:0.###}, {position.Z:0.###})"));
    }
}
