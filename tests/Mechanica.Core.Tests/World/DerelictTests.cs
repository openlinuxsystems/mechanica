using System.Numerics;
using System.Text.Json;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

/// <summary>
/// Derelict vehicles: the vanilla designs load and roll which key parts they are missing from the world seed, POI markers
/// place each one once and never again, and bad derelict data and markers are refused.
/// </summary>
public sealed class DerelictTests
{
    private static readonly Lazy<ModLoadResult> Mods = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    private static readonly Lazy<WorldGenData> Data = new(() => WorldGenData.Load(Mods.Value));

    private static readonly string[] Vanilla =
        ["scrap_buggy", "go_kart", "farm_cart", "colony_runabout", "flatbed_hauler", "old_tractor", "farm_trailer", "rover_frame"];

    private static LoadedDerelict Derelict(string id) => Data.Value.Derelicts[$"vanilla:{id}"];

    private static WorldPartSpot Spot(string id, string key = "vanilla:poi_32#20@3") =>
        new(key, "vanilla:poi_32", $"vanilla:{id}", new Vector3(10, 5, -4), 0.5f, Derelict: Derelict(id));

    [Fact]
    public void TheVanillaDerelictsLoadWithoutProblems()
    {
        Assert.Equal(Vanilla.Order(), Data.Value.Derelicts.Values.Where(d => d.ModId == "vanilla").Select(d => d.Definition.Id!).Order());
        Assert.DoesNotContain(Data.Value.Diagnostics, d => d.File.Contains("derelicts/") && d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("scrap_buggy", "vanilla:scrap_gas_engine")]
    [InlineData("colony_runabout", "vanilla:gas_engine")]
    [InlineData("old_tractor", "vanilla:big_wheel")]
    [InlineData("rover_frame", "vanilla:electric_engine")]
    public void EachIsARealVehicleOfOurParts(string id, string part)
    {
        var creation = Derelicts.Read(Derelict(id), Mods.Value, 1, id, [])!;
        Assert.Contains(creation.Parts, p => p.Part == part);
        Assert.True(creation.Parts.Count(p => p.Part == "vanilla:bearing") >= 4, "it stands on wheels on bearings");
        Assert.True(creation.Parts.Count >= 50, $"{id} has {creation.Parts.Count} parts");
    }

    [Theory]
    [InlineData("scrap_buggy", "Workshop")]
    [InlineData("colony_runabout", "Engine sent")]
    [InlineData("flatbed_hauler", "scrapyard")]
    [InlineData("old_tractor", "barn")]
    public void SomeLeaveANoteSayingWhereThePartsWent(string id, string words)
    {
        var spot = Spot(id);
        var creation = Derelicts.Build(spot, spot.Derelict!, 42, Mods.Value, 7, out var problem)!;
        Assert.Null(problem);
        var note = creation.Parts.Single(p => p.Part == "vanilla:paper_note");
        Assert.Contains(words, note.Settings!["text"].GetString());
        Assert.Null(Core.Logic.PartSettings.Check(creation.DefinitionOf(note.Id)!, note.Settings!));
    }

    [Fact]
    public void TheSeedPicksWhatIsMissingAndTheSameSeedPicksTheSame()
    {
        var derelict = Derelict("scrap_buggy");
        var whole = Derelicts.Read(derelict, Mods.Value, 1, "buggy", [])!;
        var rolls = Enumerable.Range(0, 200).Select(seed => Derelicts.Missing(derelict, whole, (uint)seed)).ToList();
        Assert.Equal(rolls[17], Derelicts.Missing(derelict, whole, 17));
        string PartOf(int id) => whole.Parts.Single(p => p.Id == id).Part;
        var engineless = rolls.Count(r => r.Any(id => PartOf(id) == "vanilla:scrap_gas_engine"));
        Assert.InRange(engineless, 150, 190);
        Assert.All(rolls, r => Assert.InRange(r.Count(id => PartOf(id) == "vanilla:scrap_wheel"), 0, 2));
        Assert.Contains(rolls, r => r.Count(id => PartOf(id) == "vanilla:scrap_wheel") == 2);
        Assert.Contains(rolls, r => r.Count == 0 || r.All(id => PartOf(id) != "vanilla:gas_container"));
    }

    [Fact]
    public void BuildLeavesItWholeStandingOnTheMarkerAndOwnedByNobody()
    {
        var spot = Spot("old_tractor");
        var creation = Derelicts.Build(spot, spot.Derelict!, 42, Mods.Value, 7, out var problem);
        Assert.Null(problem);
        Assert.NotNull(creation);
        Assert.Equal(42, creation.Id);
        Assert.False(creation.IsWorldOwned);
        Assert.Empty(creation.SplitDisconnected(() => -1));
        var full = Derelicts.Read(spot.Derelict!, Mods.Value, 1, "tractor", [])!;
        Assert.Equal(full.Parts.Count - Derelicts.Missing(spot.Derelict!, full, Derelicts.SeedOf(7, spot.Key)).Count, creation.Parts.Count);
        var box = CreationBounds.World(CreationBounds.LocalBodyBounds(creation), [.. creation.Bodies.Select((_, i) => creation.BodyPose(i))]);
        Assert.InRange(box.Min.Y - spot.Floor.Y, 0f, 0.05f);
        Assert.InRange(Vector2.Distance(new Vector2((box.Min.X + box.Max.X) / 2, (box.Min.Z + box.Max.Z) / 2), new Vector2(spot.Floor.X, spot.Floor.Z)), 0f, 0.6f);
    }

    [Fact]
    public void AMarkerPlacesItsDerelictOnceAndNeverAgain()
    {
        var spots = new[] { Spot("go_kart") };
        var next = 100;
        var first = WorldParts.Reconcile(spots, [], Mods.Value, () => next++, _ => false, worldSeed: 3);
        Assert.Single(first.Add);
        Assert.Equal(["vanilla:poi_32#20@3"], first.Used);
        // Once used (saved with the destroyed objects), loading the world places none, whether the first is still there or not.
        Func<WorldObjectId, bool> used = id => id == WorldParts.ObjectOf("vanilla:poi_32#20@3");
        Assert.Empty(WorldParts.Reconcile(spots, first.Add, Mods.Value, () => next++, used, 3).Add);
        Assert.Empty(WorldParts.Reconcile(spots, [], Mods.Value, () => next++, used, 3).Add);
        // A player's (or a stripped derelict's) Creation is never removed or moved.
        var kept = WorldParts.Reconcile(spots, first.Add, Mods.Value, () => next++, used, 3);
        Assert.Empty(kept.Remove);
        Assert.Empty(kept.Moved);
    }

    [Fact]
    public void ThePoisLeaveDerelictsInTheScrapyardsByTheRoadAndInTheFields()
    {
        var markers = Data.Value.Pois.ToDictionary(p => p.Prefab.Id!, p => p.Prefab.Markers ?? []);
        Assert.Contains(markers["farmbot_scrapyard"], m => m is { Type: PoiMarker.DerelictType, Ref: "old_tractor" });
        Assert.Contains(markers["poi_41"], m => m is { Type: PoiMarker.DerelictType, Ref: "rover_frame" });
        Assert.Contains(markers["poi_50"], m => m is { Type: PoiMarker.DerelictType, Ref: "colony_runabout" });
        Assert.Contains(markers["roadside_breakdown"], m => m is { Type: PoiMarker.DerelictType, Ref: "scrap_buggy" });
        Assert.Contains(markers["abandoned_tractor"], m => m is { Type: PoiMarker.DerelictType, Ref: "farm_trailer" });
        var used = markers.Values.SelectMany(m => m).Where(m => m.Type == PoiMarker.DerelictType).Select(m => m.Ref!).ToHashSet();
        Assert.All(Vanilla, id => Assert.Contains(id, used));
    }

    [Fact]
    public void AWorldPlacesDerelictsThatAllBuild()
    {
        var world = new WorldGenerator(Data.Value, 11, 12032f, 9024f);
        var spots = Derelicts.SpotsOf(world);
        Assert.InRange(spots.Count, 60, 300);
        var next = 1;
        var plan = WorldParts.Reconcile(spots, [], Mods.Value, () => next++, _ => false, world.Seed);
        Assert.Empty(plan.Problems);
        Assert.Equal(spots.Count, plan.Add.Count);
    }

    [Fact]
    public void BadDerelictsAreRefused()
    {
        var good = Derelict("go_kart").Definition;
        string Problems(DerelictDefinition definition, DiagnosticSeverity severity = DiagnosticSeverity.Error) => string.Join("\n",
            Derelicts.Validate(new LoadedDerelict("vanilla:x", "vanilla", definition), Mods.Value, "x.json").Where(d => d.Severity == severity).Select(d => d.Message));
        Assert.Empty(Problems(good));
        Assert.Contains("needs a name", Problems(good with { Name = null }));
        Assert.Contains("creation is required", Problems(good with { Creation = null }));
        Assert.Contains("no 'vanilla:gas_engine'", Problems(good with { Missing = [new DerelictMissing { Part = "gas_engine" }] }));
        Assert.Contains("in pieces", Problems(good with { Missing = [new DerelictMissing { Part = "scrap_metal_block" }] }));
        Assert.Contains("chance", Problems(good with { Missing = [new DerelictMissing { Part = "scrap_wheel", Chance = 2f }] }));
        Assert.Contains("unknown key 'mising'", Problems(good with { Extra = new() { ["mising"] = JsonSerializer.SerializeToElement(1) } }, DiagnosticSeverity.Warning));
    }

    [Fact]
    public void AMarkerMustNameADerelictAndHoldNoLoot()
    {
        using var mods = new TestMods().Mod("vanilla")
            .File("vanilla", WorldGenData.DefinitionFile, """
                { "biomes": [{ "id": "a" }], "loot_tables": { "junk": { "items": [{ "item": "bolt" }] } } }
                """)
            .Part("vanilla", "bolt.json", TestMods.Block("bolt"))
            .File("vanilla", "derelicts/heap.json", """
                { "id": "heap", "name": "Heap",
                  "creation": { "format_version": 1, "id": 0, "parts": [{ "id": 1, "part": "vanilla:bolt", "cell": [0, 0, 0] }] } }
                """)
            .File("vanilla", "pois/yard.json", """
                { "id": "yard", "radius": 20, "placement": { "fixed": [100, 100] },
                  "markers": [
                    { "type": "derelict", "ref": "no_such_car" },
                    { "type": "derelict", "ref": "heap", "loot": "junk" },
                    { "type": "derelict", "ref": "heap" }
                  ] }
                """);
        var data = WorldGenData.Load(mods.Load());

        Assert.True(data.Derelicts.ContainsKey("vanilla:heap"));
        var errors = data.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Contains(errors, d => d.JsonPath == "$.markers[0].ref" && d.Message.Contains("not a derelict"));
        Assert.Contains(errors, d => d.JsonPath == "$.markers[1].loot");
        Assert.DoesNotContain(errors, d => d.JsonPath?.StartsWith("$.markers[2]", StringComparison.Ordinal) == true);
    }
}
