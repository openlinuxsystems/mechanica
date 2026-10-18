using System.Numerics;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using static Mechanica.Core.Tests.Survival.SurvivalFixture;

namespace Mechanica.Core.Tests.Survival;

/// <summary>
/// Tree Harvesting spec: trees that fall as log segments (loose parts) stacked up the trunk and tipping away from the
/// blow, logs that break into Wood Rods, and a large tree that only a saw cuts.
/// </summary>
public class ForestryTests
{
    private const string Log = "test:wood_log";
    private const string Rod = "test:wood_rod";

    private readonly SurvivalFixture _f = new(new SurvivalSettings
    {
        Reach = 5,
        PickupReach = 3,
        DropLifetime = 100,
        MaxDrops = 8,
        RefineSeconds = 1.2f,
        RefineCancelDistance = 1.5f,
    });

    private readonly WorldObjectId _tree = new(0, 1, 1);
    private readonly WorldObjectId _pine = new(1, 2, 2);
    private static readonly Vector3 TreeAt = new(0, 0, -2);

    public ForestryTests()
    {
        _f.World.Feet[Me.Peer] = Vector3.Zero;
        _f.Catalog
            .Add(Log, Loose("wood_log", new GridSize(2, 2, 6)) with
            {
                Durability = 8,
                SledgeDamage = 25,
                BreaksInto = new BreakDefinition { Count = 2, Parts = [new BreakPiece { Part = Rod }] },
            })
            .Add(Rod, Loose("wood_rod", new GridSize(1, 1, 4)) with { Refine = [new RecipeItem { Item = Scrap, Count = 20 }] });
        _f.World.Objects[_tree] = new WorldObjectPlacement(_tree, Tree("leafy_tree", 40, [DamageKinds.Sledge, DamageKinds.Saw], 4, 6), TreeAt, 0, 1);
        _f.World.Objects[_pine] = new WorldObjectPlacement(_pine, Tree("pine_tree", 100, [DamageKinds.Saw], 8, 12), new Vector3(0, 0, 3), 0, 1);
    }

    private static WorldObjectDefinition Tree(string id, float health, List<string> from, int fewest, int most) => new()
    {
        Id = id,
        Collider = new ShapeDefinition { Shape = PrimitiveShape.Cylinder, Radius = 0.4f, Height = 6, At = new Vec3(0, 3, 0) },
        Health = health,
        Sink = 0.3f,
        DamageFrom = from,
        BreaksInto = new BreakDefinition { Count = fewest, CountMax = most, Parts = [new BreakPiece { Part = Log }] },
        Fell = new FellDefinition { Height = 7, Spin = 0.5f },
        Sounds = new Dictionary<string, string> { ["hit"] = $"{id}_hit", ["break"] = $"{id}_fall" },
    };

    private static PartDefinition Loose(string id, GridSize size) => new()
    {
        Id = id,
        Name = id,
        Category = PartCategory.Resource,
        Loose = true,
        Mass = 40,
        Size = size,
        StackSize = 4,
    };

    private List<Creation> LooseOf(string part) =>
        [.. _f.World.Creations.Values.Where(c => c.Parts.Count == 1 && Identifiers.Qualify(c.Parts.First().Part) == part)];

    private SledgeResult Swing(WorldObjectId id)
    {
        _f.World.Now += 1;
        return _f.Ok<SledgeResult>(Me, ToolActions.Sledge, new { @object = id.ToString() });
    }

    [Fact]
    public void TheSledgeKnocksATreeWithItsOwnSoundAndShakesIt()
    {
        var swing = Swing(_tree);
        Assert.False(swing.Broken);
        Assert.Equal(30f, swing.Health);
        Assert.Contains(_f.World.Sounds, s => s.Sound == "leafy_tree_hit");
        Assert.DoesNotContain(_f.World.Sounds, s => s.Sound == "smash_hit");
        var (id, _, broke) = Assert.Single(_f.World.ObjectHits);
        Assert.Equal(_tree, id);
        Assert.False(broke);
    }

    [Fact]
    public void AFelledTreeFallsAsLogsStackedUpItsTrunkTippingAwayFromTheBlow()
    {
        for (var i = 0; i < 3; i++)
            Assert.False(Swing(_tree).Broken);
        Assert.True(Swing(_tree).Broken);
        Assert.True(_f.World.State.IsDestroyed(_tree));
        Assert.Contains(_f.World.Sounds, s => s.Sound == "leafy_tree_fall");
        Assert.True(_f.World.ObjectHits[^1].Broke);
        Assert.Empty(LooseOf(Rod));

        var logs = LooseOf(Log);
        Assert.InRange(logs.Count, 4, 6);
        Assert.All(logs, c => Assert.False(c.IsGrounded));
        // Upright, one above the other from the ground up, never overlapping.
        var centres = logs.Select(c => FakeWorld.Centre(c, c.Parts.First().Id)).OrderBy(c => c.Y).ToList();
        Assert.All(centres, c => Assert.True(Vector2.Distance(new Vector2(c.X, c.Z), new Vector2(TreeAt.X, TreeAt.Z)) < 0.01f, $"{c} is on the trunk"));
        // The trunk's foot is the ground, sink (0.3 m) above the object's point; the lowest log stands just clear of it.
        Assert.InRange(centres[0].Y, 1.05f, 1.15f);
        for (var i = 1; i < centres.Count; i++)
            Assert.True(centres[i].Y - centres[i - 1].Y >= 1.5f, "log segments are end to end");
        foreach (var log in logs)
        {
            var up = Vector3.Transform(Vector3.UnitZ, log.BodyPose(0).Rotation);
            Assert.True(MathF.Abs(up.Y) > 0.99f, "the long side stands up");
        }
        // The player stood at the origin, south of the tree (at -z): it falls north, away from them, each log faster the
        // higher it starts, as one trunk turning about its foot, and each pushed above its middle so it turns too.
        Assert.Equal(logs.Count, _f.World.Pushes.Count);
        var pushes = _f.World.Pushes.Zip(_f.World.PushPoints).OrderBy(p => p.Second.Y).ToList();
        for (var i = 0; i < pushes.Count; i++)
        {
            var (impulse, point) = pushes[i];
            Assert.True(impulse.Z < 0 && MathF.Abs(impulse.X) < 1e-3f && MathF.Abs(impulse.Y) < 1e-3f, $"{impulse} falls away");
            // spin (0.5 rad/s) × height over the foot × mass (40 kg).
            Assert.Equal(0.5f * (centres[i].Y - 0.3f) * 40f, -impulse.Z, 2);
            Assert.InRange(point.Y - centres[i].Y, 0.001f, 0.75f);
        }
        Assert.True(-pushes[^1].First.Z > -pushes[0].First.Z);
    }

    [Fact]
    public void AFallingTreeTipsAwayFromAnySideItIsHitFrom()
    {
        _f.World.Feet[Me.Peer] = new Vector3(3, 0, -2);
        for (var i = 0; i < 4; i++)
            Swing(_tree);
        Assert.All(_f.World.Pushes, p => Assert.True(p.X < 0 && MathF.Abs(p.Z) < 1e-3f, $"{p} falls west, away from the east"));
    }

    [Fact]
    public void ALargeTreeTakesNothingFromHandToolsButFallsToASaw()
    {
        _f.World.Feet[Me.Peer] = new Vector3(0, 0, 1.5f);
        var swing = Swing(_pine);
        Assert.Equal(100f, swing.Health);
        Assert.Equal(0f, _f.World.State.DamageOf(_pine));
        Assert.Empty(_f.World.ObjectHits);
        Assert.DoesNotContain(_f.World.Sounds, s => s.Sound == "pine_tree_hit");
        _f.Survival.Splash(Me, new Vector3(0, 1, 3), 4, 80, 0);
        Assert.Equal(0f, _f.World.State.DamageOf(_pine));

        // A saw's rim bites from the side: no push, so it falls away from where it bit.
        var bite = new RayHit(new Vector3(-0.4f, 1, 3), Object: _pine);
        Assert.Equal(15f, _f.Survival.Hit(Me, bite, 15, Vector3.Zero, DamageKinds.Saw).Damage);
        Assert.Contains(_f.World.Sounds, s => s.Sound == "pine_tree_hit");
        for (var i = 0; i < 6; i++)
            _f.Survival.Hit(Me, bite, 15, Vector3.Zero, DamageKinds.Saw);
        Assert.True(_f.World.State.IsDestroyed(_pine));
        Assert.InRange(LooseOf(Log).Count, 8, 12);
        Assert.All(_f.World.Pushes, p => Assert.True(p.X > 0, $"{p} falls east, away from the saw"));
    }

    [Fact]
    public void LogsBreakIntoWoodRodsThatRefineIntoBlocks()
    {
        var log = _f.Add(Log, new Vector3(0, 0, -1.5f));
        // 80 health (durability 8), 25 a hit.
        for (var i = 0; i < 3; i++)
        {
            _f.World.Now += 1;
            Assert.False(_f.Ok<SledgeResult>(Me, ToolActions.Sledge, new { creation = log, part = 1 }).Broken);
        }
        _f.World.Now += 1;
        Assert.True(_f.Ok<SledgeResult>(Me, ToolActions.Sledge, new { creation = log, part = 1 }).Broken);
        var rods = LooseOf(Rod);
        Assert.Equal(2, rods.Count);

        var rod = rods[0];
        _f.Ok(Me, SurvivalActions.StartRefine, new { creation = rod.Id, part = rod.Parts.First().Id });
        _f.World.Now += 1.2;
        var refined = _f.Ok<RefineResult>(Me, SurvivalActions.FinishRefine, new { creation = rod.Id, part = rod.Parts.First().Id });
        Assert.Equal(20, refined.Taken);
    }

    [Theory]
    [InlineData(2, null, 0f, 2)]
    [InlineData(2, null, 0.99f, 2)]
    [InlineData(2, 3, 0f, 2)]
    [InlineData(2, 3, 0.49f, 2)]
    [InlineData(2, 3, 0.5f, 3)]
    [InlineData(2, 3, 0.999f, 3)]
    [InlineData(8, 12, 1f, 12)]
    [InlineData(0, 40, 1f, BreakDefinition.MaxCount)]
    public void ABreakCanVaryItsCount(int count, int? most, float roll, int expected) =>
        Assert.Equal(expected, new BreakDefinition { Count = count, CountMax = most }.CountFor(roll));

    [Fact]
    public void FallingWithNoSideToFallFromPicksOneFromTheSeed()
    {
        var placement = _f.World.Objects[_tree];
        var a = SurvivalActions.FallDirection(placement, placement.Position, 7);
        Assert.Equal(a, SurvivalActions.FallDirection(placement, placement.Position + Vector3.UnitY * 3, 7));
        Assert.Equal(1f, a.Length(), 3);
        Assert.Equal(0f, a.Y);
    }
}
