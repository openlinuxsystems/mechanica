using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Fire;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;

namespace Mechanica.Core.Tests.Survival;

/// <summary>A small survival world built in code: items, soil, a bed, weapons, and a fake host world.</summary>
internal sealed class SurvivalFixture
{
    public const string Seed = "test:seed";
    public const string Crop = "test:crop";
    public const string Bucket = "test:bucket";
    public const string EmptyBucket = "test:empty_bucket";
    public const string Soil = "test:soil";
    public const string Bed = "test:bed";
    public const string Blaster = "test:blaster";
    public const string Scattergun = "test:scatter";
    public const string Bomb = "test:bomb";
    public const string Block = "test:block";
    public const string Plank = "test:plank";
    public const string Chest = "test:chest";
    public const string Can = "test:can";
    public const string Scrap = "test:scrap";
    public const string Connector = HotbarTools.Connect;
    public const string Gun = "scripts/potato_gun.lua";

    public static readonly ActionCaller Me = new(2, ActionSource.Player);
    public static readonly ActionCaller You = new(3, ActionSource.Player);

    // Friendly fire is on here so the combat tests can hit one player with another; worlds start with it off.
    public SurvivalFixture(SurvivalSettings? settings = null, GameMode mode = GameMode.Survival, bool friendlyFire = true)
    {
        Catalog = new PartCatalog()
            .Add(Seed, new PartDefinition { Id = "seed", Name = "Seed", Category = PartCategory.Consumable, StackSize = 20,
                Seed = new SeedDefinition { Crop = Crop, GrowTime = 40, Yield = 2, Seeds = 1, Value = 3 }, Script = "scripts/seed.lua" })
            .Add(Crop, new PartDefinition { Id = "crop", Name = "Crop", Category = PartCategory.Consumable, StackSize = 20,
                Script = "scripts/food.lua", Properties = new JsonObject { ["health"] = 30 } })
            .Add(Bucket, new PartDefinition { Id = "bucket", Name = "Bucket", Category = PartCategory.Consumable, StackSize = 1,
                Script = "scripts/watering.lua", Properties = new JsonObject { ["seconds"] = 30, ["douse"] = 1, ["splash"] = 1.2, ["range"] = 8,
                    ["throw_speed"] = 7, ["throw_up"] = 0.35, ["empty"] = "empty_bucket" } })
            .Add(EmptyBucket, new PartDefinition { Id = "empty_bucket", Name = "Pail", Category = PartCategory.Consumable, StackSize = 3,
                Script = "scripts/bucket.lua" })
            .Add(Scrap, new PartDefinition { Id = "scrap", Name = "Scrap", Category = PartCategory.Resource, StackSize = 50 })
            // A hand tool that is also an item: crafted, kept on death.
            .Add(Connector, new PartDefinition { Id = "connect_tool", Name = "Connect Tool", Category = PartCategory.Tool })
            // Seeds, the bucket, food and weapons run vanilla item scripts (chunk 20b); the Scripting tests use them.
            .Add(Blaster, new PartDefinition { Id = "blaster", Name = "Blaster", Category = PartCategory.Weapon, Script = Gun,
                Properties = new JsonObject { ["damage"] = 20, ["interval"] = 1, ["range"] = 50, ["ammo"] = "crop", ["impulse"] = 5 } })
            .Add(Scattergun, new PartDefinition { Id = "scatter", Name = "Scattergun", Category = PartCategory.Weapon, Script = Gun,
                Properties = new JsonObject { ["damage"] = 5, ["interval"] = 1, ["range"] = 20, ["pellets"] = 5, ["spread"] = 10 } })
            .Add(Bomb, new PartDefinition { Id = "bomb", Name = "Bomb", Category = PartCategory.Weapon, StackSize = 10, Script = Gun,
                Properties = new JsonObject { ["damage"] = 40, ["interval"] = 1, ["range"] = 10, ["ammo"] = "bomb", ["radius"] = 4 } })
            .Add(Block, Placeable("block", PartCategory.Block) with { Durability = 2 })
            .Add(Plank, Placeable("plank", PartCategory.Block) with { Flammable = true })
            .Add(Chest, Placeable("chest", PartCategory.Container) with { Inventory = new InventoryDefinition { Slots = 4 } })
            .Add(Can, Placeable("can", PartCategory.Container) with { Inventory = new InventoryDefinition { Slots = 2, CarryContents = true } })
            // Soil runs the vanilla soil script (crops grow and are harvested in Lua); the Core tests run no scripts.
            .Add(Soil, Placeable("soil", PartCategory.Decor) with { Soil = new SoilDefinition { Capacity = 60, Growth = 2 },
                Script = "scripts/soil.lua", Properties = new JsonObject { ["interval"] = 0.5 } })
            .Add(Bed, Placeable("bed", PartCategory.Decor) with { RespawnPoint = true });
        Settings = settings ?? new SurvivalSettings
        {
            MaxHealth = 100,
            RespawnDelay = 5,
            Reach = 5,
            PickupReach = 3,
            DropLifetime = 100,
            MaxDrops = 8,
            StartingItems = [new ItemCount(Seed, 2)],
        };
        Inventories = new InventoryActions(Catalog, RecipeBook.Empty,
            new InventorySettings { Reach = 5, PlayerSlots = 4, AllowGive = true }, World);
        Tools = new ToolActions(new BuildSettings(), World, Catalog);
        World.Tools = Tools;
        Survival = new SurvivalActions(Catalog, Settings, Inventories, World) { Mode = mode, FriendlyFire = friendlyFire };
        Tools.Targets = Survival;
        Tools.Costs = Survival;
        World.Survival = Survival;
        Fire = new FireActions(new FireSettings(), Survival, World);
        Inventories.Register(Dispatcher);
        Tools.Register(Dispatcher);
        new BuildActions(Catalog, new BuildSettings(), World) { Costs = Survival }.Register(Dispatcher);
        Survival.Register(Dispatcher);
        Dispatcher.Gate = Survival.Gate;
        foreach (var peer in new[] { Me.Peer, You.Peer })
        {
            World.Feet[peer] = new Vector3(0, 0, 3 * (peer - 1));
            Survival.Join(peer);
        }
    }

    public PartCatalog Catalog { get; }
    public SurvivalSettings Settings { get; }
    public FakeWorld World { get; } = new();
    public ActionDispatcher Dispatcher { get; } = new();
    public InventoryActions Inventories { get; }
    public ToolActions Tools { get; }
    public SurvivalActions Survival { get; }
    public FireActions Fire { get; }

    public Inventory Pockets(ActionCaller caller) => Inventories.PlayerInventory(caller.Peer);

    public ActionResult Dispatch(ActionCaller caller, string action, object args) =>
        Dispatcher.Dispatch(caller, action, JsonSerializer.Serialize(args, ActionJson.Options));

    public T Ok<T>(ActionCaller caller, string action, object args)
    {
        var result = Dispatch(caller, action, args);
        Assert.True(result.Ok, result.ToString());
        return result.Value!.Value.Deserialize<T>(ActionJson.Options)!;
    }

    public void Ok(ActionCaller caller, string action, object args)
    {
        var result = Dispatch(caller, action, args);
        Assert.True(result.Ok, result.ToString());
    }

    public string Refused(ActionCaller caller, string action, object args)
    {
        var result = Dispatch(caller, action, args);
        Assert.False(result.Ok, $"{action} should be refused");
        return result.Message!;
    }

    /// <summary>A one-part Creation at <paramref name="at"/>; returns its id (the part is 1).</summary>
    public int Add(string part, Vector3 at)
    {
        var creation = new Creation(World.NextCreationId(), Catalog);
        Assert.True(creation.TryPlacePart(part, new GridCell(0, 0, 0), default, out _, out var error), error);
        creation.SetAllBodyPoses(new Pose(at, Quaternion.Identity));
        World.Added(creation);
        return creation.Id;
    }

    /// <summary>The slot holding <paramref name="item"/> in the caller's inventory.</summary>
    public int SlotOf(ActionCaller caller, string item) =>
        Pockets(caller).Slots.Select((s, i) => (s, i)).First(x => x.s?.Item == item).i;

    private static PartDefinition Placeable(string id, PartCategory category) => new()
    {
        Id = id,
        Name = char.ToUpperInvariant(id[0]) + id[1..],
        Category = category,
        Mass = 5,
        Durability = 3,
        Flammable = false,
        Size = GridSize.One,
        StackSize = 10,
    };

    internal sealed class FakeWorld : ISurvivalWorld, IToolWorld, IInventoryWorld, IFireWorld
    {
        private int _nextId = 1;

        public ToolActions Tools { get; set; } = null!;
        public SurvivalActions Survival { get; set; } = null!;
        public Dictionary<int, Creation> Creations { get; } = [];
        public Dictionary<int, Vector3> Feet { get; } = [];
        public Dictionary<int, Vector3> Looks { get; } = [];
        public Dictionary<WorldObjectId, WorldObjectPlacement> Objects { get; } = [];
        public WorldState State { get; } = new(1);
        public List<(Vector3 From, Vector3 Direction)> Casts { get; } = [];
        public Func<Vector3, Vector3, RayHit?> Ray { get; set; } = (_, _) => null;
        public float LastRange { get; private set; }
        public List<Vector3> Pushes { get; } = [];
        public List<Vector3> PushPoints { get; } = [];
        public List<(WorldObjectId Id, Vector3 At, bool Broke)> ObjectHits { get; } = [];

        public List<(Vector3 At, string Material)> Impacts { get; } = [];
        public List<(int Peer, Vector3 Feet)> Teleports { get; } = [];
        public List<(int Peer, Vector3 Velocity)> Knocks { get; } = [];
        public Vector3 Spawn { get; set; } = new(10, 0, 10);
        public float WaterLevel { get; set; } = -1;
        public double Now { get; set; } = 1000;
        public List<(string Sound, string Mod, Vector3 At)> Sounds { get; } = [];

        public IEnumerable<Creation> AllCreations => Creations.Values;

        public Creation? FindCreation(int id) => Creations.GetValueOrDefault(id);

        public Vector3? EyeOf(int peer) => Feet.TryGetValue(peer, out var feet) ? feet + new Vector3(0, 1.6f, 0) : null;

        public Vector3? FeetOf(int peer) => Feet.TryGetValue(peer, out var feet) ? feet : null;

        public Vector3? LookOf(int peer) => Looks.TryGetValue(peer, out var look) ? look : Feet.ContainsKey(peer) ? -Vector3.UnitZ : null;

        public Vector3 SpawnOf(int peer) => Spawn;

        public void Teleport(int peer, Vector3 feet)
        {
            Feet[peer] = feet;
            Teleports.Add((peer, feet));
        }

        public void Knock(int peer, Vector3 velocity) => Knocks.Add((peer, velocity));

        public RayHit? Cast(Vector3 from, Vector3 direction, float range, int ignorePlayer)
        {
            Casts.Add((from, direction));
            LastRange = range;
            return Ray(from, direction);
        }

        public Vector3 GroundBelow(Vector3 point) => point with { Y = 0 };

        public bool IsWater(Vector3 point) => point.Y <= WaterLevel;

        public WorldObjectPlacement? FindObject(WorldObjectId id) =>
            Objects.TryGetValue(id, out var o) && !State.IsDestroyed(id) ? o : null;

        public IEnumerable<WorldObjectPlacement> ObjectsNear(Vector3 point, float radius) =>
            Objects.Values.Where(o => !State.IsDestroyed(o.Id) && Vector3.Distance(o.Position, point) <= radius);

        public float AddObjectDamage(WorldObjectId id, float damage) => State.AddDamage(id, damage);

        public void DestroyObject(WorldObjectId id) => State.Destroy(id);

        public void PlaySound(string sound, string ownerModId, Vector3 position) => Sounds.Add((sound, ownerModId, position));

        public SledgeResult DamagePart(Creation creation, int partId, float damage) => Tools.DamagePart(creation, partId, damage);

        public void Push(Creation creation, int bodyIndex, Vector3 point, Vector3 impulse)
        {
            Pushes.Add(impulse);
            PushPoints.Add(point);
        }

        public void RemovePart(Creation creation, int partId) => Tools.RemovePart(creation, partId);

        public void Shatter(BreakDefinition breaks, Vector3 centre, float radius, uint seed) => Tools.Shatter(breaks, centre, radius, seed);

        public void Fell(BreakDefinition breaks, Vector3 foot, float height, Vector3 direction, float spin, uint seed) =>
            Tools.Fell(breaks, foot, height, direction, spin, seed);

        public void ObjectHit(WorldObjectId id, Vector3 at, bool broke) => ObjectHits.Add((id, at, broke));

        public void Impact(Vector3 at, string material) => Impacts.Add((at, material));

        // A part's shape reaches the sphere when its centre is within the radius plus half a block.
        public IEnumerable<(int Creation, int Part)> PartsNear(Vector3 point, float radius) =>
            [.. Creations.Values.SelectMany(c => c.Parts.Select(p => (Creation: c, Part: p)))
                .Where(x => Vector3.Distance(Centre(x.Creation, x.Part.Id), point) <= radius + MechanicaInfo.GridSize / 2)
                .Select(x => (x.Creation.Id, x.Part.Id))];

        public static Vector3 Centre(Creation creation, int partId)
        {
            var part = creation.FindPart(partId)!;
            return creation.BodyPose(creation.BodyIndexOf(partId))
                .Apply(PartGeometry.CentreMetres(creation.DefinitionOf(partId), part.Cell, part.Rotation).ToNumerics());
        }

        public void Breaking(Creation creation, int partId) => Survival.Spill(creation, partId);

        public int NextCreationId() => _nextId++;

        public string? CheckClear(Creation? target, PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose) => null;

        public string? CheckPartClear(PartDefinition definition, GridCell cell, GridRotation rotation, Pose pose,
            IReadOnlyCollection<int> ignoreCreations, int? ignoreLiftOf) => null;

        public string? CheckLiftClear(LiftState lift, IReadOnlyCollection<int> ignoreCreations) => null;

        public void Added(Creation creation) => Creations.Add(creation.Id, creation);

        public void Changed(Creation creation)
        {
        }

        public void Removed(int creationId) => Creations.Remove(creationId);

        public void Moved(Creation creation)
        {
        }

        public void LiftChanged(int owner, LiftState? lift)
        {
        }
    }
}
