using System.Numerics;
using System.Text.Json.Nodes;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Scripting.Bots;

namespace Mechanica.Scripting.Tests.Bots;

/// <summary>
/// A host world for bot brain tests: players and soil in memory, the real survival and bot actions
/// behind a real dispatcher, bot scripts in a temporary folder, and <see cref="Step"/> to walk bots.
/// </summary>
internal sealed class BotRig : ISurvivalWorld, IInventoryWorld, IBotWorld, IRaidWorld, IBotScriptSource, IDisposable
{
    public const string Seed = "test:seed";
    public const string Crop = "test:crop";
    public const string Soil = "test:soil";
    public const string Scrap = "test:scrap";

    private int _nextCreation = 1;

    public BotRig(params BotDefinition[] bots)
    {
        Directory.CreateDirectory(Root);
        Catalog = new PartCatalog()
            .Add(Seed, new PartDefinition { Id = "seed", Name = "Seed", Category = PartCategory.Consumable, StackSize = 20,
                Seed = new SeedDefinition { Crop = Crop, GrowTime = 40, Yield = 1, Value = 5 } })
            .Add(Crop, new PartDefinition { Id = "crop", Name = "Crop", Category = PartCategory.Consumable, StackSize = 20 })
            .Add(Scrap, new PartDefinition { Id = "scrap", Name = "Scrap", Category = PartCategory.Resource, StackSize = 20 })
            .Add(Soil, new PartDefinition { Id = "soil", Name = "Soil", Category = PartCategory.Decor, Mass = 5, Durability = 3, Size = GridSize.One,
                StackSize = 10, Soil = new SoilDefinition { Capacity = 60, Growth = 1 } });
        Book = BotBook.From(Catalog, [.. bots.Select(b => ("test", b))]);
        var inventories = new InventoryActions(Catalog, RecipeBook.Empty, new InventorySettings(), this);
        Survival = new SurvivalActions(Catalog, new SurvivalSettings(), inventories, this);
        Survival.Register(Dispatcher);
        Bots = new BotActions(Book, new BotSettings { ThinkInterval = 0.1f }, Survival, this, this);
        Survival.Bots = Bots;
        Bots.Register(Dispatcher);
        Raids = new RaidDirector(RaidTable.None, Bots, Survival, this);
        Runtime = new BotRuntime(new ScriptSettings(), Dispatcher, Bots, Raids, this, this);
        Runtime.Message += Messages.Add;
    }

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "mechanica-bot-tests", Guid.NewGuid().ToString("N"));
    public PartCatalog Catalog { get; }
    public BotBook Book { get; }
    public ActionDispatcher Dispatcher { get; } = new();
    public SurvivalActions Survival { get; }
    public BotActions Bots { get; }
    public RaidDirector Raids { get; }
    public BotRuntime Runtime { get; }
    public List<ScriptMessage> Messages { get; } = [];
    public Dictionary<int, Creation> Creations { get; } = [];
    public Dictionary<int, Vector3> Feet { get; } = [];
    public double Now { get; set; } = 100;

    public IEnumerable<ScriptMessage> Errors => Messages.Where(m => m.Kind == ScriptMessageKind.Error);

    public IEnumerable<string> Prints => Messages.Where(m => m.Kind == ScriptMessageKind.Print).Select(m => m.Text);

    public static JsonObject Tree(string json) => JsonNode.Parse(json)!.AsObject();

    public void Write(string relative, string source)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddTicks(Interlocked.Increment(ref _stamp)));
    }

    private static long _stamp;

    public void AddPlayer(int peer, Vector3 feet)
    {
        Feet[peer] = feet;
        Survival.Join(peer);
    }

    public int Spawn(string type, Vector3 at)
    {
        var result = Dispatcher.Dispatch(new ActionCaller(1, ActionSource.Bot), BotActions.SpawnBot, new SpawnBotArgs(type, [at.X, at.Y, at.Z]));
        Assert.True(result.Ok, result.ToString());
        return Bots.Bots.Keys.Max();
    }

    /// <summary>Soil with a crop planted; returns its creation id (the part is 1).</summary>
    public int Plant(Vector3 at)
    {
        var creation = new Creation(_nextCreation++, Catalog);
        Assert.True(creation.TryPlacePart(Soil, new GridCell(0, 0, 0), default, out _, out var error), error);
        creation.SetAllBodyPoses(new Pose(at, Quaternion.Identity));
        creation.SoilOf(1)!.Plant(Seed);
        Creations[creation.Id] = creation;
        return creation.Id;
    }

    /// <summary>Runs <paramref name="seconds"/> of the host: brains think, bots walk where they asked to.</summary>
    public void Step(float seconds, float dt = 0.05f)
    {
        for (var t = 0f; t < seconds - 1e-4f; t += dt)
        {
            Now += dt;
            Runtime.Update(dt);
            foreach (var bot in Bots.Bots.Values)
                Bots.Moved(bot.Id, bot.Position + bot.Desired * dt, false);
            Bots.Tick(dt);
        }
    }

    // ---- IBotScriptSource ----

    public ScriptFile? FindScript(Bot bot) =>
        bot.Script is { } key ? new ScriptFile(key, bot.ModId, Path.Combine(Root, key[(key.IndexOf(':') + 1)..])) : null;

    // ---- Worlds ----

    public RayHit? Cast(Vector3 from, Vector3 to, int ignoreBot) => null;

    public IEnumerable<Creation> AllCreations => Creations.Values;

    public Creation? FindCreation(int id) => Creations.GetValueOrDefault(id);

    public Vector3? EyeOf(int peer) => Feet.TryGetValue(peer, out var f) ? f + new Vector3(0, 1.6f, 0) : null;

    public Vector3? FeetOf(int peer) => Feet.TryGetValue(peer, out var f) ? f : null;

    public Vector3? LookOf(int peer) => Feet.ContainsKey(peer) ? -Vector3.UnitZ : null;

    public Vector3 SpawnOf(int peer) => Vector3.Zero;

    public void Teleport(int peer, Vector3 feet) => Feet[peer] = feet;

    public RayHit? Cast(Vector3 from, Vector3 direction, float range, int ignorePlayer) => null;

    public Vector3 GroundBelow(Vector3 point) => point with { Y = 0 };

    public bool IsWater(Vector3 point) => false;

    public WorldObjectPlacement? FindObject(WorldObjectId id) => null;

    public IEnumerable<WorldObjectPlacement> ObjectsNear(Vector3 point, float radius) => [];

    public float AddObjectDamage(WorldObjectId id, float damage) => 0;

    public void DestroyObject(WorldObjectId id)
    {
    }

    public SledgeResult DamagePart(Creation creation, int partId, float damage) => new(false, null, false, []);

    public void Push(Creation creation, int bodyIndex, Vector3 point, Vector3 impulse)
    {
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
