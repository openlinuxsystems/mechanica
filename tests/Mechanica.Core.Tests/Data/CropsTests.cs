using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Data;
using Mechanica.Core.Items;

namespace Mechanica.Core.Tests.Data;

/// <summary>
/// Crops batch K1 (crops map): the four seeds we have take the wiki's seed stats (3/2/4/3/Y, stack 20: 4.5 kg, durability
/// 2, friction 0.4, buoyancy 0.3, flammable), every crop grows in 1260 s, harvests keep our yields (Potato 10, as SM) with
/// 1 to 3 seeds back (farmed Cotton exactly 1), and the raid values are SM's for these crops. One full Water Bucket waters a
/// soil mound for a whole crop (both 1260 s), a Growbed holds 2160 s, and a Water Cannon's jets from one bucket water as
/// much as throwing it. Wild cotton keeps its 40 % seed.
/// </summary>
public class CropsTests
{
    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static PartDefinition Part(string id) =>
        Vanilla.Value.FindDefinition(id) ?? throw new Xunit.Sdk.XunitException($"vanilla has no item '{id}'.");

    [Theory]
    [InlineData("carrot_seed")]
    [InlineData("tomato_seed")]
    [InlineData("potato_seed")]
    [InlineData("cotton_seed")]
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
        foreach (var id in new[] { "carrot_seed", "tomato_seed", "potato_seed", "cotton_seed" })
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
}
