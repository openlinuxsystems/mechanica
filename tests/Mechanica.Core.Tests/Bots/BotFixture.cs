using System.Numerics;
using System.Text.Json.Nodes;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Survival;
using Mechanica.Core.Tests.Survival;

namespace Mechanica.Core.Tests.Bots;

/// <summary>The survival fixture plus bot types, the bot actions, a raid table and a fake bot world.</summary>
internal sealed class BotFixture : IBotWorld, IRaidWorld
{
    public const string Grunt = "test:grunt";
    public const string Shooter = "test:shooter";
    public const string Bomber = "test:bomber";
    public const string Tank = "test:tank";
    public const string Critter = "test:critter";

    public static readonly ActionCaller Brain = new(1, ActionSource.Bot);

    public BotFixture(GameMode mode = GameMode.Survival, RaidTableDefinition? raids = null)
    {
        Survival = new SurvivalFixture(mode: mode);
        Book = BotBook.From(Survival.Catalog,
            ("test", new BotDefinition
            {
                Id = "grunt", Name = "Grunt", Family = "grunt", Health = 60, Speed = 5, RaidCost = 1,
                Size = new BotSize { Radius = 0.4f, Height = 1.2f },
                Attacks = [new BotAttackDefinition { Id = "whip", Kind = BotAttackKind.Melee, Damage = 15, Range = 1.5f, Interval = 1 }],
                Drops = [new BotDropDefinition { Item = SurvivalFixture.Scrap, Chance = 1, Count = 2 }],
                Behavior = JsonNode.Parse("""{ "type": "find_crop" }""")!.AsObject(),
            }),
            ("test", new BotDefinition
            {
                Id = "shooter", Name = "Shooter", Health = 40, Speed = 3, RaidCost = 2,
                Attacks =
                [
                    new BotAttackDefinition { Id = "tape", Kind = BotAttackKind.Ranged, Damage = 25, Range = 20, Interval = 2,
                        DamageOverTime = new BotDamageOverTime { PerSecond = 5, Seconds = 2 } },
                ],
            }),
            ("test", new BotDefinition
            {
                Id = "bomber", Name = "Bomber", Health = 40, Speed = 6, RaidCost = 4,
                Attacks = [new BotAttackDefinition { Id = "boom", Kind = BotAttackKind.Area, Damage = 40, Radius = 4, Interval = 1, KillsSelf = true }],
            }),
            ("test", new BotDefinition { Id = "tank", Name = "Tank", Health = 1500, Speed = 3, RaidCost = 5, MeleeImmune = true }),
            ("test", new BotDefinition { Id = "critter", Name = "Critter", Health = 30, Speed = 2, Passive = true }));
        Raids = RaidTable.From(raids ?? new RaidTableDefinition
        {
            CheckInterval = 10, Cooldown = 100, Warning = 5, SpawnDistance = 30, Duration = 60,
            Levels =
            [
                new RaidLevelDefinition { Level = 1, MinValue = 3, Budget = 4, Bots = [new RaidBotDefinition { Bot = Grunt, Count = 4 }] },
                new RaidLevelDefinition { Level = 2, MinValue = 9, Budget = 8, Bots =
                    [new RaidBotDefinition { Bot = Grunt, Count = 2 }, new RaidBotDefinition { Bot = Shooter, Count = 3 }] },
            ],
        }, Book);
        Bots = new BotActions(Book, new BotSettings { CorpseSeconds = 3 }, Survival.Survival, Survival.World, this) { Seed = 7 };
        Survival.Survival.Bots = Bots;
        Bots.Register(Survival.Dispatcher);
        Director = new RaidDirector(Raids, Bots, Survival.Survival, this);
        Director.Register(Survival.Dispatcher);
    }

    public SurvivalFixture Survival { get; }
    public BotBook Book { get; }
    public RaidTable Raids { get; }
    public BotActions Bots { get; }
    public RaidDirector Director { get; }

    /// <summary>What a bot's ray hits; null means clear.</summary>
    public Func<Vector3, Vector3, int, RayHit?> Ray { get; set; } = (_, _, _) => null;

    public double Now => Survival.World.Now;

    public RayHit? Cast(Vector3 from, Vector3 to, int ignoreBot) => Ray(from, to, ignoreBot);

    public Vector3 GroundBelow(Vector3 point) => point with { Y = 0 };

    public int Spawn(string type, Vector3 at) =>
        Survival.Ok<SpawnBotResult>(Brain, BotActions.SpawnBot, new SpawnBotArgs(type, [at.X, at.Y, at.Z])).Bot;

    /// <summary>A soil part with a seed planted, at <paramref name="at"/>; returns its creation id (the part is 1).</summary>
    public int Crop(Vector3 at)
    {
        var id = Survival.Add(SurvivalFixture.Soil, at);
        Survival.World.Creations[id].SoilOf(1)!.Plant(SurvivalFixture.Seed);
        return id;
    }

    public void Advance(double seconds)
    {
        Survival.World.Now += seconds;
        Bots.Tick((float)seconds);
        Director.Tick();
    }
}
