using System.Numerics;
using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

/// <summary>Boulder Mining spec §2.3 and §3, Square Rocks redesign: rock deposits of heaped blocks, scattered on open, flat ground.</summary>
public class OutcropTests
{
    private static readonly Lazy<WorldGenData> VanillaData = new(() => WorldGenData.Load(ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"))));

    private static readonly Dictionary<string, Vector3> TestBlocks = new()
    {
        ["small"] = new(0.65f, 0.55f, 0.6f),
        ["medium"] = new(1f, 1f, 1f),
        ["large"] = new(1.6f, 1.25f, 1.4f),
        ["rich_medium"] = new(1f, 1f, 1f),
    };

    private static readonly OutcropDefinition TestDeposit = new()
    {
        Deposits =
        [
            new DepositSize { Weight = 1, Mounds = [1, 2], Spread = 3, Sizes = [new MoundPick { Mound = "small" }] },
            new DepositSize { Weight = 1, Mounds = [5, 7], Spread = 7, Sizes = [new MoundPick { Mound = "small" }, new MoundPick { Mound = "large", Weight = 2 }] },
        ],
        Mounds = new()
        {
            ["small"] = new MoundDefinition { Blocks = [4, 8], Kinds = [new MoundBlock { Object = "small" }, new MoundBlock { Object = "medium" }] },
            ["large"] = new MoundDefinition
            {
                Blocks = [14, 24],
                Kinds = [new MoundBlock { Object = "small" }, new MoundBlock { Object = "medium", Weight = 2 }, new MoundBlock { Object = "large", Weight = 2 }],
            },
        },
        RichChance = 0.3f,
        Rich = new() { ["medium"] = "rich_medium" },
    };

    private static IReadOnlyList<OutcropBlock> Deposit(uint salt) =>
        OutcropLayout.Blocks(TestDeposit, id => TestBlocks.TryGetValue(id, out var size) ? size : null, id => id == "medium" ? 2 : 1, 12f, salt);

    // Footprint overlap of two blocks, less the give the layout allows at chiselled edges.
    private static float Overlap(OutcropBlock a, OutcropBlock b)
    {
        var ox = MathF.Min(a.Bottom.X + a.Half.X, b.Bottom.X + b.Half.X) - MathF.Max(a.Bottom.X - a.Half.X, b.Bottom.X - b.Half.X) - 0.12f;
        var oz = MathF.Min(a.Bottom.Z + a.Half.Y, b.Bottom.Z + b.Half.Y) - MathF.Max(a.Bottom.Z - a.Half.Y, b.Bottom.Z - b.Half.Y) - 0.12f;
        return ox > 0 && oz > 0 ? ox * oz : 0f;
    }

    [Theory]
    [InlineData(1u)]
    [InlineData(7u)]
    [InlineData(123456u)]
    [InlineData(0xDEADBEEFu)]
    public void ADepositIsMoundsOfBlocksThatRestOnTheGroundOrOnEachOther(uint salt)
    {
        var blocks = Deposit(salt);
        Assert.Equal(blocks, Deposit(salt));
        Assert.NotEmpty(blocks);
        foreach (var mound in blocks.GroupBy(b => b.Mound))
        {
            var list = mound.ToList();
            Assert.InRange(list.Count, 4, 24);
            for (var i = 0; i < list.Count; i++)
            {
                var b = list[i];
                Assert.InRange(b.Yaw, -OutcropLayout.Twist, OutcropLayout.Twist);
                Assert.Equal(TestBlocks[b.Object].Y, b.Height);
                Assert.True(new Vector2(b.Bottom.X, b.Bottom.Z).Length() + b.Half.Length() <= 12f, "a block stands outside the deposit's footprint");
                // No two blocks of a mound share any space.
                for (var j = 0; j < i; j++)
                {
                    var o = list[j];
                    Assert.False(Overlap(b, o) > 0f && b.Bottom.Y < o.Top - 0.001f && o.Bottom.Y < b.Top - 0.001f, $"blocks {j} and {i} overlap");
                }
                // A block not on the ground rests mostly on blocks whose tops are level with its underside.
                if (b.Bottom.Y > 0f)
                {
                    var support = list.Where(o => MathF.Abs(o.Top - b.Bottom.Y) < 0.08f).Sum(o => Overlap(b, o));
                    Assert.True(support >= 0.45f * (2 * b.Half.X - 0.12f) * (2 * b.Half.Y - 0.12f) - 1e-4f, $"block {i} hangs in the air");
                }
            }
            // Big blocks make the base: the first block of a mound is one of its largest.
            Assert.Equal(list.Max(b => TestBlocks[b.Object].X * TestBlocks[b.Object].Y * TestBlocks[b.Object].Z),
                TestBlocks[list[0].Object].X * TestBlocks[list[0].Object].Y * TestBlocks[list[0].Object].Z);
        }
        // Mounds stand apart: no block of one overlaps a block of another.
        foreach (var a in blocks)
            Assert.DoesNotContain(blocks, b => b.Mound != a.Mound && Overlap(a, b) > 0f);
        // Only the main medium block has a second model.
        Assert.All(blocks, b => Assert.InRange(b.Variant, 0, b.Object == "medium" ? 1 : 0));
    }

    [Fact]
    public void ManyDepositsCoverEverySizeAndSomeMoundsTurnMetalRich()
    {
        var deposits = Enumerable.Range(0, 200).Select(i => Deposit(Noise.Mix((uint)i))).ToList();
        var mounds = deposits.Select(d => d.Select(b => b.Mound).Distinct().Count()).ToList();
        Assert.Contains(1, mounds);
        Assert.Contains(mounds, m => m >= 5);
        Assert.Contains(deposits, d => d.GroupBy(b => b.Mound).Any(m => m.Count() >= 14));
        Assert.Contains(deposits, d => d.Any(b => b.Object == "large"));
        // Metal-rich mounds swap their blocks, never in a deposit's first mound.
        Assert.Contains(deposits, d => d.Any(b => b.Object == "rich_medium"));
        Assert.All(deposits, d => Assert.DoesNotContain(d, b => b.Mound == 0 && b.Object == "rich_medium"));
        Assert.All(deposits, d => Assert.All(d.GroupBy(b => b.Mound), m => Assert.False(m.Any(b => b.Object == "rich_medium") && m.Any(b => b.Object == "medium"))));
        // Some mounds stack: blocks up off the ground.
        Assert.Contains(deposits, d => d.Any(b => b.Bottom.Y > 0.5f));
    }

    [Theory]
    [InlineData("""{ "mounds": [1, 2], "sizes": [{ "mound": "missing" }] }""", """{ "blocks": [4, 8], "kinds": [{ "object": "block" }] }""", "$.objects[0].outcrop.deposits[0].sizes[0].mound")]
    [InlineData("""{ "mounds": [3, 1], "sizes": [{ "mound": "small" }] }""", """{ "blocks": [4, 8], "kinds": [{ "object": "block" }] }""", "$.objects[0].outcrop.deposits[0].mounds")]
    [InlineData("""{ "mounds": [1, 2], "sizes": [{ "mound": "small" }] }""", """{ "blocks": [4, 2], "kinds": [{ "object": "block" }] }""", "$.objects[0].outcrop.mounds.small.blocks")]
    [InlineData("""{ "mounds": [1, 2], "sizes": [{ "mound": "small" }] }""", """{ "blocks": [4, 8], "kinds": [{ "object": "ball" }] }""", "$.objects[0].outcrop.mounds.small.kinds[0].object")]
    [InlineData("""{ "mounds": [1, 2], "sizes": [{ "mound": "small" }] }""", """{ "blocks": [4, 8], "kinds": [{ "object": "nothing" }] }""", "$.objects[0].outcrop.mounds.small.kinds[0].object")]
    [InlineData("""{ "mounds": [1, 2], "sizes": [{ "mound": "small" }] }""", """{ "blocks": [4, 8], "kinds": [] }""", "$.objects[0].outcrop.mounds.small.kinds")]
    public void ADepositWithBadSizesOrBlocksGivesAClearError(string deposit, string mound, string path)
    {
        var json = $$"""
            {
              "biomes": [{ "id": "a" }],
              "objects": [
                { "id": "deposit", "footprint": 8, "collider": { "shape": "box", "size": [1, 1, 1] },
                  "outcrop": { "deposits": [{{deposit}}], "mounds": { "small": {{mound}} } } },
                { "id": "block", "collider": { "shape": "box", "size": [1, 1, 1] } },
                { "id": "ball", "collider": { "shape": "sphere", "radius": 0.5 } }
              ]
            }
            """;
        using var mods = new TestMods().Mod("vanilla").File("vanilla", WorldGenData.DefinitionFile, json);
        var data = WorldGenData.Load(mods.Load());
        Assert.Contains(data.Diagnostics, d => d.Code == DiagnosticCodes.WorldGenInvalid && d.JsonPath == path);
    }

    [Fact]
    public void ObjectIdsCarryTheCube()
    {
        Assert.Equal("3.10.-4", new WorldObjectId(3, 10, -4).ToString());
        Assert.Equal("3.10.-4.7", new WorldObjectId(3, 10, -4, 7).ToString());
        Assert.True(WorldObjectId.TryParse("3.10.-4.7", out var id));
        Assert.Equal(new WorldObjectId(3, 10, -4, 7), id);
        Assert.True(WorldObjectId.TryParse("3.10.-4", out id));
        Assert.Equal(0, id.Piece);
        Assert.False(WorldObjectId.TryParse("3.10.-4.0", out _));
        Assert.False(WorldObjectId.TryParse("3.10.-4.-1", out _));
        Assert.False(WorldObjectId.TryParse("3.10.-4.1.2", out _));
        var state = new WorldState(5);
        state.Destroy(new WorldObjectId(3, 10, -4, 7));
        Assert.True(WorldState.FromJson(state.ToJson()).IsDestroyed(new WorldObjectId(3, 10, -4, 7)));
    }

    [Fact]
    public void VanillaDepositsStandOnOpenFlatGroundAsBlocksThatOnlyADrillBreaks()
    {
        var data = VanillaData.Value;
        Assert.Empty(data.Diagnostics);
        var gen = new WorldGenerator(data, 1234, 4096f);
        var blockIds = new[] { "rock_outcrop_small", "rock_outcrop", "rock_outcrop_large", "metal_outcrop_small", "metal_outcrop", "metal_outcrop_large" };
        foreach (var id in blockIds)
        {
            var block = data.Objects[id];
            Assert.Equal([DamageKinds.Drill], block.DamageFrom);
            Assert.Equal(PrimitiveShape.Box, block.Collider!.Shape);
            Assert.NotNull(block.Mesh!.Variants);
        }
        Assert.NotNull(data.Objects["rock_outcrop"].Outcrop);
        Assert.NotNull(data.Objects["metal_outcrop"].Outcrop);
        // Small blocks give one chunk, medium two, and large ones three loose boulders.
        Assert.Equal((1, "stone_chunk"), Breaks(data.Objects["rock_outcrop_small"]));
        Assert.Equal((2, "stone_chunk"), Breaks(data.Objects["rock_outcrop"]));
        Assert.Equal((3, "stone_boulder"), Breaks(data.Objects["rock_outcrop_large"]));
        Assert.Equal((3, "metal_boulder"), Breaks(data.Objects["metal_outcrop_large"]));

        var blocks = gen.ObjectsIn(-1500, -1500, 1500, 1500).Where(o => blockIds.Contains(o.Definition.Id)).ToList();
        var deposits = blocks.GroupBy(o => o.Id with { Piece = 0 }).ToList();
        Assert.True(deposits.Count > 50, $"deposits scattered ({deposits.Count})");
        Assert.Contains(deposits, c => c.Any(p => p.Definition.Id == "metal_outcrop_large"));
        Assert.Contains(deposits, c => c.Any(p => p.Variant == 1));
        Assert.Contains(deposits, c => c.Count() >= 40);
        // Forests keep their deposits only where a neighbouring biome's reach into them carries a few over the border.
        var biomes = deposits.Select(c => gen.BiomeAt(c.First().Position.X, c.First().Position.Z).Id).ToList();
        Assert.True(biomes.Count(b => b == "forest") <= deposits.Count / 20, $"{biomes.Count(b => b == "forest")} of {deposits.Count} deposits in forest");
        var forest = gen.Data.Biomes.First(b => b.Id == "forest").Index;
        Assert.All(deposits, c => Assert.True(gen.BiomeShare(c.First().Position.X, c.First().Position.Z, forest) < 1f));
        Assert.DoesNotContain("warehouse_district", biomes);
        foreach (var deposit in deposits.Take(40))
        {
            var pieces = deposit.ToList();
            Assert.Equal(Enumerable.Range(0, pieces.Count), pieces.Select(p => p.Id.Piece));
            // Each block is found again by its own id, where it was generated.
            foreach (var piece in pieces)
                Assert.Equal(piece, gen.Find(piece.Id));
            // Every block stays inside the deposit's footprint, which keeps POIs, water and other biomes clear of it.
            var rule = data.ScatterRules[deposit.Key.Rule].Object;
            var middle = new Vector2(pieces[0].Position.X, pieces[0].Position.Z);
            Assert.All(pieces, p => Assert.True(Vector2.Distance(new Vector2(p.Position.X, p.Position.Z), middle) <= 2f * rule.Footprint * p.Scale));
            // The lowest block of the deposit stands on (or sunk into) the ground, not above it.
            var lowest = pieces.MinBy(p => p.Position.Y)!;
            Assert.True(gen.HeightAt(lowest.Position.X, lowest.Position.Z) >= lowest.Position.Y, "a deposit floats above the ground");
        }
    }

    private static (int, string?) Breaks(WorldObjectDefinition o) => (o.BreaksInto!.Count, o.BreaksInto.Parts!.Single().Part);

    [Fact]
    public void VanillaChunksAndRodsHangTogether()
    {
        var mods = ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"));
        Assert.DoesNotContain(mods.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var data = WorldGenData.Load(mods);
        foreach (var o in data.Objects.Values.Where(o => o.BreaksInto is not null))
        {
            foreach (var piece in o.BreaksInto!.Parts!)
                Assert.True(mods.FindDefinition(piece.Part!) is { Loose: true }, $"{o.Id} breaks into loose {piece.Part}");
        }
        foreach (var boulder in new[] { "stone_boulder", "metal_boulder" })
        {
            var definition = mods.FindDefinition(boulder)!;
            Assert.True(definition.Loose);
            Assert.Equal(0f, definition.SledgeDamage);
            Assert.Equal(2, definition.BreaksInto!.Count);
        }
        Assert.Equal("stone_chunk", mods.FindDefinition("stone_boulder")!.BreaksInto!.Parts!.Single().Part);
        Assert.Equal("metal_chunk", mods.FindDefinition("metal_boulder")!.BreaksInto!.Parts!.Single().Part);
        foreach (var chunk in new[] { "stone_chunk", "metal_chunk" })
        {
            var definition = mods.FindDefinition(chunk)!;
            Assert.True(definition.Loose);
            Assert.NotNull(definition.Durability);
            Assert.Equal(25f, definition.SledgeDamage);
            Assert.Equal(2, definition.BreaksInto!.Count);
            Assert.All(definition.BreaksInto.Parts!, p => Assert.True(mods.FindDefinition(p.Part!) is { Loose: true, Refine.Count: > 0 }));
        }
        // Stone mostly from stone, metal mostly from metal-rich chunks: 2 chunks of 2 rods is 4 rods a cube.
        Assert.Equal(7f, mods.FindDefinition("stone_chunk")!.BreaksInto!.Parts!.Single(p => p.Part == "stone_rod").Weight);
        Assert.Equal(7f, mods.FindDefinition("metal_chunk")!.BreaksInto!.Parts!.Single(p => p.Part == "metal_rod").Weight);
        Assert.Equal(("scrap_stone_block", 20), Refined(mods, "stone_rod"));
        Assert.Equal(("metal_block", 20), Refined(mods, "metal_rod"));
        Assert.Null(mods.FindDefinition("stone_rod")!.Durability);
        Assert.NotNull(mods.FindDefinition("scrap_stone_block"));
        Assert.NotNull(mods.FindDefinition("scrap_stone_wedge"));
    }

    private static (string?, int?) Refined(ModLoadResult mods, string rod) =>
        mods.FindDefinition(rod)!.Refine!.Select(r => (r.Item, r.Count)).Single();
}
