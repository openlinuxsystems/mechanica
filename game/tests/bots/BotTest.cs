using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Bots;
using Mechanica.Game.Building;
using Mechanica.Game.Mods;
using Mechanica.Scripting;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Bots and a raid, host plus client (chunk 21, D10), on a flat Survival world with a test bot whose
/// behaviour is only a data tree run by the Lua behaviour library. The client plants a carrot; the host
/// sets off a raid; the client sees the warning, then the raiders arrive, walk to the crop and destroy it;
/// the client kills them with the Sledgehammer and the raid ends. Every step is checked on the client, including the
/// raiders' animated model (the Green Scuttler's): its walk, attack, hit and death clips and its eye.
/// </summary>
public partial class BotTest : StagedTest
{
    private const string Raider = "test:raider";
    private const string Seed = "vanilla:carrot_seed";
    private const string Soil = "vanilla:soil";

    private readonly List<ActionResult> _results = [];
    private readonly HashSet<string> _clips = [];
    private float _eye;
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _soil;
    private int _soilPart;

    protected override string Tag => "bots";

    protected override int StageTimeoutFrames => 1200;

    private BotSync ClientBots => _client.Main.Bots;

    private int Peer => _client.Peer;

    public override void _Ready()
    {
        var mods = ModPaths.LoadContent();
        var book = BotBook.From(mods, ("test", new BotDefinition
        {
            Id = "raider",
            Name = "Raider",
            Family = "scuttler",
            Health = 30,
            Speed = 4,
            RaidCost = 1,
            Color = "#5a9e3a",
            Model = new BotModelDefinition
            {
                Mesh = "vanilla:meshes/bots/scuttler_green.glb",
                Attacks = new() { ["chomp"] = ["claw_combo"] },
                ClipSpeeds = new() { ["patrol"] = 0.93f, ["chase"] = 3f },
            },
            Attacks = [new BotAttackDefinition { Id = "chomp", Kind = BotAttackKind.Melee, Damage = 10, PartDamage = 10, Range = 1.5f, Interval = 1 }],
            Behavior = JsonNode.Parse("""
                { "type": "selector", "children": [
                  { "type": "sequence", "children": [
                    { "type": "find_crop" },
                    { "type": "chase", "attack": "chomp" },
                    { "type": "attack", "attack": "chomp" } ] },
                  { "type": "go_to_raid" } ] }
                """)!.AsObject(),
        }));
        var raids = RaidTable.From(new RaidTableDefinition
        {
            CheckInterval = 1000, Cooldown = 1000, Warning = 1, SpawnDistance = 10, Duration = 120,
            Levels = [new RaidLevelDefinition { Level = 1, MinValue = 1, Budget = 2, Bots = [new RaidBotDefinition { Bot = Raider, Count = 2 }] }],
        }, book);
        Check(!book.HasErrors && !raids.HasErrors, $"the test bot and raid table load ({string.Join("; ", book.Diagnostics.Concat(raids.Diagnostics))})");
        _harness = new NetTestHarness
        {
            Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat, BotTypes = book, RaidTable = raids,
        };
        AddChild(_harness);
        _host = _harness.StartHost(mods: mods);
        _client = _harness.StartClient("Client", mods: mods);

        Stage("the client joins a Survival world", _ =>
            _client.Main.Survival.Own is not null && _client.Main.Inventory.Own is not null && _client.Main.LocalPlayer is { } player && player.IsOnFloor());
        Stage("the client plants a carrot", frame =>
        {
            if (frame == 0)
                Request(BuildActions.PlacePart, new PlacePartArgs(Soil, GridCell.Zero, Position: (Feet(_client) + new Vector3(0, 0.125f, -2)).ToNumerics().ToVec3()));
            if (frame == 20 && _results.Count == 1)
            {
                var placed = Value<PlacePartResult>(_results[0]);
                (_soil, _soilPart) = (placed.Creation, placed.Part);
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Seed), Creation: _soil, Part: _soilPart));
            }
            if (_results.Count < 2 || _client.Main.Survival.Soil.GetValueOrDefault((_soil, _soilPart)) is not { Seed: not null })
                return false;
            Check(_results.All(r => r.Ok), $"placing and planting succeed ({string.Join("; ", _results)})");
            return true;
        });
        Stage("players cannot set off raids in Survival", frame =>
        {
            if (frame == 0)
                Request(RaidDirector.StartRaid, new StartRaidArgs(1));
            if (_results.Count == 0)
                return false;
            Check(!_results[0].Ok && _results[0].Message!.Contains("Creative"), $"StartRaid is refused ({_results[0]})");
            return true;
        });
        Stage("the host sets off a raid and the client sees the warning", frame =>
        {
            if (frame == 0)
                Check(_host.Main.Bots.Raids!.Start() is { Level: 1 }, "the crop sets off a level 1 raid");
            if (ClientBots.Raid.Phase != RaidPhases.Warning)
                return false;
            // Two players scale the budget of 2 by 1.3, to 3.
            var raid = ClientBots.Raid;
            Check(raid is { Players: 2, Budget: 3, Bots: 3 } && raid.Composition.Count == 3 && raid.Composition.All(b => b == Raider),
                $"three raiders are coming for two players ({raid}: {string.Join(", ", raid.Composition)})");
            Check(ClientBots.Bodies.Count == 0, "no raider has arrived yet");
            return true;
        });
        Stage("the raiders arrive on both peers", _ =>
        {
            if (ClientBots.Raid.Phase != RaidPhases.Attacking || ClientBots.Bodies.Count != 3)
                return false;
            var crop = CropPosition();
            foreach (var body in ClientBots.Bodies.Values)
            {
                var away = new Vector2(body.GlobalPosition.X - crop.X, body.GlobalPosition.Z - crop.Z).Length();
                Check(away is > 8 and < 12, $"bot {body.BotId} arrives about 10 m from the farm ({away:0.0} m)");
            }
            Check(_host.Main.Bots.Bodies.Count == 3, "the host shows them too");
            Check(ClientBots.Bodies.Values.All(b => b.Model is { Legs: 6 }), "the client shows each raider's model, with its six legs");
            return true;
        });
        Stage("the raiders walk to the crop and destroy it", _ =>
        {
            if (_client.Main.Survival.Soil.GetValueOrDefault((_soil, _soilPart)) is { Seed: not null })
                return false;
            var crop = CropPosition();
            Check(_host.Main.Bots.CropsDestroyed >= 1, "the host counted a destroyed crop");
            Check(_clips.Contains("chase") || _clips.Contains("patrol"), $"the client's raiders walk ({string.Join(", ", _clips)})");
            Check(_clips.Contains("claw_combo"), $"the client's raiders play their attack ({string.Join(", ", _clips)})");
            Check(ClientBots.Bodies.Values.Any(b => b.GlobalPosition.DistanceTo(crop) < 3f),
                $"the client shows a raider at the crop ({string.Join(", ", ClientBots.Bodies.Values.Select(b => b.GlobalPosition.DistanceTo(crop).ToString("0.0")))} m)");
            return true;
        });
        Stage("the client kills the raiders with the Sledgehammer and the raid ends", frame =>
        {
            var target = _host.Main.Bots.Bots!.Bots.Values.FirstOrDefault(b => !b.Dead);
            // No faster than one swing per sledge.swing_seconds, so a step every 80 frames.
            if (target is not null && frame % 80 == 0)
                _host.Main.PlayerSync.HostTeleport(Peer, target.Position.ToGodot() + new Vector3(1.5f, 0, 0));
            if (target is not null && frame % 80 == 20)
                Request(ToolActions.Sledge, new SledgeArgs(Bot: target.Id));
            if (ClientBots.Raid.Phase != RaidPhases.None || _host.Main.Bots.Bots!.Bots.Values.Any(b => !b.Dead))
                return false;
            Check(_results.Count >= 6 && _results.All(r => r.Ok), $"every swing lands ({string.Join("; ", _results)})");
            Check(_clips.Contains("hit"), $"the client's raiders flinch when hit ({string.Join(", ", _clips)})");
            Check(_eye > 0.9f, $"a fighting raider's eye turns to its engaged colour ({_eye:0.00})");
            Check(ClientBots.Bodies.Values.All(b => b.Model!.Clip == "death"), "the client's dead raiders play their death");
            return true;
        });
        Stage("the dead raiders are cleared away on the client", _ => ClientBots.Bodies.Count == 0);
        Stage("every vanilla bot model loads with its legs and clips", _ =>
        {
            var shared = new[] { "idle", "patrol", "chase", "fall", "alert", "lose_interest", "hit", "death", "jump", "land" };
            var vanilla = BotBook.Load(mods).All.Where(b => b.Model is not null && b.ModId == "vanilla").ToList();
            Check(vanilla.Count >= 12, $"the five Scuttlers, three Rakehands and four Spoolers have models ({vanilla.Count})");
            foreach (var type in vanilla)
            {
                var view = BotModelView.For(type, _client.Main.Survival.Assets, default);
                Check(view is not null, $"{type.QualifiedId}'s model loads");
                if (view is null)
                    continue;
                AddChild(view);
                var player = LiftBody.FindPlayer(view.Scene);
                var missing = shared.Concat(type.Model!.Attacks.Values.SelectMany(c => c)).Where(c => player?.HasAnimation(c) != true).ToList();
                var legs = type.Family is "rakehand" or "spooler" ? 2 : 6;
                Check(view.Legs == legs, $"{type.QualifiedId} has {legs} legs ({view.Legs})");
                Check(missing.Count == 0, $"{type.QualifiedId} has every clip (missing {string.Join(", ", missing)})");
                view.QueueFree();
            }
            return true;
        });
    }

    // Each stage starts with no results.
    private void Stage(string name, Func<int, bool> tick) => Add(name, frame =>
    {
        if (frame == 0)
        {
            _results.Clear();
            _clips.Clear();
            _eye = 0;
        }
        foreach (var body in _client.Main.Bots.Bodies.Values)
        {
            if (body.Model is { } model)
            {
                if (model.Clip is { } clip)
                    _clips.Add(clip);
                _eye = Math.Max(_eye, model.EyeEngaged);
            }
        }
        return tick(frame);
    });

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    private int SlotOf(string item) => _client.Main.Inventory.Own!.Items.First(s => s.Item == item).Slot;

    private Vector3 CropPosition() => _client.Main.Survival.CropNodes.TryGetValue((_soil, _soilPart), out var crop)
        ? crop.GlobalPosition
        : _client.Main.Creations.NodeOf(_soil)!.Bodies[0].GlobalPosition;

    private static Vector3 Feet(Instance who) => who.Main.LocalPlayer!.GlobalPosition;

    private static T Value<T>(ActionResult result) => result.Value!.Value.Deserialize<T>(ActionJson.Options)!;
}
