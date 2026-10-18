using System.Numerics;
using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

/// <summary>Boulder Mining spec §2.3 and §3: rock outcrops of stacked cubes, scattered on open, flat ground.</summary>
public class OutcropTests
{
    private static readonly Lazy<WorldGenData> VanillaData = new(() => WorldGenData.Load(ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"))));

    [Theory]
    [InlineData(1u)]
    [InlineData(7u)]
    [InlineData(123456u)]
    [InlineData(0xDEADBEEFu)]
    public void AnOutcropIsOneConnectedSteppedMassOfCubes(uint salt)
    {
        var outcrop = new OutcropDefinition { Cubes = [6, 20], Height = [1, 4] };
        var cubes = OutcropLayout.Cubes(outcrop, salt);

        Assert.InRange(cubes.Count, 6, 20);
        Assert.Equal(cubes, OutcropLayout.Cubes(outcrop, salt));
        Assert.Equal(cubes.Count, cubes.Distinct().Count());
        Assert.All(cubes, c => Assert.InRange(c.Y, 0, 3));
        Assert.All(cubes, c => Assert.InRange(c.Turn, 0, 3));
        // Every cube stands on the ground or on the cube below.
        Assert.All(cubes.Where(c => c.Y > 0), c => Assert.Contains(cubes, b => b.X == c.X && b.Z == c.Z && b.Y == c.Y - 1));
        // The footprint is connected edge to edge.
        var columns = cubes.Select(c => (c.X, c.Z)).ToHashSet();
        var reached = new HashSet<(int, int)> { (0, 0) };
        var queue = new Queue<(int X, int Z)>([(0, 0)]);
        while (queue.TryDequeue(out var at))
        {
            foreach (var next in new[] { (at.X + 1, at.Z), (at.X - 1, at.Z), (at.X, at.Z + 1), (at.X, at.Z - 1) })
            {
                if (columns.Contains(next) && reached.Add(next))
                    queue.Enqueue(next);
            }
        }
        Assert.Equal(columns.Count, reached.Count);
    }

    [Fact]
    public void ManyOutcropsCoverTheWholeRangeOfSizes()
    {
        var outcrop = new OutcropDefinition { Cubes = [6, 20], Height = [1, 4] };
        var sizes = Enumerable.Range(0, 300).Select(i => OutcropLayout.Cubes(outcrop, Noise.Mix((uint)i)).Count).ToList();
        Assert.Equal(6, sizes.Min());
        Assert.Equal(20, sizes.Max());
        var tallest = Enumerable.Range(0, 300).Select(i => OutcropLayout.Cubes(outcrop, Noise.Mix((uint)i)).Max(c => c.Y)).ToList();
        Assert.Contains(3, tallest);
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
    public void VanillaOutcropsStandOnOpenFlatGroundAsCubesThatOnlyADrillBreaks()
    {
        var data = VanillaData.Value;
        Assert.Empty(data.Diagnostics);
        var gen = new WorldGenerator(data, 1234, 4096f);
        var outcrop = data.Objects["rock_outcrop"];
        Assert.Equal([DamageKinds.Drill], outcrop.DamageFrom);
        Assert.NotNull(outcrop.Outcrop);
        Assert.Equal("stone_chunk", outcrop.BreaksInto!.Parts!.Single().Part);
        Assert.Equal([DamageKinds.Drill], data.Objects["metal_outcrop"].DamageFrom);

        var cubes = gen.ObjectsIn(-1500, -1500, 1500, 1500).Where(o => o.Definition.Outcrop is not null).ToList();
        var clusters = cubes.GroupBy(o => o.Id with { Piece = 0 }).ToList();
        Assert.True(clusters.Count > 50, $"outcrops scattered ({clusters.Count})");
        Assert.Contains(clusters, c => c.First().Definition.Id == "metal_outcrop");
        var biomes = clusters.Select(c => gen.BiomeAt(c.First().Position.X, c.First().Position.Z).Id).ToHashSet();
        Assert.DoesNotContain("forest", biomes);
        Assert.DoesNotContain("warehouse_district", biomes);
        foreach (var cluster in clusters.Take(40))
        {
            var pieces = cluster.ToList();
            Assert.InRange(pieces.Count, 5, 20);
            Assert.Equal(Enumerable.Range(0, pieces.Count), pieces.Select(p => p.Id.Piece));
            // Each cube is found again by its own id, where it was generated.
            foreach (var piece in pieces)
                Assert.Equal(piece, gen.Find(piece.Id));
            // Every cube stays inside the footprint, which is what keeps POIs, water and other biomes clear of it.
            var middle = new Vector2(pieces.Average(p => p.Position.X), pieces.Average(p => p.Position.Z));
            Assert.All(pieces, p => Assert.True(Vector2.Distance(new Vector2(p.Position.X, p.Position.Z), middle) + 0.71f * p.Scale <= p.Definition.Footprint * p.Scale));
            // Cubes stack in whole cubes on one base, which no corner of the footprint floats above.
            var floor = pieces.Min(p => p.Position.Y);
            Assert.All(pieces, p => Assert.True(MathF.Abs((p.Position.Y - floor) / p.Scale - MathF.Round((p.Position.Y - floor) / p.Scale)) < 0.01f));
            foreach (var p in pieces.Where(p => MathF.Abs(p.Position.Y - floor) < 0.01f))
                Assert.True(gen.HeightAt(p.Position.X, p.Position.Z) >= floor, "a base cube's middle is not above the ground");
        }
    }

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
