using System.Numerics;
using Mechanica.Core.Data;
using Mechanica.Core.Settings;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

public class FloraTests
{
    private const float ChunkSize = 64f;

    private static readonly Lazy<WorldGenData> VanillaData = new(() => WorldGenData.Load(ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods"))));

    [Fact]
    public void EveryKindHasSeveralModels()
    {
        Assert.Equal(FloraModels.All.Count, FloraModels.All.Select(m => m.Id).Distinct().Count());
        Assert.InRange(FloraModels.OfKind(FloraKind.Grass).Count, 4, 8);
        Assert.InRange(FloraModels.OfKind(FloraKind.GroundCover).Count, 3, 8);
        Assert.InRange(FloraModels.OfKind(FloraKind.Bush).Count, 4, 8);
        Assert.NotNull(FloraModels.Find("bush_berry"));
        Assert.Null(FloraModels.Find("bush_square"));
    }

    [Fact]
    public void ModelMeshesAreWellFormed()
    {
        foreach (var model in FloraModels.All)
        {
            foreach (var mesh in new[] { model.Mesh, model.Lod })
            {
                Assert.True(mesh.Triangles > 0, model.Id);
                Assert.Equal(mesh.Positions.Length, mesh.Triangles * 3);
                Assert.Equal(mesh.Positions.Length, mesh.Normals.Length);
                Assert.Equal(mesh.Positions.Length, mesh.Colors.Length);
                Assert.Equal(mesh.Positions.Length, mesh.Uvs.Length);
                Assert.All(mesh.Positions, p => Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)));
                Assert.All(mesh.Normals, n => Assert.Equal(1f, n.Length(), 3));
                Assert.All(mesh.Colors, c => Assert.True(c.X is >= 0f and <= 1f && c.Y is >= 0f and <= 1f && c.Z is >= 0f and <= 1f && c.W is 0f or 1f, model.Id));
                Assert.All(mesh.Uvs, uv => Assert.True(uv.X is >= 0f and <= 1f && uv.Y is >= 0f and <= 1f, model.Id));
                // Standing on its ground point: no more than a little sunk into it.
                Assert.True(mesh.Min.Y > -0.15f, $"{model.Id} reaches {mesh.Min.Y} below the ground");
            }
            // Front faces wind clockwise seen from the side their normal points to (Godot's convention).
            for (var t = 0; t < model.Mesh.Positions.Length; t += 3)
            {
                var p = model.Mesh.Positions;
                Assert.True(Vector3.Dot(Vector3.Cross(p[t + 1] - p[t], p[t + 2] - p[t]), model.Mesh.Normals[t]) < 0f, model.Id);
            }
            Assert.True(model.Lod.Triangles < model.Mesh.Triangles, $"{model.Id}: the far mesh is lighter ({model.Lod.Triangles} vs {model.Mesh.Triangles})");
        }
    }

    [Theory]
    [InlineData(FloraKind.Grass, 0.1f, 1.0f, 0.6f)]
    [InlineData(FloraKind.GroundCover, 0.05f, 0.45f, 0.4f)]
    [InlineData(FloraKind.Bush, 0.4f, 1.3f, 1.1f)]
    public void ModelsAreSizedForTheirKind(FloraKind kind, float minHeight, float maxHeight, float maxReach)
    {
        foreach (var model in FloraModels.OfKind(kind))
        {
            var mesh = model.Mesh;
            Assert.InRange(mesh.Max.Y, minHeight, maxHeight);
            var reach = mesh.Positions.Max(p => MathF.Sqrt(p.X * p.X + p.Z * p.Z));
            Assert.True(reach <= maxReach, $"{model.Id} reaches {reach} m out");
        }
    }

    [Fact]
    public void VanillaBiomesGrowFlora()
    {
        var data = VanillaData.Value;
        Assert.Empty(data.Diagnostics);
        var meadow = data.FloraByBiome[data.Biomes.First(b => b.Id == "meadow").Index];
        Assert.Contains(meadow, f => f.Kind == FloraKind.Grass);
        Assert.Contains(meadow, f => f.Kind == FloraKind.GroundCover);
        Assert.Contains(meadow, f => f.Kind == FloraKind.Bush);
        // The harvestable bush is drawn as the berry bush and is walk-through; decorative bushes leave its model alone.
        Assert.Equal("bush_berry", data.Objects["bush"].Model);
        Assert.False(data.Objects["bush"].Solid);
        Assert.True(data.Objects["tree"].Solid);
        Assert.DoesNotContain(data.FloraRules, f => f.Models.Any(m => m.Id == "bush_berry"));
    }

    [Fact]
    public void GrowingIsDeterministicAndStaysOnDryOpenGround()
    {
        var a = new WorldGenerator(VanillaData.Value, 1234, 4096f);
        var b = new WorldGenerator(VanillaData.Value, 1234, 4096f);
        var total = 0;
        for (var cx = -3; cx <= 3; cx++)
        {
            for (var cz = -3; cz <= 3; cz++)
            {
                var coord = new ChunkCoord(cx, cz);
                var chunk = a.GenerateChunk(coord, ChunkSize);
                var grass = FloraScatter.Grow(a, chunk, FloraKind.Grass, 1f);
                Assert.Equal(grass, FloraScatter.Grow(b, b.GenerateChunk(coord, ChunkSize), FloraKind.Grass, 1f));
                var (ox, oz) = coord.Origin(ChunkSize);
                foreach (var plant in grass.Concat(FloraScatter.Grow(a, chunk, FloraKind.Bush, 1f)))
                {
                    Assert.InRange(plant.Position.X, ox, ox + ChunkSize);
                    Assert.InRange(plant.Position.Z, oz, oz + ChunkSize);
                    Assert.True(plant.Position.Y >= a.SeaLevel, "nothing grows under the sea");
                    Assert.Equal(a.HeightAt(plant.Position.X, plant.Position.Z), plant.Position.Y, 0.35f);
                    Assert.Empty(a.PoisAt(plant.Position.X, plant.Position.Z));
                    Assert.InRange(plant.Rank, 0f, 1f);
                }
                total += grass.Count;
            }
        }
        Assert.True(total > 10_000, $"grass grows round the spawn ({total} tufts)");
    }

    [Fact]
    public void LowerLevelsDrawASubsetAndOffDrawsNothing()
    {
        var settings = new FloraSettings();
        var gen = new WorldGenerator(Meadow(grassDensity: 2f), 7, 1024f);
        var chunk = gen.GenerateChunk(new ChunkCoord(1, 1), ChunkSize);
        var all = FloraScatter.Grow(gen, chunk, FloraKind.Grass, settings.MaxDensity);
        // A full patch of 2 tufts per m² over a 64 m chunk, grown at the densest level.
        Assert.InRange(all.Count, 0.95f * 2f * ChunkSize * ChunkSize * settings.MaxDensity, 1.05f * 2f * ChunkSize * ChunkSize * settings.MaxDensity);
        var counts = new[] { FoliageLevel.Off, FoliageLevel.Low, FoliageLevel.Medium, FoliageLevel.High, FoliageLevel.VeryHigh }
            .Select(level => all.Count(p => p.Rank < settings.Keep(level))).ToList();
        Assert.Equal(0, counts[0]);
        Assert.True(counts.SequenceEqual(counts.Order()), string.Join(", ", counts));
        Assert.Equal(all.Count, counts[^1]);
        Assert.Equal(settings.High.Density / settings.MaxDensity, counts[3] / (float)all.Count, 0.03f);
        Assert.Empty(FloraScatter.Grow(gen, chunk, FloraKind.Grass, 0f));
        Assert.Empty(FloraScatter.Grow(gen, chunk, FloraKind.GroundCover, 1f));
    }

    [Fact]
    public void CoverageMakesPatches()
    {
        var gen = new WorldGenerator(Meadow(grassDensity: 1f, coverage: 0.4f), 7, 1024f);
        var count = 0;
        for (var c = 0; c < 4; c++)
            count += FloraScatter.Grow(gen, gen.GenerateChunk(new ChunkCoord(c, 0), ChunkSize), FloraKind.Grass, 1f).Count;
        var full = 4 * ChunkSize * ChunkSize;
        Assert.InRange(count, 0.15f * full, 0.65f * full);
    }

    [Fact]
    public void TintsAndModelsComeFromTheRule()
    {
        var gen = new WorldGenerator(Meadow(grassDensity: 1f, models: ["grass_tall"], colors: ["#80ff00"]), 7, 1024f);
        var grass = FloraScatter.Grow(gen, gen.GenerateChunk(default, ChunkSize), FloraKind.Grass, 1f);
        Assert.NotEmpty(grass);
        Assert.All(grass, p => Assert.Equal("grass_tall", p.Model.Id));
        Assert.All(grass, p => Assert.InRange(p.Tint.X / (128f / 255f), 0.9f, 1.1f));
    }

    [Theory]
    [InlineData("""{ "biomes": [{ "id": "a", "flora": [{ "density": 1 }] }] }""", "$.biomes[0].flora[0].kind")]
    [InlineData("""{ "biomes": [{ "id": "a", "flora": [{ "kind": "grass", "density": 500 }] }] }""", "$.biomes[0].flora[0].density")]
    [InlineData("""{ "biomes": [{ "id": "a", "flora": [{ "kind": "grass", "density": 1, "coverage": 2 }] }] }""", "$.biomes[0].flora[0].coverage")]
    [InlineData("""{ "biomes": [{ "id": "a", "flora": [{ "kind": "grass", "density": 1, "models": ["oak"] }] }] }""", "$.biomes[0].flora[0].models[0]")]
    [InlineData("""{ "biomes": [{ "id": "a", "flora": [{ "kind": "grass", "density": 1, "models": ["bush_round"] }] }] }""", "$.biomes[0].flora[0].models[0]")]
    [InlineData("""{ "biomes": [{ "id": "a", "flora": [{ "kind": "bush", "density": 1, "colors": ["green"] }] }] }""", "$.biomes[0].flora[0].colors[0]")]
    [InlineData("""{ "biomes": [{ "id": "a", "flora": [{ "kind": "bush", "density": 1, "patch_size": 0 }] }] }""", "$.biomes[0].flora[0].patch_size")]
    [InlineData("""{ "biomes": [{ "id": "a" }], "objects": [{ "id": "bush", "collider": { "shape": "sphere", "radius": 1 }, "model": "hedge" }] }""", "$.objects[0].model")]
    public void BadFloraGivesAClearError(string json, string path)
    {
        using var mods = new TestMods().Mod("vanilla").File("vanilla", WorldGenData.DefinitionFile, json);
        var data = WorldGenData.Load(mods.Load());
        Assert.Contains(data.Diagnostics, d => d.Code == DiagnosticCodes.WorldGenInvalid && d.JsonPath == path);
    }

    [Fact]
    public void ShippedFloraSettingsParse()
    {
        var settings = FloraSettings.FromJson(File.ReadAllText(Path.Combine(TestMods.RepoRoot, "game", "data", "flora.json")));
        Assert.Equal(1f, settings.Keep(FoliageLevel.VeryHigh));
        Assert.Equal(0f, settings.Keep(FoliageLevel.Off));
        Assert.Equal(0f, settings.Distance(FloraKind.Grass, FoliageLevel.Off));
        Assert.True(settings.Distance(FloraKind.Grass, FoliageLevel.Low) < settings.Distance(FloraKind.Grass, FoliageLevel.VeryHigh));
        Assert.True(settings.Distance(FloraKind.Bush) > settings.Distance(FloraKind.Grass));
        Assert.Equal(0f, settings.Low.Wind);
    }

    [Theory]
    [InlineData("""{ "grass_distance": -1 }""", "grass_distance")]
    [InlineData("""{ "low": { "density": -1 } }""", "low.density")]
    [InlineData("""{ "wind_direction": [0, 0] }""", "wind_direction")]
    [InlineData("""{ "grass_distanse": 10 }""", "grass_distanse")]
    public void BadFloraSettingsAreRejected(string json, string message)
    {
        var e = Assert.Throws<FormatException>(() => FloraSettings.FromJson(json));
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void FoliageLevelsAreSavedAndClamped()
    {
        var settings = new UserSettings { Graphics = new GraphicsSettings { Grass = FoliageLevel.VeryHigh, Bushes = FoliageLevel.Off } };
        var json = settings.ToJson();
        Assert.Contains("\"grass\": \"very_high\"", json);
        var read = UserSettings.Parse(json)!;
        Assert.Equal(FoliageLevel.VeryHigh, read.Graphics.Foliage(FloraKind.Grass));
        Assert.Equal(FoliageLevel.High, read.Graphics.Foliage(FloraKind.GroundCover));
        Assert.Equal(FoliageLevel.Off, read.Graphics.Foliage(FloraKind.Bush));
        // The quality preset leaves foliage alone.
        Assert.Equal(FoliageLevel.VeryHigh, read.Graphics.WithPreset(Quality.Low).Grass);
        Assert.Equal(FoliageLevel.High, (new GraphicsSettings { Grass = (FoliageLevel)42 }).Clamped().Grass);
    }

    // A flat world of one meadow biome growing only grass.
    private static WorldGenData Meadow(float grassDensity, float coverage = 1f, List<string>? models = null, List<string>? colors = null) =>
        WorldGenData.FromDefinition(new WorldGenDefinition
        {
            Terrain = new TerrainDefinition { CoastWidth = 0f, CoastBiomeWidth = 0f, SeaLevel = -100f, SeaFloor = -100f },
            Biomes =
            [
                new BiomeDefinition
                {
                    Id = "meadow", Weight = 1f, Color = "#79a35a",
                    Flora = [new FloraRule { Kind = FloraKind.Grass, Density = grassDensity, Coverage = coverage, Models = models, Colors = colors }],
                },
            ],
        });
}
