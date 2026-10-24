using System.Numerics;
using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

public class GroundTests
{
    private const float ChunkSize = 64f;
    private const float WorldSize = 4096f;
    private const int Layers = TerrainChunkData.GroundLayers;

    [Fact]
    public void VanillaGroundsHaveTheirTextures()
    {
        var data = WorldGenData.Load(ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));
        Assert.DoesNotContain(data.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.InRange(data.Grounds.Count, 5, GroundDefinition.MaxGrounds);
        foreach (var ground in data.Grounds)
        {
            Assert.True(File.Exists(ground.TextureFile), ground.TextureFile);
            // A square 1024 px PNG, so every layer of the texture array matches without resampling.
            var header = File.ReadAllBytes(ground.TextureFile!)[..24];
            Assert.Equal("PNG", System.Text.Encoding.ASCII.GetString(header, 1, 3));
            Assert.Equal(1024, (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19]);
            Assert.Equal(1024, (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23]);
        }
        Assert.All(data.BiomeGrounds, g => Assert.True(g >= 0));
        Assert.True(data.SlopeGround >= 0);
        var crash = data.Pois.Single(p => p.Prefab.Id == "crash_site").Prefab;
        Assert.NotNull(crash.Flora);
        Assert.Contains(crash.Ground!, p => data.Grounds[data.GroundIndex(p.Ground!)].Flora == false);
    }

    [Fact]
    public void ABiomesGroundCoversItAndWeightsNeverExceedOne()
    {
        var generator = World(groundA: "grass", groundB: null, relief: 0f);
        var chunk = generator.GenerateChunk(new ChunkCoord(1, 1), ChunkSize);
        Assert.Equal(chunk.Samples * chunk.Samples * Layers, chunk.Grounds.Length);
        for (var k = 0; k < chunk.Samples * chunk.Samples; k++)
        {
            var weights = chunk.Grounds.AsSpan(k * Layers, Layers).ToArray();
            Assert.All(weights, w => Assert.InRange(w, 0f, 1.0001f));
            Assert.InRange(weights.Sum(), 0f, 1.0001f);
            Assert.Equal(1f, weights[0], 3);
        }
    }

    [Fact]
    public void ABiomeWithoutGroundKeepsItsColourAndAWorldWithoutGroundsHasNoWeights()
    {
        var chunk = World(groundA: null, groundB: null, relief: 0f).GenerateChunk(new ChunkCoord(1, 1), ChunkSize);
        Assert.All(chunk.Grounds, w => Assert.Equal(0f, w));
        var flat = new WorldGenerator(WorldGenData.Flat, 1, WorldSize).GenerateChunk(new ChunkCoord(0, 0), ChunkSize);
        Assert.Empty(flat.Grounds);
    }

    [Fact]
    public void SteepSlopesTakeTheSlopeGround()
    {
        var generator = World(groundA: "grass", groundB: null, relief: 90f);
        var steep = 0;
        var gentle = 0;
        foreach (var coord in new[] { new ChunkCoord(3, 2), new ChunkCoord(-4, 5), new ChunkCoord(6, -3) })
        {
            var chunk = generator.GenerateChunk(coord, ChunkSize);
            for (var k = 0; k < chunk.Samples * chunk.Samples; k++)
            {
                var up = chunk.Normals[k].Y;
                var slope = MathF.Sqrt(1f - up * up) / up;
                var rock = chunk.Grounds[k * Layers + 1];
                if (slope > 1.1f)
                {
                    Assert.Equal(1f, rock, 3);
                    Assert.Equal(0f, chunk.Grounds[k * Layers], 3);
                    steep++;
                }
                else if (slope < 0.7f)
                {
                    Assert.Equal(0f, rock, 3);
                    gentle++;
                }
            }
        }
        Assert.True(steep > 50 && gentle > 50, $"steep {steep}, gentle {gentle}");
    }

    [Fact]
    public void APoiPaintsItsGroundAndGrowsItsFloraOutsideTheBareMiddle()
    {
        var generator = World(groundA: "grass", groundB: null, relief: 0f, poi: true);
        var chunk = generator.GenerateChunk(new ChunkCoord(0, 0), ChunkSize);
        float Burnt(float x, float z) => chunk.Grounds[((int)z * chunk.Samples + (int)x) * Layers + 2];
        Assert.Equal(1f, Burnt(32f, 32f), 3);
        Assert.Equal(0f, Burnt(32f, 56f), 3);
        Assert.Equal(0f, chunk.Grounds[(32 * chunk.Samples + 32) * Layers], 3);

        var grass = FloraScatter.Grow(generator, chunk, FloraKind.Grass, 1f);
        Assert.NotEmpty(grass);
        var centre = new Vector2(32f, 32f);
        float Distance(FloraInstance p) => Vector2.Distance(new Vector2(p.Position.X, p.Position.Z), centre);
        Assert.DoesNotContain(grass, p => Distance(p) < 8f);
        Assert.Contains(grass, p => Distance(p) < 20f);
        // Next to the burnt ground the grass is charred; farther out it keeps its own colour.
        var charred = new Vector3(0.36f, 0.30f, 0.14f);
        Assert.Contains(grass, p => Distance(p) < 13f && Vector3.Distance(p.Tint, charred) < 0.05f);
        Assert.All(grass.Where(p => Distance(p) > 22f), p => Assert.True(Vector3.Distance(p.Tint, charred) > 0.2f));
    }

    [Fact]
    public void APoiThatNamesNoFloraStaysBare()
    {
        var generator = World(groundA: "grass", groundB: null, relief: 0f, poi: true, poiFlora: false);
        var chunk = generator.GenerateChunk(new ChunkCoord(0, 0), ChunkSize);
        var grass = FloraScatter.Grow(generator, chunk, FloraKind.Grass, 1f);
        Assert.DoesNotContain(grass, p => Vector2.Distance(new Vector2(p.Position.X, p.Position.Z), new Vector2(32f, 32f)) < 25f);
    }

    // A one-biome world with grass, rock and burnt grounds, optionally a POI at (32, 32) that burns its middle.
    private static WorldGenerator World(string? groundA, string? groundB, float relief, bool poi = false, bool poiFlora = true)
    {
        var mods = new TestMods().Mod("vanilla").File("vanilla", WorldGenData.DefinitionFile, $$"""
            {
              "terrain": { "coast_width": 0, "coast_biome_width": 0, "sea_level": -500, "sea_floor": -500, "noise_scale": 90 },
              "grounds": [
                { "id": "grass", "texture": "g.png" },
                { "id": "rock", "texture": "r.png" },
                { "id": "burnt", "texture": "b.png", "flora": false, "flora_tint": "#5c4d24" }
              ],
              "slope_ground": { "ground": "rock", "from": 0.7, "to": 1.1 },
              "biomes": [
                { "id": "a", "relief": {{relief}}, {{(groundA is null ? "" : $"\"ground\": \"{groundA}\",")}}
                  "flora": [{ "kind": "grass", "density": 2 }] }
                {{(groundB is null ? "" : $", {{ \"id\": \"b\", \"ground\": \"{groundB}\" }}")}}
              ]
            }
            """);
        if (poi)
        {
            mods.File("vanilla", "pois/camp.json", $$"""
                {
                  "id": "camp", "radius": 30, "placement": { "fixed": [32, 32] },
                  "terrain": [{ "op": "flatten", "radius": 20, "blend": 8 }],
                  "ground": [{ "ground": "burnt", "radius": 8, "blend": 4 }]
                  {{(poiFlora ? ", \"flora\": \"a\"" : "")}}
                }
                """);
        }
        using (mods)
        {
            var data = WorldGenData.Load(mods.Load());
            Assert.DoesNotContain(data.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
            return new WorldGenerator(data, 7, WorldSize);
        }
    }
}
