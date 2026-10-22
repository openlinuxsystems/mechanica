using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Bots;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Items;
using Mechanica.Core.Player;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Survival;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Consumables batch C3, host plus client, on a flat world. A tame Grazer and a tame calf live by a Feeding Trough with
/// five carrots in it: the Grazer eats all five (the calf none) and then has Grazer Milk ready, which the client sees as
/// the cue over it. Aimed at it, Use offers to collect the milk, and doing so puts a bottle in the client's inventory and
/// clears the cue; the meals it eats next save with the world. Drinking the milk heals 20. Killing the Grazer drops
/// Grazer Steak, which the client sees and picks up.
/// </summary>
public partial class HusbandryTest : StagedTest
{
    private const string Grazer = "vanilla:grazer";
    private const string Calf = "vanilla:grazer_calf";
    private const string Trough = "vanilla:feeding_trough";
    private const string Carrot = "vanilla:carrot";
    private const string Milk = "vanilla:grazer_milk";
    private const string Steak = "vanilla:grazer_steak";

    private static readonly ActionCaller Driver = new(Creation.HostPeer, ActionSource.Bot);

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private SurvivalHud _hud = null!;
    private Vector3 _feet;
    private Inventory _trough = null!;
    private int _grazer;
    private int _calf;
    private float _health;

    protected override string Tag => "husbandry";

    protected override int StageTimeoutFrames => 900;

    private int Peer => _client.Peer;

    private BotActions HostBots => _host.Main.Bots.Bots!;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins", _ =>
        {
            if (_client.Main.Inventory.Own is null || _client.Main.Tools is null || _client.Main.LocalPlayer is not { } player
                || !player.IsOnFloor() || !_host.Main.PlayerSync.Players.ContainsKey(Peer) || _client.Main.Survival.Own is null)
                return false;
            _feet = player.GlobalPosition;
            _hud = new SurvivalHud
            {
                Name = "TestSurvivalHud", Survival = _client.Main.Survival, Actions = _client.Main.Actions, Players = _client.Main.PlayerSync,
                Catalog = _client.Main.Survival.Catalog, Playing = () => true,
            };
            _client.Main.AddChild(_hud);
            return true;
        });
        Add("a Feeding Trough with five carrots, a tame Grazer and a tame calf by it", frame =>
        {
            if (frame == 0)
            {
                var at = _feet + new Vector3(4, 0.27f, -4);
                var placed = Act(BuildActions.PlacePart, new PlacePartArgs(Trough, GridCell.Zero, Position: at.ToNumerics().ToVec3()));
                Check(placed.Ok, $"the trough is placed ({placed})");
                var made = placed.Value!.Value.Deserialize<PlacePartResult>(ActionJson.Options)!;
                _trough = _host.Main.Inventory.Items!.Find(InventoryRef.OfPart(made.Creation, made.Part))!;
                Check(_trough.Add(Carrot, 5) == 5, "the trough takes five carrots");
                _grazer = Spawn(Grazer, at + new Vector3(-1.5f, 0, 1.5f));
                _calf = Spawn(Calf, at + new Vector3(1.5f, 0, 1.5f));
                HostBots.Keep(_grazer);
                HostBots.Keep(_calf);
                return false;
            }
            return _client.Main.Bots.Bodies.ContainsKey(_grazer) && _client.Main.Bots.Bodies.ContainsKey(_calf);
        });
        Add("the Grazer eats the five carrots from the trough, the calf none, and has milk ready on both peers", frame =>
        {
            // a meal every eat_every seconds: hurried along here
            if (frame % 15 == 0)
                HostBots.Hurry(_grazer);
            if (_client.Main.Bots.Bodies.GetValueOrDefault(_grazer)?.View is not { Ready: true } view)
                return false;
            var grazer = HostBots.Find(_grazer)!;
            Check(grazer.Meals == 5 && view.Meals == 5, $"it ate five meals ({grazer.Meals}, the client sees {view.Meals})");
            Check(_trough.Count(Carrot) == 0, $"the trough is empty ({_trough.Count(Carrot)})");
            Check(HostBots.Find(_calf)!.Meals == 0 && !HostBots.Find(_calf)!.Ready, "the calf eats nothing and gives no milk");
            Check(_client.Main.Bots.Bodies[_grazer].ProduceCue.Visible, "the client shows the milk cue over it");
            return Check(!_client.Main.Bots.Bodies[_calf].ProduceCue.Visible, "and none over the calf");
        });
        Add("aimed at the Grazer, Use collects the milk into the client's inventory", frame =>
        {
            var at = HostBots.Find(_grazer)!.Position.ToGodot();
            if (frame == 0)
            {
                _host.Main.PlayerSync.HostTeleport(Peer, at + new Vector3(2.2f, 0.3f, 0));
                return false;
            }
            if (frame < 30)
            {
                LookAt(_client.Main.Bots.Bodies[_grazer].GlobalPosition + new Vector3(0, 0.7f, 0));
                return false;
            }
            if (frame == 30)
            {
                var label = _hud.Interaction()?.Label;
                Check(label == "collect Grazer Milk from the Grazer", $"Use offers to collect the milk ({label})");
                Check(_hud.Interact(), "Use does it");
            }
            if (Count(Milk) != 1 || _client.Main.Bots.Bodies[_grazer].ProduceCue.Visible)
                return frame < 300 ? false : Check(false, $"the milk arrives ({Count(Milk)}, {HostBots.Find(_grazer)!.Ready})");
            Check(HostBots.Find(_grazer) is { Meals: 0, Ready: false }, "the Grazer counts its meals again");
            _results.Clear();
            _client.Main.Actions.Request(BotActions.CollectBot, new BotArgs(_grazer), _results.Add);
            return true;
        });
        var refused = false;
        Add("a second is refused; its cooldown and next meals save with the world", frame =>
        {
            if (_results.Count == 0)
                return false;
            if (!refused)
            {
                refused = true;
                Check(!_results[0].Ok && _results[0].Message!.Contains("nothing to give"), $"nothing to give yet ({_results[0].Message})");
                var cooling = _host.Main.Saves.Capture("Husbandry").Bots.Single(b => b.Type == Grazer);
                Check(cooling.ProduceIn is > 250 and <= 300, $"the save holds the cooldown left ({cooling.ProduceIn})");
                _trough.Add(Carrot, 2);
            }
            if (frame % 15 == 0)
                HostBots.Hurry(_grazer);
            if (HostBots.Find(_grazer)!.Meals < 2)
                return false;
            var saved = _host.Main.Saves.Capture("Husbandry").Bots.Single(b => b.Type == Grazer);
            return Check(saved.Meals == 2, $"and its two meals since ({saved.Meals})");
        });
        Add("drinking the Grazer Milk heals 20", frame =>
        {
            var tools = _client.Main.Tools!;
            if (frame == 0)
            {
                _host.Main.Survival.Actions!.Damage(Peer, 50, "test");
                tools.Item.Choose(Milk);
                tools.Use(tools.Item);
                return false;
            }
            if (frame == 30)
            {
                _health = _client.Main.Survival.Own!.Health;
                tools.Item.Click(place: false);
            }
            if (frame < 30 || Count(Milk) != 0 || _client.Main.Survival.Own!.Health <= _health)
                return frame < 300 ? false : Check(false, $"the milk is drunk ({Count(Milk)}, {_client.Main.Survival.Own!.Health})");
            return Check(_client.Main.Survival.Own!.Health == _health + 20, $"it heals 20 ({_health} -> {_client.Main.Survival.Own!.Health})");
        });
        var drop = 0;
        Add("killing the Grazer drops Grazer Steak that the client sees and picks up", frame =>
        {
            if (frame == 0)
            {
                _client.Main.Tools!.Item.Choose(null);
                HostBots.DamageBot(_grazer, 1000, "test", melee: false);
                return false;
            }
            if (drop == 0)
            {
                if (_client.Main.Survival.Drops.Values.FirstOrDefault(d => d.Items.Any(s => s.Item == Steak)) is not { } seen)
                    return false;
                var steaks = seen.Items.Where(s => s.Item == Steak).Sum(s => s.Count);
                Check(steaks is 1 or 2, $"one or two steaks drop ({steaks})");
                drop = seen.Id;
                _host.Main.PlayerSync.HostTeleport(Peer, seen.Position.ToGodot() + new Vector3(0.5f, 0.3f, 0));
                return false;
            }
            if (frame % 30 == 0 && Count(Steak) == 0)
                _client.Main.Actions.Request(SurvivalActions.PickUp, new PickUpArgs(drop));
            return Count(Steak) > 0;
        });
    }

    private int Spawn(string type, Vector3 at)
    {
        var result = Act(BotActions.SpawnBot, new SpawnBotArgs(type, [at.X, at.Y, at.Z]));
        Check(result.Ok, $"{type} spawns ({result})");
        return result.Ok ? result.Value!.Value.Deserialize<SpawnBotResult>(ActionJson.Options)!.Bot : -1;
    }

    private ActionResult Act<TArgs>(string action, TArgs args) => _host.Main.Actions.Dispatcher.Dispatch(Driver, action, args);

    private int Count(string item) => _client.Main.Inventory.Own?.Items.Where(s => s.Item == item).Sum(s => s.Count) ?? 0;

    private void LookAt(Vector3 target)
    {
        var player = _client.Main.LocalPlayer!;
        var d = (target - player.Head.GlobalPosition).Normalized();
        player.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Asin(d.Y) };
    }
}
