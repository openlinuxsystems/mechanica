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
/// Boulder Mining spec: rock outcrop cubes that only a spinning drill breaks, into chunks (loose parts) that a drill or
/// the Sledgehammer breaks into Stone and Metal Rods, which refine by hand into blocks.
/// </summary>
public class MiningTests
{
    private const string Chunk = "test:stone_chunk";
    private const string StoneRod = "test:stone_rod";
    private const string MetalRod = "test:metal_rod";
    private const string StoneBlock = "test:stone_block";

    private readonly SurvivalFixture _f = new(new SurvivalSettings
    {
        Reach = 5,
        PickupReach = 3,
        DropLifetime = 100,
        MaxDrops = 8,
        RefineSeconds = 1.2f,
        RefineCancelDistance = 1.5f,
    });

    private readonly WorldObjectId _cube = new(0, 4, 4, 3);

    public MiningTests()
    {
        _f.World.Feet[Me.Peer] = Vector3.Zero;
        _f.Catalog
            .Add(StoneBlock, new PartDefinition { Id = "stone_block", Name = "Stone Block", Category = PartCategory.Block, Size = GridSize.One, Mass = 9, StackSize = 100 })
            .Add(Chunk, Loose("stone_chunk", 2) with
            {
                Durability = 10,
                SledgeDamage = 25,
                BreaksInto = new BreakDefinition { Count = 2, Parts = [new BreakPiece { Part = StoneRod, Weight = 7 }, new BreakPiece { Part = MetalRod, Weight = 3 }] },
            })
            .Add(StoneRod, Loose("stone_rod", 1) with { Size = new GridSize(1, 1, 4), Refine = [new RecipeItem { Item = StoneBlock, Count = 20 }] })
            .Add(MetalRod, Loose("metal_rod", 1) with { Size = new GridSize(1, 1, 4), Refine = [new RecipeItem { Item = Scrap, Count = 20 }] });
        _f.World.Objects[_cube] = new WorldObjectPlacement(_cube, new WorldObjectDefinition
        {
            Id = "rock_outcrop",
            Collider = new ShapeDefinition { Shape = PrimitiveShape.Box, Size = new Vec3(1, 1, 1), At = new Vec3(0, 0.5f, 0) },
            Health = 100,
            DamageFrom = [DamageKinds.Drill],
            BreaksInto = new BreakDefinition { Count = 2, Parts = [new BreakPiece { Part = Chunk }] },
        }, new Vector3(0, 0, -2), 0, 1);
    }

    private static PartDefinition Loose(string id, int size) => new()
    {
        Id = id,
        Name = string.Join(' ', id.Split('_').Select(w => char.ToUpperInvariant(w[0]) + w[1..])),
        Category = PartCategory.Resource,
        Loose = true,
        Mass = 10,
        Size = new GridSize(size, size, size),
        StackSize = 10,
    };

    private RayHit CubeHit => new(new Vector3(0, 0.5f, -1.5f), Object: _cube);

    private List<Creation> LooseOf(string part) =>
        [.. _f.World.Creations.Values.Where(c => c.Parts.Count == 1 && Identifiers.Qualify(c.Parts.First().Part) == part)];

    [Fact]
    public void OnlyASpinningDrillDamagesAnOutcropCube()
    {
        Assert.Equal(0f, _f.Survival.Hit(Me, CubeHit, 50, Vector3.Zero).Damage);
        Assert.Equal(0f, _f.Survival.Hit(Me, CubeHit, 50, Vector3.Zero, DamageKinds.Saw).Damage);
        _f.Survival.Splash(Me, new Vector3(0, 0.5f, -2), 4, 80, 0);
        _f.World.Now += 1;
        var swing = _f.Ok<SledgeResult>(Me, ToolActions.Sledge, new { @object = _cube.ToString() });
        Assert.False(swing.Broken);
        Assert.Equal(100f, swing.Health);
        Assert.Equal(0f, _f.World.State.DamageOf(_cube));

        Assert.Equal(40f, _f.Survival.Hit(Me, CubeHit, 40, Vector3.Zero, DamageKinds.Drill).Damage);
        Assert.Equal(40f, _f.World.State.DamageOf(_cube));
        Assert.False(_f.World.State.IsDestroyed(_cube));
    }

    [Fact]
    public void ADrilledCubeBreaksIntoLooseChunks()
    {
        for (var i = 0; i < 4; i++)
            _f.Survival.Hit(Me, CubeHit, 25, Vector3.Zero, DamageKinds.Drill);
        Assert.True(_f.World.State.IsDestroyed(_cube));
        var chunks = LooseOf(Chunk);
        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c => Assert.False(c.IsGrounded));
        // Each is thrown out and up a little, and they start apart, round where the cube stood.
        Assert.Equal(2, _f.World.Pushes.Count);
        Assert.All(_f.World.Pushes, p => Assert.True(p.Y > 0));
        var centres = chunks.Select(c => FakeWorld.Centre(c, c.Parts.First().Id)).ToList();
        Assert.True(Vector3.Distance(centres[0], centres[1]) > 0.1f);
        Assert.All(centres, c => Assert.True(Vector3.Distance(c, new Vector3(0, 0.5f, -2)) < 1f));
        Assert.Contains(_f.World.Sounds, s => s.Sound == SurvivalActions.BreakSound);
    }

    [Fact]
    public void AChunkBreaksIntoRodsUnderTheSledgeOrADrill()
    {
        var chunk = _f.Add(Chunk, new Vector3(0, 0, -1.5f));
        for (var i = 0; i < 3; i++)
        {
            _f.World.Now += 1;
            Assert.False(_f.Ok<SledgeResult>(Me, ToolActions.Sledge, new { creation = chunk, part = 1 }).Broken);
        }
        _f.World.Now += 1;
        // 100 health, 25 a hit (its own sledge_damage, not the sledge's 10).
        Assert.True(_f.Ok<SledgeResult>(Me, ToolActions.Sledge, new { creation = chunk, part = 1 }).Broken);
        Assert.False(_f.World.Creations.ContainsKey(chunk));
        Assert.Equal(2, LooseOf(StoneRod).Count + LooseOf(MetalRod).Count);

        var other = _f.Add(Chunk, new Vector3(0, 0, -1.5f));
        var at = new RayHit(new Vector3(0, 0.25f, -1.5f), Creation: other, Part: 1);
        for (var i = 0; i < 4; i++)
            _f.Survival.Hit(Me, at, 25, Vector3.Zero, DamageKinds.Drill);
        Assert.False(_f.World.Creations.ContainsKey(other));
        Assert.Equal(4, LooseOf(StoneRod).Count + LooseOf(MetalRod).Count);
    }

    [Fact]
    public void RodsAreMostlyStoneFromAStoneChunk()
    {
        var tools = _f.Tools;
        var breaks = _f.Catalog.FindDefinition(Chunk)!.BreaksInto!;
        for (var i = 0u; i < 200; i++)
            tools.Shatter(breaks, new Vector3(0, 1, 0), 0.25f, Noise.Mix(i));
        var stone = LooseOf(StoneRod).Count;
        var metal = LooseOf(MetalRod).Count;
        Assert.Equal(400, stone + metal);
        Assert.InRange(stone / 400f, 0.6f, 0.8f);
    }

    [Fact]
    public void HoldingUseOnARodRefinesItIntoBlocks()
    {
        var rod = _f.Add(StoneRod, new Vector3(0, 0, -1.5f));
        var args = new { creation = rod, part = 1 };
        _f.Ok(Me, SurvivalActions.StartRefine, args);
        Assert.Equal($"part:{rod}.1", _f.Survival.RefiningOf(Me.Peer)!.Value.Target);
        _f.World.Now += 0.5;
        Assert.Equal("Keep holding Use to refine it.", _f.Refused(Me, SurvivalActions.FinishRefine, args));
        _f.World.Now += 0.8;
        Assert.Equal(new RefineResult(20, 0), _f.Ok<RefineResult>(Me, SurvivalActions.FinishRefine, args));
        Assert.Equal(20, _f.Pockets(Me).Count(StoneBlock));
        Assert.False(_f.World.Creations.ContainsKey(rod));
        Assert.Contains(_f.World.Sounds, s => s.Sound == SurvivalActions.RefineCompleteSound);
    }

    [Fact]
    public void OnlyLooseRefinablePartsInReachRefine()
    {
        var chunk = _f.Add(Chunk, new Vector3(0, 0, -1.5f));
        Assert.Equal("The Stone Chunk cannot be refined.", _f.Refused(Me, SurvivalActions.StartRefine, new { creation = chunk, part = 1 }));
        var far = _f.Add(StoneRod, new Vector3(0, 0, -20));
        Assert.StartsWith("That is", _f.Refused(Me, SurvivalActions.StartRefine, new { creation = far, part = 1 }));
        Assert.StartsWith("There is nothing to refine", _f.Refused(Me, SurvivalActions.StartRefine, new { creation = 99, part = 1 }));
        Assert.Equal("Give the object, or the creation and part, to refine.", _f.Refused(Me, SurvivalActions.StartRefine, new { }));
        Assert.Equal("Give either object or creation and part, not both.",
            _f.Refused(Me, SurvivalActions.StartRefine, new { @object = "1.2.3", creation = far, part = 1 }));
    }

    [Fact]
    public void NothingIsBuiltOntoOrWeldedToALoosePart()
    {
        var rod = _f.Add(StoneRod, new Vector3(0, 0, -1.5f));
        Assert.Equal("Nothing can be built onto a Stone Rod.",
            _f.Refused(Me, BuildActions.PlacePart, new { part = Block, cell = new[] { 1, 0, 0 }, creation = rod }));
        var block = _f.Add(Block, new Vector3(0, 0, -1));
        Assert.Equal("Stone Rod is set down on its own, not on a creation.",
            _f.Refused(Me, BuildActions.PlacePart, new { part = StoneRod, cell = new[] { 1, 0, 0 }, creation = block }));
        Assert.Equal("A Stone Rod cannot be welded to anything.", _f.Refused(Me, ToolActions.Weld, new { source = rod, target = block, offset = new[] { 1, 0, 0 } }));
    }

    [Fact]
    public void ARodSetDownStandsAloneAndIsNotAnchored()
    {
        _f.Pockets(Me).SetSlot(0, new ItemStack(StoneRod, 2));
        var placed = _f.Ok<PlacePartResult>(Me, BuildActions.PlacePart, new { part = StoneRod, cell = new[] { 0, 0, 0 }, position = new Vec3(0, 0, -1) });
        var creation = _f.World.Creations[placed.Creation];
        Assert.False(creation.IsGrounded);
        Assert.Equal(1, _f.Pockets(Me).Count(StoneRod));
    }

    [Fact]
    public void BotsLeaveOutcropsAlone()
    {
        Assert.Equal(100f, _f.Survival.StrikeObject(_cube.ToString(), 50, DamageKinds.Bot));
        Assert.Equal(50f, _f.Survival.StrikeObject(_cube.ToString(), 50, DamageKinds.Drill));
    }
}
