using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// Crops batch K1 (crops map): the four seeds we have take the wiki's seed stats (3/2/4/3/Y, stack 20: 4.5 kg, durability
/// 2, friction 0.4, buoyancy 0.3, flammable), every crop grows in 1260 s, harvests keep our yields (Potato 10, as SM) with
/// 1 to 3 seeds back (farmed Cotton exactly 1), and the raid values are SM's for these crops. One full Water Bucket waters a
/// soil mound for a whole crop (both 1260 s), a Growbed holds 2160 s, and a Water Cannon's jets from one bucket water as
/// much as throwing it. Wild cotton keeps its 40 % seed.
/// Batch K2 adds the Pigment Flower: a 6 kg craft input, not food, whose seed grows like the others (2 flowers, exactly
/// 1 seed back, raid value 0) in soil or a Growbed; wild ones in meadows and forests are picked like wild cotton; and the
/// Fabricator makes 10 Paint Ammo from one in 5 s, no longer from tomatoes.
/// </summary>
public class CropsTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static readonly Lazy<WorldGenDefinition> World = new(() => WorldGenData.Load(Vanilla.Value).Definition);

    private static WorldObjectDefinition WorldObject(string id) => World.Value.Objects!.Single(o => o.Id == id);

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    [Theory]
    [InlineData("carrot_seed")]
    [InlineData("tomato_seed")]
    [InlineData("potato_seed")]
    [InlineData("cotton_seed")]
    [InlineData("pigment_flower_seed")]
    public void SeedsTakeTheWikisSeedStats(string id)
    {
        var part = Part(id);

        Assert.Equal((4.5, 2, 0.4, 0.3, true, 20),
            (Math.Round((double)part.Mass!.Value, 3), (int)part.Durability!.Value, Math.Round((double)part.Friction!.Value, 3),
                Math.Round((double)part.Buoyancy!.Value, 3), part.Flammable!.Value, part.StackSize!.Value));
        Assert.Equal(InventoryTab.Consumable, InventoryTabs.Of(part, false));
        Assert.Equal("scripts/seed.lua", part.Script);
    }

    [Theory]
    [InlineData("carrot_seed", "carrot", 2, 1, 3, 2)]
    [InlineData("tomato_seed", "tomato", 2, 1, 3, 1)]
    [InlineData("potato_seed", "potato", 10, 1, 3, 1)]
    [InlineData("cotton_seed", "cotton", 2, 1, 1, 0)]
    [InlineData("pigment_flower_seed", "pigment_flower", 2, 1, 1, 0)]
    public void EveryCropGrowsIn1260SecondsWithItsHarvestAndRaidValue(string id, string crop, int yield, int seeds, int seedsMax, double value)
    {
        var seed = Part(id).Seed!;

        Assert.Equal((crop, 1260f, yield, seeds, seedsMax, (float)value), (seed.Crop, seed.GrowTime, seed.Yield, seeds, seed.SeedsMax, seed.Value));
        Assert.Equal(seeds, seed.Seeds);
        Assert.Null(seed.Thirst); // default 1: one second of water a second
        Assert.True(Part(crop).StackSize >= yield, $"a {crop} harvest fits one stack");
    }

    [Fact]
    public void OneBucketGrowsOneCrop()
    {
        var bucket = Part("water_bucket_full").Properties!["seconds"]!.GetValue<double>();
        var soil = Part("soil").Soil!;
        var growbed = Part("growbed").Soil!;
        var cannon = Part("water_cannon").Properties!;

        Assert.Equal(1260.0, bucket);
        Assert.Equal(1260f, soil.Capacity);
        Assert.Equal(2160f, growbed.Capacity);
        Assert.Equal(bucket, cannon["water"]!.GetValue<double>() * cannon["jets"]!.GetValue<int>(), 3);
        Assert.Equal("water_bucket_full", cannon["water_item"]!.GetValue<string>());
        foreach (var id in new[] { "carrot_seed", "tomato_seed", "potato_seed", "cotton_seed", "pigment_flower_seed" })
        {
            var drinks = Part(id).Seed!.GrowTime!.Value * (Part(id).Seed!.Thirst ?? 1f);
            Assert.True(drinks <= bucket && drinks <= soil.Capacity, $"a {id} crop drinks one bucket at most ({drinks} s)");
        }
    }

    [Fact]
    public void WildCottonKeepsItsFortyPercentSeed()
    {
        var path = Path.Combine(TestMods.RepoRoot, "mods", "vanilla", "world", "worldgen.json");
        var json = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        })!;
        var drops = json.AsObject().Select(p => p.Value).OfType<JsonArray>().SelectMany(a => a.OfType<JsonObject>())
            .Single(o => (string?)o["id"] == "wild_cotton")["drops"]!.AsArray();
        var seed = drops.OfType<JsonObject>().Single(d => (string?)d["item"] == "cotton_seed");

        Assert.Equal((1, 0.4), (seed["count"]!.GetValue<int>(), seed["chance"]!.GetValue<double>()));
    }

    [Fact]
    public void ThePigmentFlowerIsACraftInputNotFood()
    {
        var flower = Part("pigment_flower");

        Assert.Equal((6.0, 2, 0.4, 0.3, true, 20),
            (Math.Round((double)flower.Mass!.Value, 3), (int)flower.Durability!.Value, Math.Round((double)flower.Friction!.Value, 3),
                Math.Round((double)flower.Buoyancy!.Value, 3), flower.Flammable!.Value, flower.StackSize!.Value));
        Assert.Equal(PartCategory.Resource, flower.Category);
        Assert.Null(flower.Script); // not eaten: no food.lua
        Assert.Equal("textures/items/pigment_flower_icon.png", flower.Icon);
        Assert.Equal("meshes/items/pigment_flower_held.glb", flower.Held?.Mesh);
        Assert.Equal("meshes/crops/pigment_flower_plant.glb", Part("pigment_flower_seed").Seed!.Plant);
        foreach (var file in new[] { "meshes/items/pigment_flower.glb", "meshes/items/pigment_flower_held.glb", "meshes/items/pigment_flower_seed.glb",
                     "meshes/crops/pigment_flower_plant.glb", "meshes/world/props/wild_pigment_flower.glb", "textures/items/pigment_flower_icon.png",
                     "textures/items/pigment_flower_seed_icon.png" })
            Assert.True(File.Exists(Path.Combine(TestMods.RepoRoot, "mods", "vanilla", file)), $"{file} is built");
    }

    [Fact]
    public void TheFabricatorMakesTenPaintAmmoFromOnePigmentFlower()
    {
        var book = RecipeBook.Load(Vanilla.Value);
        var recipe = book.Find("paint_ammo")!;

        Assert.Equal(["vanilla:fabricator"], recipe.Stations);
        Assert.Equal(["vanilla:pigment_flower:1"], recipe.Inputs.Select(i => $"{i.Item}:{i.Count}"));
        Assert.Equal(("vanilla:paint_ammo", 10), (recipe.Outputs.Single().Item, recipe.Outputs.Single().Count));
        Assert.Equal((5f, 2440), (recipe.Time, recipe.Definition.Order));
        // Its place in the tools group is its own, and tomatoes make no paint any more.
        Assert.Single(book.All, r => r.Definition.Order == 2440);
        Assert.DoesNotContain(book.All, r => r.Outputs.Any(o => o.Item == "vanilla:paint_ammo") && r.Inputs.Any(i => i.Item == "vanilla:tomato"));
    }

    [Fact]
    public void TheGrowbedGrowsPigmentFlowers()
    {
        var growbed = Part("growbed");

        Assert.Equal(["carrot_seed", "potato_seed", "tomato_seed", "cotton_seed", "pigment_flower_seed"], growbed.Soil!.Seeds);
        Assert.Contains("pigment flower seeds", growbed.Description);
    }

    [Fact]
    public void TheWildPigmentFlowerIsOneLightMesh()
    {
        // The world draws an object from the first mesh in its file (TerrainChunk), many times over a biome.
        var bytes = File.ReadAllBytes(Path.Combine(TestMods.RepoRoot, "mods", "vanilla", "meshes", "world", "props", "wild_pigment_flower.glb"));
        using var doc = JsonDocument.Parse(bytes.AsMemory(20, BitConverter.ToInt32(bytes, 12)));
        var meshes = doc.RootElement.GetProperty("meshes");
        var accessors = doc.RootElement.GetProperty("accessors");
        var triangles = meshes.EnumerateArray().SelectMany(m => m.GetProperty("primitives").EnumerateArray())
            .Sum(p => accessors[p.GetProperty("indices").GetInt32()].GetProperty("count").GetInt32() / 3);

        Assert.Equal(1, meshes.GetArrayLength());
        Assert.InRange(triangles, 1000, 12000);
    }

    [Fact]
    public void WildPigmentFlowersArePickedLikeWildCottonInMeadowsAndForests()
    {
        var cotton = WorldObject("wild_cotton");
        var flower = WorldObject("wild_pigment_flower");

        Assert.True(flower.Harvest, "Use picks it");
        Assert.Equal(cotton.Regrow, flower.Regrow);
        Assert.Equal([("pigment_flower", 1, (int?)null, (float?)null), ("pigment_flower_seed", 1, null, 0.4f)],
            flower.Drops!.Select(d => (d.Item, d.Count, d.CountMax, d.Chance)));
        Assert.Equal(cotton.Drops!.Single(d => d.Item == "cotton_seed").Chance, flower.Drops!.Single(d => d.Item == "pigment_flower_seed").Chance);
        var cottonRule = World.Value.Biomes!.SelectMany(b => b.Scatter ?? []).Single(r => r.Object == "wild_cotton");
        var biomes = World.Value.Biomes!.Where(b => b.Scatter?.Any(r => r.Object == "wild_pigment_flower") == true).Select(b => b.Id).ToList();
        Assert.Equal(["meadow", "forest"], biomes);
        Assert.All(World.Value.Biomes!.SelectMany(b => b.Scatter ?? []).Where(r => r.Object == "wild_pigment_flower"),
            r => Assert.Equal((cottonRule.Spacing, cottonRule.Chance), (r.Spacing, r.Chance)));
    }

    [Fact]
    public void PigmentFlowerSeedsAreRareLoot()
    {
        var tables = World.Value.LootTables!.Where(t => t.Value.Items!.Any(i => i.Item == "pigment_flower_seed")).ToList();

        Assert.NotEmpty(tables);
        foreach (var (_, table) in tables)
        {
            var seed = table.Items!.Single(i => i.Item == "pigment_flower_seed");
            Assert.True(seed.Weight <= table.Items!.Where(i => i.Item!.EndsWith("_seed", StringComparison.Ordinal)).Min(i => i.Weight),
                "no likelier than any other seed in its table");
        }
    }
}
