using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

/// <summary>The bumps and the biomes' patches (sub-areas): rocky knolls, groves, hollows and flower meadows.</summary>
public class PatchTests
{
    private const float ChunkSize = 64f;
    private const float WorldSize = 4096f;
    private const int Layers = TerrainChunkData.GroundLayers;

    private const string Knoll = """{ "layer": "rocks", "scale": 70, "cover": 0.2, "ground": "dirt", "paint": 0.8, "height": 3 }""";
    private const string Flowers = """{ "layer": "flowers", "scale": 60, "cover": 0.2 }""";
    private const string Rocks = """{ "object": "stone", "spacing": 6, "chance": 1, "cluster": { "layer": "rocks", "scale": 70, "cover": 0.2 } }""";
    private const string FlowerFlora = """{ "kind": "ground_cover", "density": 2, "layer": "flowers" }""";

    [Fact]
    public void AKnollRaisesAndPaintsItsPatchAndNothingElse()
    {
        var plain = World("", Rocks, "");
        var knolls = World($"{Knoll}, {Flowers}", Rocks, "");
        var raised = 0;
        var level = 0;
        for (var i = 0; i < 400; i++)
        {
            float x = (i % 20) * 37f - 300f, z = (i / 20) * 41f - 300f;
            var w = knolls.PatchWeight(0, 0, x, z);
            var rise = knolls.HeightAt(x, z) - plain.HeightAt(x, z);
            Assert.InRange(rise, -1e-3f, 3.001f);
            if (w <= 0f)
            {
                Assert.Equal(0f, rise, 3);
                level++;
            }
            else if (w >= 1f)
            {
                Assert.True(rise > 0.5f, $"({x}, {z}) is deep in a knoll but only {rise} m up");
                raised++;
            }
        }
        Assert.True(raised > 10 && level > 100, $"{raised} spots in knolls, {level} outside");

        var dirt = 0;
        var outside = 0;
        foreach (var coord in new[] { new ChunkCoord(0, 0), new ChunkCoord(-3, 2), new ChunkCoord(4, -1), new ChunkCoord(2, 3) })
        {
            var chunk = knolls.GenerateChunk(coord, ChunkSize);
            var (ox, oz) = coord.Origin(ChunkSize);
            for (var k = 0; k < chunk.Samples * chunk.Samples; k++)
            {
                float x = ox + k % chunk.Samples * chunk.Resolution, z = oz + k / chunk.Samples * chunk.Resolution;
                var w = knolls.PatchWeight(0, 0, x, z);
                Assert.Equal(0.8f * w, chunk.Grounds[k * Layers + 1], 3);
                if (w >= 1f)
                    dirt++;
                else if (w <= 0f)
                    outside++;
            }
        }
        Assert.True(dirt > 0 && outside > 0);
    }

    [Fact]
    public void ALayersObjectsStandOnItsPatchAndItsFloraKeepsToIt()
    {
        var world = World($"{Knoll}, {Flowers}", Rocks, $", {FlowerFlora}");
        var stones = world.ObjectsIn(-600f, -600f, 600f, 600f).ToList();
        Assert.NotEmpty(stones);
        // The rules' dice fray the patch edge a little; the stones still stand on the knolls' ground.
        var on = stones.Count(s => world.PatchWeight(0, 0, s.Position.X, s.Position.Z) > 0f);
        Assert.True(on >= stones.Count * 0.9f, $"{on} of {stones.Count} stones on the knolls");

        var plants = 0;
        foreach (var coord in new[] { new ChunkCoord(0, 0), new ChunkCoord(-3, 2), new ChunkCoord(4, -1), new ChunkCoord(2, 3) })
        {
            var chunk = world.GenerateChunk(coord, ChunkSize);
            foreach (var plant in FloraScatter.Grow(world, chunk, FloraKind.GroundCover, 1f))
            {
                // Grown per 1 m cell from the cell's middle, so allow the patch's weight there.
                Assert.True(world.PatchWeight(0, 1, plant.Position.X, plant.Position.Z) > 0f
                    || world.PatchWeight(0, 1, MathF.Floor(plant.Position.X) + 0.5f, MathF.Floor(plant.Position.Z) + 0.5f) > 0f);
                plants++;
            }
        }
        Assert.True(plants > 0);
    }

    [Fact]
    public void BumpsRollTheGroundButNotAFlatBiome()
    {
        var flat = World("", "", "");
        var bumpy = World("", "", "", bumps: """, "bumps": { "height": 2.5, "scale": 50 }""");
        var lake = World("", "", "", bumps: """, "bumps": { "height": 2.5, "scale": 50 }""", biomeBumps: 0f);
        var most = 0f;
        for (var i = 0; i < 200; i++)
        {
            float x = i * 13.7f - 1400f, z = i * 7.3f - 700f;
            var d = bumpy.HeightAt(x, z) - flat.HeightAt(x, z);
            Assert.InRange(d, -2.5f, 2.5f);
            most = MathF.Max(most, MathF.Abs(d));
            Assert.Equal(flat.HeightAt(x, z), lake.HeightAt(x, z), 4);
            // Gentle enough to drive over.
            Assert.True(bumpy.SlopeAt(x, z) < 0.5f);
        }
        Assert.True(most > 0.8f, $"the bumps only reach {most} m");
    }

    [Fact]
    public void PatchesAreValidated()
    {
        var data = Load("""{ "layer": "rocks", "scale": 50, "cover": 0.2, "ground": "mud", "height": 30 }""", Rocks,
            """, { "kind": "grass", "density": 1, "layer": "nope" }""");
        Assert.Contains(data.Diagnostics, d => d.JsonPath == "$.biomes[0].patches[0].ground" && d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(data.Diagnostics, d => d.JsonPath == "$.biomes[0].patches[0].height" && d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(data.Diagnostics, d => d.JsonPath == "$.biomes[0].flora[1].layer" && d.Severity == DiagnosticSeverity.Error);
        // The rule's own patches are 70 m: off the patch's 50 m ones.
        Assert.Contains(data.Diagnostics, d => d.JsonPath == "$.biomes[0].scatter[0].cluster" && d.Severity == DiagnosticSeverity.Warning);
    }

    private static WorldGenerator World(string patches, string scatter, string flora, string bumps = "", float biomeBumps = 1f)
    {
        var data = Load(patches, scatter, flora, bumps, biomeBumps);
        Assert.DoesNotContain(data.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return new WorldGenerator(data, 7, WorldSize);
    }

    private static WorldGenData Load(string patches, string scatter, string flora, string bumps = "", float biomeBumps = 1f)
    {
        using var mods = new TestMods().Mod("vanilla").File("vanilla", WorldGenData.DefinitionFile, $$"""
            {
              "terrain": { "coast_width": 0, "coast_biome_width": 0, "sea_level": -500, "sea_floor": -500, "noise_scale": 400 {{bumps}} },
              "grounds": [
                { "id": "grass", "texture": "g.png" },
                { "id": "dirt", "texture": "d.png" }
              ],
              "biomes": [
                { "id": "a", "relief": 2, "ground": "grass", "bumps": {{biomeBumps}}, "patches": [{{patches}}], "scatter": [{{scatter}}],
                  "flora": [{ "kind": "grass", "density": 1 } {{flora}}] }
              ],
              "objects": [{ "id": "stone", "footprint": 0.5, "collider": { "shape": "sphere", "radius": 0.5 } }]
            }
            """);
        return WorldGenData.Load(mods.Load());
    }
}
