using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Saves;
using Mechanica.Core.Tests.Data;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.World;

/// <summary>
/// The world's own parts (POI <c>part</c> markers, a Workshop's Fabricator): one per marker, standing on it, kept through
/// saves, and world-owned: usable, but never picked up, built onto, lifted, welded, painted or damaged.
/// </summary>
public sealed class WorldPartsTests
{
    private const string Fabricator = "vanilla:fabricator";
    private const string Wood = "vanilla:wood_block";
    private const string Workshop = "vanilla:outpost";
    private static readonly ActionCaller Player = new(2, ActionSource.Player);

    private static readonly Lazy<ModLoadResult> Vanilla = new(() => ModLoader.LoadAll(Path.Combine(TestMods.RepoRoot, "mods")));

    // The vanilla world at the game's size (data/streaming.json), with the test harness's seed.
    private static readonly Lazy<WorldGenerator> VanillaWorld = new(() => new WorldGenerator(WorldGenData.Load(Vanilla.Value), 1234, 12032f, 9024f));

    private static IPartCatalog Catalog => Vanilla.Value;

    private static IReadOnlyList<WorldPartSpot> Spots => WorldParts.SpotsOf(VanillaWorld.Value);

    // ---- Markers ----

    [Fact]
    public void EveryWorkshopHasOneFabricatorMarker()
    {
        var world = VanillaWorld.Value;
        var workshops = world.Pois.Where(p => p.Poi.Prefab.Markers?.Any(m => m.Type == PoiMarker.PartType) == true).ToList();

        Assert.Contains(workshops, p => p.Poi.QualifiedId == Workshop);
        Assert.Equal(workshops.Count, Spots.Count);
        Assert.All(Spots, s => Assert.Equal(Fabricator, s.Part));
        Assert.Equal(Spots.Count, Spots.Select(s => s.Key).Distinct().Count());
        foreach (var poi in workshops)
            Assert.Single(Spots, s => s.Key.EndsWith($"@{poi.Index}", StringComparison.Ordinal) && s.Poi == poi.Poi.QualifiedId);
        Assert.Contains(Spots, s => s.Key.StartsWith($"{Workshop}#0@", StringComparison.Ordinal));
    }

    [Fact]
    public void AWorldPartStandsOnItsMarkerFacingItsYaw()
    {
        var markers = VanillaWorld.Value.MarkersOfType(PoiMarker.PartType).ToList();
        Assert.Equal(Spots.Count, markers.Count);
        for (var i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            var creation = WorldParts.Build(Spots[i], 7, Catalog, out var problem) ?? throw new InvalidOperationException(problem);

            Assert.Equal(WorldParts.KeyOf(marker.Poi.Poi.QualifiedId, marker.Index, marker.Poi.Index), creation.WorldPart);
            Assert.True(creation.IsWorldOwned);
            Assert.Equal(Creation.HostPeer, creation.AuthorityPeer);
            var part = Assert.Single(creation.Parts);
            Assert.Equal(Fabricator, part.Part);
            Assert.True(creation.IsAnchored(part.Id));
            Assert.True(creation.IsBodyAnchored(0));
            // Its bottom face sits centred on the marker (the floor point).
            var box = CreationBounds.World(creation);
            Assert.Equal(marker.Position.Y, box.Min.Y, 1e-3f);
            Assert.Equal(marker.Position.X, (box.Min.X + box.Max.X) / 2, 1e-3f);
            Assert.Equal(marker.Position.Z, (box.Min.Z + box.Max.Z) / 2, 1e-3f);
            // Its front (local -Z) faces the marker's yaw, as every marker turns: 0 faces -Z, counter-clockwise from above.
            var front = Vector3.Transform(-Vector3.UnitZ, creation.BodyPose(0).Rotation);
            var expected = Vector3.Transform(-Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.UnitY, marker.Yaw));
            Assert.True(Vector3.Distance(front, expected) < 1e-4f, $"{creation.WorldPart} faces {front}, not {expected}");
            Assert.True(Vector3.Distance(Vector3.Transform(Vector3.UnitY, creation.BodyPose(0).Rotation), Vector3.UnitY) < 1e-4f, "it stands upright");
        }
    }

    [Fact]
    public void TheWorkshopFabricatorFacesOutOfItsBay()
    {
        // The outpost's marker turns 180°: unturned, its Fabricator faces +Z, out of the bay at the back wall.
        var spot = new WorldPartSpot("vanilla:outpost#0@0", Workshop, Fabricator, new Vector3(7.25f, 0.3f, -10.6f), MathF.PI);
        var creation = WorldParts.Build(spot, 1, Catalog, out _)!;

        var front = Vector3.Transform(-Vector3.UnitZ, creation.BodyPose(0).Rotation);
        Assert.True(Vector3.Distance(front, Vector3.UnitZ) < 1e-4f, $"{front}");
        var box = CreationBounds.World(creation);
        // 10 × 9 × 8 cells: 2.5 m wide, 2.25 m tall, 2 m deep, standing on the floor point.
        Assert.Equal(new Vector3(6f, 0.3f, -11.6f), box.Min, new Vector3Comparer(1e-3f));
        Assert.Equal(new Vector3(8.5f, 2.55f, -9.6f), box.Max, new Vector3Comparer(1e-3f));
    }

    [Fact]
    public void ABareRefNamesAPartOfThePoisMod()
    {
        Assert.Equal("wrecks:crate", PoiMarker.Qualify("crate", "wrecks:hulk"));
        Assert.Equal("vanilla:fabricator", PoiMarker.Qualify("vanilla:fabricator", "wrecks:hulk"));
    }

    [Fact]
    public void APartMarkerNamesALoadedPartOfItsOwnModOrAnother()
    {
        using var mods = new TestMods().Mod("vanilla").Mod("wrecks", dependencies: """[{ "id": "vanilla", "version": ">=1.0.0" }]""")
            .File("vanilla", WorldGenData.DefinitionFile, """{ "biomes": [{ "id": "a" }] }""")
            .Part("vanilla", "crate.json", TestMods.Block("crate"))
            .Part("wrecks", "winch.json", TestMods.Block("winch"))
            .File("wrecks", "pois/hulk.json", """
                { "id": "hulk", "radius": 20, "placement": { "fixed": [100, 100] },
                  "markers": [{ "type": "part", "ref": "winch", "at": [1, 0, 2] }, { "type": "item", "ref": "anything" },
                              { "type": "part", "ref": "vanilla:crate", "at": [-3, 0.5, 0], "yaw": 90 }] }
                """);
        var loaded = mods.Load();
        var data = WorldGenData.Load(loaded);
        Assert.Empty(data.Diagnostics);

        var world = new WorldGenerator(data, 1, 4096f);
        var hulk = Assert.Single(world.Pois);
        var spots = WorldParts.SpotsOf(world);
        Assert.Equal([$"wrecks:hulk#0@{hulk.Index}", $"wrecks:hulk#2@{hulk.Index}"], spots.Select(s => s.Key));
        Assert.Equal(["wrecks:winch", "vanilla:crate"], spots.Select(s => s.Part));
        Assert.Equal(hulk.ToWorld(new Vector3(-3, 0.5f, 0)), spots[1].Floor);
        Assert.Equal(hulk.Yaw + MathF.PI / 2, spots[1].Yaw, 4);
        Assert.Equal(2, WorldParts.Reconcile(spots, [], loaded, Ids()).Add.Count);
    }

    [Fact]
    public void AMarkerOfAnUnknownPartPlacesNothingAndSaysWhy()
    {
        var spot = new WorldPartSpot("test:ruin#0@0", "test:ruin", "vanilla:nope", Vector3.Zero, 0);
        var plan = WorldParts.Reconcile([spot], [], Catalog, Ids());

        Assert.Empty(plan.Add);
        Assert.Contains("vanilla:nope", Assert.Single(plan.Problems));
    }

    // ---- Once per marker ----

    [Fact]
    public void ANewWorldGetsOnePartPerMarkerAndReconcilingAgainAddsNone()
    {
        var first = WorldParts.Reconcile(Spots, [], Catalog, Ids());

        Assert.Equal(Spots.Select(s => s.Key), first.Add.Select(c => c.WorldPart));
        Assert.Empty(first.Remove);
        Assert.Empty(first.Moved);
        Assert.Empty(first.Problems);
        Assert.Equal(first.Add.Count, first.Add.Select(c => c.Id).Distinct().Count());

        var again = WorldParts.Reconcile(Spots, first.Add, Catalog, Ids(500));
        Assert.Empty(again.Add);
        Assert.Empty(again.Remove);
        Assert.Empty(again.Moved);
    }

    [Fact]
    public void ASavedWorldKeepsItsPartsAndWhatTheyHoldWithoutDuplicates()
    {
        var made = WorldParts.Reconcile(Spots, [], Catalog, Ids()).Add;
        var fabricator = made[0];
        fabricator.InventoryOf(1)!.Add("vanilla:scrap_metal", 7);
        fabricator.CraftingOf(1)!.Enqueue(new CraftJob("vanilla:chest", 2));
        // Players' own Creations are saved alongside and never touched.
        var own = new Creation(900, Catalog);
        own.TryPlacePart(Wood, GridCell.Zero, GridRotation.Identity, out _, out _);

        var save = new SaveGame { Seed = 1234, Creations = [.. made.Append(own).Select(CreationJson.Write)] };
        var diagnostics = new List<DataDiagnostic>();
        var read = SaveGame.Read(save.Write(), "save", diagnostics)!;
        var loaded = read.ReadCreations(Catalog, "save", diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        Assert.Equal(made.Select(c => c.WorldPart), loaded.Where(c => c.IsWorldOwned).Select(c => c.WorldPart));
        Assert.False(loaded.Single(c => c.Id == 900).IsWorldOwned);
        var back = loaded.Single(c => c.Id == fabricator.Id);
        Assert.Equal(7, back.InventoryOf(1)!.Count("vanilla:scrap_metal"));
        Assert.Equal(2, back.CraftingOf(1)!.Jobs[0].Count);
        Assert.True(back.IsAnchored(1));
        Assert.Equal(fabricator.BodyPose(0), back.BodyPose(0));

        var plan = WorldParts.Reconcile(Spots, loaded, Catalog, Ids(1000));
        Assert.Empty(plan.Add);
        Assert.Empty(plan.Remove);
        Assert.Empty(plan.Moved);
    }

    [Fact]
    public void AnOldSaveWithoutWorldPartsGetsThem()
    {
        var own = new Creation(1, Catalog);
        own.TryPlacePart(Wood, GridCell.Zero, GridRotation.Identity, out _, out _);

        var plan = WorldParts.Reconcile(Spots, [own], Catalog, Ids(2));

        Assert.Equal(Spots.Count, plan.Add.Count);
        Assert.DoesNotContain(plan.Add, c => c.Id == 1);
        Assert.Empty(plan.Remove);
    }

    [Fact]
    public void PartsWhoseMarkerIsGoneMovedOrRepeatedAreSortedOut()
    {
        var spot = Spots[0];
        var made = WorldParts.Reconcile(Spots, [], Catalog, Ids()).Add;
        var stray = WorldParts.Build(spot with { Key = "gone:ruin#0@99" }, 800, Catalog, out _)!;
        var twin = WorldParts.Build(spot, 801, Catalog, out _)!;
        made[0].SetAllBodyPoses(new Pose(made[0].BodyPose(0).Position + new Vector3(0, 0.5f, 0), made[0].BodyPose(0).Rotation));

        var plan = WorldParts.Reconcile(Spots, [.. made, stray, twin], Catalog, Ids(900));

        Assert.Empty(plan.Add);
        Assert.Equal([800, 801], plan.Remove);
        Assert.Equal([made[0].Id], plan.Moved);
        Assert.Equal(WorldParts.PoseFor(made[0].DefinitionOf(1), spot), made[0].BodyPose(0));
    }

    [Fact]
    public void AMarkerThatNamesAnotherPartNowReplacesTheOldOne()
    {
        var spot = Spots[0];
        var old = WorldParts.Build(spot with { Part = "vanilla:chest" }, 5, Catalog, out _)!;

        var plan = WorldParts.Reconcile([spot], [old], Catalog, Ids(6));

        Assert.Equal([5], plan.Remove);
        Assert.Equal(Fabricator, Assert.Single(Assert.Single(plan.Add).Parts).Part);
    }

    [Fact]
    public void WorldPartIsSavedAndSyncedButNeverInABlueprint()
    {
        var creation = WorldParts.Build(Spots[0], 3, Catalog, out _)!;

        var json = CreationJson.Write(creation);
        Assert.Equal(Spots[0].Key, JsonNode.Parse(json)!["world_part"]!.GetValue<string>());
        var diagnostics = new List<DataDiagnostic>();
        Assert.Equal(Spots[0].Key, CreationJson.Read(json, Catalog, "sync", diagnostics)!.WorldPart);
        Assert.Null(JsonNode.Parse(CreationJson.Write(new Creation(4, Catalog).With(Wood)))!["world_part"]);

        var blueprint = Blueprint.Write(creation, "Free fabricator", DateTimeOffset.UnixEpoch);
        Assert.DoesNotContain("world_part", blueprint);
        Assert.False(Blueprint.Read(blueprint, Catalog, 9, "bp", diagnostics)!.IsWorldOwned);
        // Even a hand-edited blueprint that claims a marker loads as the player's own.
        var forged = JsonNode.Parse(blueprint)!.AsObject();
        forged["creation"]!["world_part"] = Spots[0].Key;
        Assert.False(Blueprint.Read(forged.ToJsonString(), Catalog, 9, "bp", diagnostics)!.IsWorldOwned);
    }

    // ---- Protection ----

    [Fact]
    public void NobodyCanPickUpOrBuildOntoAWorldPart()
    {
        var t = new Protected();

        Assert.Equal("The Fabricator belongs to the world: it can't be picked up.", t.Refused(BuildActions.RemovePart, new { creation = t.Id, part = 1 }));
        Assert.Equal("The Fabricator belongs to the world: it can't be picked up.", t.Refused(BuildActions.RemoveParts, new { creation = t.Id, parts = new[] { 1 } }));
        Assert.Equal("The Fabricator belongs to the world: it can't be built onto.",
            t.Refused(BuildActions.PlacePart, new { part = Wood, cell = new[] { 0, 9, 0 }, creation = t.Id }));
        Assert.Equal("The Fabricator belongs to the world: it can't be built onto.",
            t.Refused(BuildActions.PlaceParts, new { part = Wood, cells = new[] { new[] { 0, 9, 0 } }, creation = t.Id }));
        Assert.Equal("The Fabricator belongs to the world: it can't be built onto.", BuildAim.JoinProblem(Catalog.FindDefinition(Wood)!, t.World.Creations[t.Id]));
        t.AssertUnchanged();

        // A Fabricator a player put down is theirs to pick up.
        var mine = new Creation(50, Catalog).With(Fabricator);
        mine.SetAllBodyPoses(new Pose(new Vector3(-1, 0, 0), Quaternion.Identity));
        t.World.Add(mine, owned: false);
        var result = t.Dispatch(BuildActions.RemovePart, new { creation = 50, part = 1 });
        Assert.True(result.Ok, result.ToString());
    }

    [Fact]
    public void NobodyCanLiftWeldOrPaintAWorldPart()
    {
        var t = new Protected();
        var loose = t.World.Add(Loose(60, new Vector3(0, 0, 1.5f)), owned: false);

        Assert.Equal("The Fabricator belongs to the world: it can't be lifted.", t.Refused(ToolActions.PlaceLift, new { position = new[] { 0f, 0f, 1f }, creation = t.Id }));
        Assert.Equal("The Fabricator belongs to the world: it can't be lifted.", t.Refused(ToolActions.CaptureLift, new { creation = t.Id }));
        Assert.Equal("The Fabricator belongs to the world: it can't be lifted.",
            ToolActions.LiftRefusal(t.World.Creations[t.Id], new LiftSettings { RefuseGrounded = false }, [], 2));
        Assert.Equal("The Fabricator belongs to the world: it can't be welded.",
            t.Refused(ToolActions.Weld, new { source = loose, target = t.Id, offset = new[] { 0, 9, 0 } }));
        Assert.Equal("The Fabricator belongs to the world: it can't be welded.",
            t.Refused(ToolActions.Weld, new { source = t.Id, target = loose, offset = new[] { 0, 1, 0 } }));
        Assert.Equal("The Fabricator belongs to the world: it can't be welded.", t.Refused(ToolActions.WeldParts, new { creation = t.Id, a = 1, b = 1 }));
        Assert.Equal("The Fabricator belongs to the world: it can't be painted.", t.Refused(ToolActions.Paint, new { creation = t.Id, parts = new[] { 1 }, color = "#ff0000" }));
        Assert.Equal("The Fabricator belongs to the world: it can't be painted.", t.Refused(ToolActions.Paint, new { creation = t.Id, parts = new[] { 1 } }));
        Assert.Null(t.Tools.LiftOf(2)?.Creation);
        t.AssertUnchanged();
    }

    [Fact]
    public void NothingDamagesAWorldPart()
    {
        var t = new Protected();

        Assert.Equal("The Fabricator belongs to the world: it can't be damaged.", t.Refused(ToolActions.Sledge, new { creation = t.Id, part = 1 }));
        // Whatever else hits it (a weapon, a bot, fire, a blast through IToolWorld) deals nothing and breaks nothing.
        for (var i = 0; i < 20; i++)
        {
            var hit = t.Tools.DamagePart(t.World.Creations[t.Id], 1, 1000f);
            Assert.False(hit.Broken);
            Assert.Null(hit.Health);
        }
        Assert.Equal(0f, t.World.Creations[t.Id].PartDamage(1));
        Assert.Empty(t.World.Pushes);
        t.AssertUnchanged();
    }

    [Fact]
    public void WorldOwnershipNamesThePart()
    {
        var plain = new Creation(1, Catalog).With(Wood);
        Assert.Null(WorldOwned.Refusal(plain, "painted"));
        Assert.Null(WorldOwned.Refusal(null, "painted"));
        Assert.Equal("The Wood Block belongs to the world: it can't be painted.",
            WorldOwned.Refusal(Mechanica.Core.Tests.Creations.WorldOwnedCreations.Owned(plain), "painted"));
    }

    private static Func<int> Ids(int first = 1)
    {
        var next = first;
        return () => next++;
    }

    // A wood block lying loose (not anchored) with its grid origin at `at`.
    private static Creation Loose(int id, Vector3 at)
    {
        var creation = new Creation(id, Catalog).With(Wood);
        creation.SetAllBodyPoses(new Pose(at, Quaternion.Identity));
        return creation;
    }

    /// <summary>A world-owned Fabricator in front of player 2, with the building and tool actions on a fake world.</summary>
    private sealed class Protected
    {
        private readonly int _revision;
        private readonly Pose _pose;

        public Protected()
        {
            var settings = new BuildSettings
            {
                Reach = 8f,
                MaxPartsPerCreation = 100,
                Lift = new LiftSettings { PlatformSize = 4f, BaseHeight = 0.25f, LevelHeight = 0.25f, MaxLevel = 4, MaxExtent = 10f },
                Sledge = new SledgeSettings { Damage = 10f, HealthPerDurability = 10f, SwingSeconds = 0.5f, Impulse = 20f },
            };
            new BuildActions(Catalog, settings, World).Register(Dispatcher);
            Tools = new ToolActions(settings, World, Catalog);
            Tools.Register(Dispatcher);
            World.Eyes[2] = new Vector3(0, 1.6f, 3);
            var creation = WorldParts.Build(new WorldPartSpot("vanilla:outpost#0@0", Workshop, Fabricator, Vector3.Zero, 0), 1, Catalog, out _)!;
            Id = World.Add(creation, owned: true);
            _revision = creation.Revision;
            _pose = creation.BodyPose(0);
            World.Log.Clear();
        }

        public FakeToolWorld World { get; } = new();

        public ActionDispatcher Dispatcher { get; } = new();

        public ToolActions Tools { get; }

        public int Id { get; }

        public ActionResult Dispatch(string action, object args) =>
            Dispatcher.Dispatch(Player, action, JsonSerializer.Serialize(args, ActionJson.Options));

        public string Refused(string action, object args)
        {
            var result = Dispatch(action, args);
            Assert.False(result.Ok, $"{action} went through");
            Assert.Equal(ActionErrors.Rejected, result.Code);
            return result.Message!;
        }

        // Still there, whole, where it was, with nothing told to the world.
        public void AssertUnchanged()
        {
            var creation = World.Creations[Id];
            Assert.Equal(Fabricator, Assert.Single(creation.Parts).Part);
            Assert.Equal(_revision, creation.Revision);
            Assert.Equal(_pose, creation.BodyPose(0));
            Assert.True(creation.IsAnchored(1));
            Assert.False(creation.OnLift);
            Assert.Null(creation.FindPart(1)!.Color);
            Assert.DoesNotContain(World.Log, l => l.EndsWith($" {Id}", StringComparison.Ordinal));
        }
    }

    /// <summary>The world the building and tool actions change, in memory.</summary>
    internal sealed class FakeToolWorld : IToolWorld
    {
        private int _nextId = 100;

        public Dictionary<int, Creation> Creations { get; } = [];

        public Dictionary<int, Vector3> Eyes { get; } = [];

        public List<string> Log { get; } = [];

        public List<Vector3> Pushes { get; } = [];

        public double Now { get; set; } = 100;

        public int Add(Creation creation, bool owned)
        {
            Assert.Equal(owned, creation.IsWorldOwned);
            Creations[creation.Id] = creation;
            return creation.Id;
        }

        public Creation? FindCreation(int id) => Creations.GetValueOrDefault(id);

        public Vector3? EyeOf(int peer) => Eyes.TryGetValue(peer, out var eye) ? eye : null;

        public int NextCreationId() => _nextId++;

        public string? CheckClear(Creation? target, PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose) => null;

        public string? CheckPartClear(PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose,
            IReadOnlyCollection<int> ignoreCreations, int? ignoreLiftOf) => null;

        public string? CheckLiftClear(LiftState lift, IReadOnlyCollection<int> ignoreCreations) => null;

        public void Added(Creation creation)
        {
            Creations[creation.Id] = creation;
            Log.Add($"added {creation.Id}");
        }

        public void Changed(Creation creation) => Log.Add($"changed {creation.Id}");

        public void Removed(int creationId)
        {
            Creations.Remove(creationId);
            Log.Add($"removed {creationId}");
        }

        public void Moved(Creation creation) => Log.Add($"moved {creation.Id}");

        public void LiftChanged(int owner, LiftState? lift) => Log.Add($"lift {owner}");

        public void Push(Creation creation, int bodyIndex, Vector3 point, Vector3 impulse) => Pushes.Add(impulse);
    }

    private sealed class Vector3Comparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;

        public int GetHashCode(Vector3 v) => 0;
    }
}

/// <summary>Placing parts as test one-liners.</summary>
internal static class CreationTestExtensions
{
    /// <summary>Places <paramref name="part"/> at <paramref name="cell"/> (attached to what is there) and returns the Creation.</summary>
    public static Creation With(this Creation creation, string part, GridCell cell = default)
    {
        Assert.True(creation.TryPlacePart(part, cell, GridRotation.Identity, out _, out var error), error);
        return creation;
    }
}
