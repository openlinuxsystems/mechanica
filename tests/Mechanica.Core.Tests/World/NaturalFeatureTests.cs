using System.Numerics;
using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

public class NaturalFeatureTests
{
    // A level world with one pond, and a tree, a lily pad and a reed rule over all of it.
    private static WorldGenerator Pond(bool wild)
    {
        using var mods = new TestMods().Mod("vanilla").File("vanilla", WorldGenData.DefinitionFile, """
            { "biomes": [{ "id": "a", "height": 10, "scatter": [
                { "object": "tree", "spacing": 4, "chance": 1 },
                { "object": "lily", "spacing": 3, "chance": 1, "water": "surface" },
                { "object": "reed", "spacing": 2, "chance": 1, "water": "shallows" }] }],
              "liquids": [{ "id": "water", "water": true }],
              "objects": [{ "id": "tree", "collider": { "shape": "sphere", "radius": 0.5 }, "footprint": 0.5, "sink": 0.1 },
                          { "id": "lily", "collider": { "shape": "sphere", "radius": 0.5 }, "footprint": 0.5, "solid": false },
                          { "id": "reed", "collider": { "shape": "sphere", "radius": 0.5 }, "footprint": 0.5, "solid": false }] }
            """).File("vanilla", "pois/pond.json", $$"""
            { "id": "pond", "radius": 40, "wild": {{(wild ? "true" : "false")}}, "placement": { "fixed": [0, 0] },
              "terrain": [{ "op": "flatten", "radius": 30, "blend": 10 }, { "op": "basin", "radius": 10, "blend": 8, "depth": 2, "wobble": 0.3 }],
              "pools": [{ "liquid": "water", "radius": 28, "level": -0.5 }] }
            """);
        var data = WorldGenData.Load(mods.Load());
        Assert.Empty(data.Diagnostics);
        return new WorldGenerator(data, 5, 1024f);
    }

    [Fact]
    public void WildPondsHoldWaterWithLiliesOnItAndReedsAlongItsEdge()
    {
        var gen = Pond(wild: true);
        var pond = gen.Pois.Single();
        var level = pond.PadHeight - 0.5f;
        // Pond water stands at the pool's level over the basin; everywhere else the sea's level counts.
        Assert.Equal(level, gen.WaterLevelAt(pond.Centre.X, pond.Centre.Y), 3);
        Assert.Equal(gen.SeaLevel, gen.WaterLevelAt(pond.Centre.X + 200f, pond.Centre.Y));
        Assert.True(gen.LiquidAt(new Vector3(pond.Centre.X, level - 0.2f, pond.Centre.Y))!.Liquid.Water);

        var objects = gen.ObjectsIn(-60f, -60f, 60f, 60f).ToList();
        var lilies = objects.Where(o => o.Definition.Id == "lily").ToList();
        var reeds = objects.Where(o => o.Definition.Id == "reed").ToList();
        var trees = objects.Where(o => o.Definition.Id == "tree").ToList();
        Assert.True(lilies.Count >= 10, $"lilies ({lilies.Count})");
        Assert.True(reeds.Count >= 10, $"reeds ({reeds.Count})");
        foreach (var lily in lilies)
        {
            // Floating on the surface, over water deep enough to float in.
            Assert.Equal(level, lily.Position.Y, 3);
            Assert.InRange(level - gen.HeightAt(lily.Position.X, lily.Position.Z), 0.5f, 3f);
        }
        foreach (var reed in reeds)
            Assert.InRange(gen.HeightAt(reed.Position.X, reed.Position.Z), level - 0.6f, level + 0.3f);
        // The pond is wild: trees grow on its footprint, but only on dry ground.
        Assert.Contains(trees, t => Vector2.Distance(new Vector2(t.Position.X, t.Position.Z), pond.Centre) < pond.Radius);
        Assert.All(trees, t => Assert.True(gen.HeightAt(t.Position.X, t.Position.Z) >= level + 0.2f, $"tree in the water at {t.Position}"));
        // Nothing of the water rules on dry land away from the pond.
        Assert.DoesNotContain(lilies.Concat(reeds), o => Vector2.Distance(new Vector2(o.Position.X, o.Position.Z), pond.Centre) > 30f);
    }

    [Fact]
    public void ABuiltPoiKeepsItsFootprintClearAndAWildOneDoesNot()
    {
        static int Inside(WorldGenerator gen) =>
            gen.ObjectsIn(-60f, -60f, 60f, 60f).Count(o => Vector2.Distance(new Vector2(o.Position.X, o.Position.Z), gen.Pois[0].Centre) < gen.Pois[0].Radius);
        Assert.Equal(0, Inside(Pond(wild: false)));
        Assert.True(Inside(Pond(wild: true)) > 50);
    }

    [Fact]
    public void StripBasinsCutRavinesAndRaisesLiftMesas()
    {
        using var mods = new TestMods().Mod("vanilla").File("vanilla", WorldGenData.DefinitionFile, """
            { "biomes": [{ "id": "a", "height": 20 }] }
            """).File("vanilla", "pois/ravine.json", """
            { "id": "ravine", "radius": 70, "wild": true, "placement": { "fixed": [-200, 0] },
              "terrain": [{ "op": "basin", "at": [-40, 0], "to": [40, 0], "radius": 4, "blend": 16, "depth": 8, "wobble": 0.2 }] }
            """).File("vanilla", "pois/mesa.json", """
            { "id": "mesa", "radius": 40, "wild": true, "placement": { "fixed": [200, 0] },
              "terrain": [{ "op": "raise", "radius": 18, "blend": 10, "height": 15 }] }
            """);
        var data = WorldGenData.Load(mods.Load());
        Assert.Empty(data.Diagnostics);
        var gen = new WorldGenerator(data, 2, 1024f);
        var ravine = gen.Pois.Single(p => p.Poi.Prefab.Id == "ravine");
        var mesa = gen.Pois.Single(p => p.Poi.Prefab.Id == "mesa");
        // The floor runs the whole length of the strip, and the banks either side and the ground past its ends stand above it.
        foreach (var along in new[] { -38f, -20f, 0f, 20f, 38f })
        {
            var p = ravine.ToWorld(new Vector3(along, 0f, 0f));
            Assert.Equal(ravine.PadHeight - 8f, gen.HeightAt(p.X, p.Z), 1);
        }
        var bank = ravine.ToWorld(new Vector3(0f, 0f, 30f));
        Assert.True(gen.HeightAt(bank.X, bank.Z) > ravine.PadHeight - 0.5f, $"bank {gen.HeightAt(bank.X, bank.Z)}");
        var beyond = ravine.ToWorld(new Vector3(68f, 0f, 0f));
        Assert.True(gen.HeightAt(beyond.X, beyond.Z) > ravine.PadHeight - 0.5f, $"beyond {gen.HeightAt(beyond.X, beyond.Z)}");
        // A mesa's top is level at its height over the pad, and its foot is back down near the pad.
        Assert.Equal(mesa.PadHeight + 15f, gen.HeightAt(mesa.Centre.X, mesa.Centre.Y), 2);
        Assert.Equal(mesa.PadHeight + 15f, gen.HeightAt(mesa.Centre.X + 12f, mesa.Centre.Y), 2);
        Assert.True(gen.HeightAt(mesa.Centre.X + 38f, mesa.Centre.Y) < mesa.PadHeight + 3f, $"foot {gen.HeightAt(mesa.Centre.X + 38f, mesa.Centre.Y)}");
    }

    [Fact]
    public void RoadsideRulesLineTheRoadFacingIt()
    {
        using var mods = new TestMods().Mod("vanilla").File("vanilla", WorldGenData.DefinitionFile, """
            { "biomes": [{ "id": "a", "height": 5, "scatter": [
                { "object": "pole", "spacing": 40, "chance": 1, "roadside": { "gap": 4, "side": "left" } },
                { "object": "sign", "spacing": 60, "chance": 1, "roadside": { "gap": 3 } }] }],
              "roads": {},
              "sites": [{ "id": "home", "poi": "hut", "at": [0.3, 0.0], "road": "road" }, { "id": "away", "poi": "hut", "at": [-0.3, 0.05], "road": "road" }],
              "objects": [{ "id": "pole", "collider": { "shape": "sphere", "radius": 0.5 }, "footprint": 0.5 },
                          { "id": "sign", "collider": { "shape": "sphere", "radius": 0.5 }, "footprint": 0.5 }] }
            """).File("vanilla", "pois/hut.json", """{ "id": "hut", "radius": 10, "placement": {}, "terrain": [{ "op": "flatten", "radius": 6, "blend": 4 }] }""");
        var data = WorldGenData.Load(mods.Load());
        Assert.Empty(data.Diagnostics);
        var gen = new WorldGenerator(data, 3, 2048f);
        Assert.True(gen.Roads.LengthOf(RoadKind.Road) > 1000f);
        var all = gen.ObjectsIn(-1024f, -1024f, 1024f, 1024f).ToList();
        var poles = all.Where(o => o.Definition.Id == "pole").ToList();
        var signs = all.Where(o => o.Definition.Id == "sign").ToList();
        // About one per cell the road crosses.
        Assert.True(poles.Count >= gen.Roads.LengthOf(RoadKind.Road) / 40f * 0.6f, $"poles ({poles.Count})");
        Assert.True(signs.Count >= gen.Roads.LengthOf(RoadKind.Road) / 60f * 0.5f, $"signs ({signs.Count})");
        var lefts = 0;
        foreach (var o in poles.Concat(signs))
        {
            var p = new Vector2(o.Position.X, o.Position.Z);
            var road = gen.Roads.Nearest(p.X, p.Y, 48f)!.Value;
            // Just past the bed, off it, and turned so its front (+z) faces the road.
            var gap = o.Definition.Id == "pole" ? 4f : 3f;
            Assert.InRange(road.Distance, gen.Roads.HalfBed(road.Chain) + gap - 0.6f, gen.Roads.HalfBed(road.Chain) + gap + 0.6f);
            Assert.False(gen.Roads.Covers(p.X, p.Y));
            var front = new Vector2(MathF.Sin(o.Yaw), MathF.Cos(o.Yaw));
            Assert.True(Vector2.Dot(front, Vector2.Normalize(road.Point - p)) > 0.99f, $"{o.Definition.Id} at {p} faces the road");
            if (o.Definition.Id == "pole")
            {
                var along = road.Chain.Points[road.Segment + 1] - road.Chain.Points[road.Segment];
                if (along.Y * (p.X - road.Point.X) - along.X * (p.Y - road.Point.Y) > 0f)
                    lefts++;
            }
        }
        // Poles keep to one side of the road's direction the whole way.
        Assert.True(lefts == 0 || lefts == poles.Count, $"{lefts} of {poles.Count} poles on one side");
    }

    [Theory]
    [InlineData("""{ "object": "o", "spacing": 10, "chance": 1, "roadside": { "gap": 1 } }""", "$.biomes[0].scatter[0].roadside.gap")]
    [InlineData("""{ "object": "o", "spacing": 10, "chance": 1, "roadside": { "gap": 4 }, "perch": {} }""", "$.biomes[0].scatter[0].roadside")]
    [InlineData("""{ "object": "s", "spacing": 10, "chance": 1, "water": "surface" }""", "$.biomes[0].scatter[0].water")]
    public void BadWaterAndRoadsideRulesAreRejected(string rule, string path)
    {
        using var mods = new TestMods().Mod("vanilla").File("vanilla", WorldGenData.DefinitionFile, $$"""
            { "biomes": [{ "id": "a", "scatter": [{{rule}}] }],
              "objects": [{ "id": "o", "collider": { "shape": "sphere", "radius": 1 }, "footprint": 1 },
                          { "id": "s", "scene": { "layouts": [{ "pieces": [{ "object": "o", "at": [0, 0] }] }] } }] }
            """);
        var data = WorldGenData.Load(mods.Load());
        Assert.Contains(data.Diagnostics, d => d.Code == DiagnosticCodes.WorldGenInvalid && d.JsonPath == path);
    }

    [Theory]
    [InlineData("""{ "op": "basin", "at": [-4, 0], "to": [30, 0], "radius": 2, "blend": 6, "depth": 2 }""", "$.terrain[0]")]
    [InlineData("""{ "op": "basin", "to": [3], "radius": 2, "blend": 6, "depth": 2 }""", "$.terrain[0].to")]
    [InlineData("""{ "op": "raise", "radius": 2, "blend": 6, "height": -2 }""", "$.terrain[0].height")]
    public void BadStripsAndRaisesAreRejected(string op, string path)
    {
        using var mods = new TestMods().Mod("vanilla")
            .File("vanilla", WorldGenData.DefinitionFile, """{ "biomes": [{ "id": "a" }] }""")
            .File("vanilla", "pois/p.json", $$"""{ "id": "p", "radius": 20, "placement": { "fixed": [0, 0] }, "terrain": [{{op}}] }""");
        var data = WorldGenData.Load(mods.Load());
        Assert.Contains(data.Diagnostics, d => d.Code == DiagnosticCodes.PoiInvalid && d.JsonPath == path);
    }
}
