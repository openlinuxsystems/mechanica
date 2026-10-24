using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Characters;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Player;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Player;
using Mechanica.Scripting;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Consumables batch C2b, host plus client. Bloom Tea in hand is held upright in the fist (the drink prop) on both
/// machines; drinking it plays the drink clip on the client's arms and on the body the host draws, heals 20 once, and the
/// player may keep sprinting meanwhile. A Grazer Shake heals 100. Fertilizer used on a watered tomato makes it grow 15 times
/// faster: the host stores the boost on the soil, the client is sent it and draws the soil fertilized, the boost saves
/// with the soil's Creation, and the harvest clears it.
/// </summary>
public partial class DrinksTest : StagedTest
{
    private const string Tea = "vanilla:bloom_tea";
    private const string Shake = "vanilla:grazer_shake";
    private const string Fertilizer = "vanilla:fertilizer";
    private const string Seed = "vanilla:tomato_seed";
    private const string Bucket = "vanilla:water_bucket_full";
    private const string Soil = "vanilla:soil";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private Vector3 _soilAt;
    private int _soil;
    private int _soilPart;
    private float _health;
    private bool _hostDrank;
    private bool _clientDrank;
    private bool _sprintBlocked;
    private float _grownBefore;
    private bool _watered;

    protected override string Tag => "drinks";

    protected override int StageTimeoutFrames => 900;

    private int Peer => _client.Peer;

    private CharacterModel HostView => (CharacterModel)_host.Main.PlayerSync.Players[Peer].Character!;

    private CharacterModel ClientView => (CharacterModel)_client.Main.LocalPlayer!.Arms!;

    private SurvivalActions HostSurvival => _host.Main.Survival.Actions!;

    private SoilState HostSoil => _host.Main.Creations.Find(_soil)!.SoilOf(_soilPart)!;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins and is given drinks, Fertilizer, a seed and water", _ =>
        {
            if (_client.Main.Inventory.Own is null || _client.Main.Tools is null || _client.Main.LocalPlayer is not { } player
                || !player.IsOnFloor() || !_host.Main.PlayerSync.Players.ContainsKey(Peer) || _client.Main.Survival.Own is null)
                return false;
            var pockets = _host.Main.Inventory.Items!.PlayerInventory(Peer);
            pockets.Add(Tea, 2);
            pockets.Add(Shake, 1);
            pockets.Add(Fertilizer, 2);
            pockets.Add(Seed, 1);
            pockets.Add(Bucket, 2);
            return true;
        });
        Add("Bloom Tea is held upright in the fist on both machines", frame =>
        {
            var tools = _client.Main.Tools!;
            if (Count(Tea) != 2)
                return false;
            if (tools.Current != tools.Item || tools.Item.InHand?.Item != Tea)
            {
                tools.Item.Choose(Tea);
                if (frame % 30 == 0)
                    tools.Use(tools.Item);
                return false;
            }
            if (HostView.HeldNode is null || ClientView.HeldNode is null || HostView.Animator.Prop != CharacterProp.Drink)
                return frame < 300 ? false : Check(false, $"the host draws the drink prop ({HostView.Animator.Prop})");
            return Check(ClientView.Animator.Prop == CharacterProp.Drink, $"the client's arms hold a drink ({ClientView.Animator.Prop})");
        });
        Add("drinking it plays the drink clip on both machines, heals 20 once and does not stop a sprint", frame =>
        {
            var item = _client.Main.Tools!.Item;
            if (frame == 0)
            {
                HostSurvival.Damage(Peer, 50, "test");
                return false;
            }
            if (frame == 10)
            {
                _health = _client.Main.Survival.Own!.Health;
                item.Click(place: false);
            }
            if (frame == 14)
                item.Click(place: false); // still drinking: drinks nothing
            if (frame < 10)
                return false;
            _hostDrank |= HostView.Animator.Gesture == "drink";
            _clientDrank |= ClientView.Animator.Gesture == "drink_fp";
            _sprintBlocked |= !_client.Main.LocalPlayer!.CanSprint;
            if (frame < 120)
                return false;
            Check(_client.Main.Survival.Own!.Health == _health + 20 && Count(Tea) == 1,
                $"one Bloom Tea is drunk and heals 20 ({_health} -> {_client.Main.Survival.Own!.Health}, {Count(Tea)} left)");
            Check(_hostDrank, "the host's view plays the drink clip");
            Check(_clientDrank, "the client's arms play the first-person drink clip");
            return Check(!_sprintBlocked, "the player may sprint while drinking");
        });
        Add("a Grazer Shake heals 100", frame =>
        {
            var tools = _client.Main.Tools!;
            if (frame == 0)
            {
                HostSurvival.Damage(Peer, 60, "test"); // 70 to 10
                tools.Item.Choose(Shake);
                return false;
            }
            if (frame == 30)
            {
                _health = _client.Main.Survival.Own!.Health;
                tools.Item.Click(place: false);
            }
            if (frame < 120)
                return false;
            return Check(_client.Main.Survival.Own!.Health == Mathf.Min(100f, _health + 100) && Count(Shake) == 0,
                $"the shake heals to full ({_health} -> {_client.Main.Survival.Own!.Health}, {Count(Shake)} left)");
        });
        Add("the client places soil, plants a tomato seed and waters it", frame =>
        {
            if (frame == 0)
            {
                _client.Main.Tools!.Select(0);
                _results.Clear();
                _soilAt = _client.Main.LocalPlayer!.GlobalPosition + new Vector3(0, 0.125f, -2);
                Request(BuildActions.PlacePart, new PlacePartArgs(Soil, GridCell.Zero, Position: _soilAt.ToNumerics().ToVec3()));
                return false;
            }
            if (_results.Count == 1 && _results[0].Ok && _soil == 0)
            {
                var placed = _results[0].Value!.Value.Deserialize<PlacePartResult>(ActionJson.Options)!;
                (_soil, _soilPart) = (placed.Creation, placed.Part);
                LookAt(_soilAt);
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Seed), Creation: _soil, Part: _soilPart));
                return false;
            }
            if (_results.Count == 2 && _results.All(r => r.Ok) && !_watered)
            {
                _watered = true;
                Water();
                return false;
            }
            if (_results.Any(r => !r.Ok))
                return Check(false, $"placing, planting or watering failed ({string.Join("; ", _results.Where(r => !r.Ok))})");
            return _results.Count == 3 && HostSoil.Water > 0;
        });
        Add("Fertilizer aimed at nothing is refused; on the crop it boosts it 15 times on the host", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Fertilizer)));
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Fertilizer), Creation: _soil, Part: _soilPart));
                return false;
            }
            if (_results.Count < 2)
                return false;
            Check(!_results[0].Ok && _results[0].Message!.Contains("growing crop"), $"aimed at nothing it is refused ({_results[0].Message})");
            Check(_results[1].Ok, $"used on the crop it is taken ({_results[1].Message})");
            return Check(HostSoil.Boost == 15f, $"the host's soil grows 15 times faster ({HostSoil.Boost})");
        });
        Add("the client is sent the boost and draws the soil fertilized, and a second is refused", frame =>
        {
            if (frame == 0)
            {
                _results.Clear();
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Fertilizer), Creation: _soil, Part: _soilPart));
            }
            if (_results.Count == 0 || Count(Fertilizer) != 1)
                return false;
            if (!_client.Main.Survival.CropNodes.TryGetValue((_soil, _soilPart), out var crop) || crop.View?.Boost != 15f)
                return false;
            if ((_client.Main.SoilLooks.FertilizedOf(_soil, _soilPart) ?? 0f) < 0.99f)
                return false;
            return Check(!_results[0].Ok && _results[0].Message!.Contains("already fertilized"), $"a second is refused ({_results[0].Message})");
        });
        Add("the boosted crop grows 15 times as fast on the same water", frame =>
        {
            if (frame == 0)
            {
                HostSurvival.WaterSoil(new ActionCaller(Peer, ActionSource.Player), _soil, _soilPart, 300);
                _grownBefore = HostSoil.Grown;
                _health = HostSoil.Water;
                _host.Main.Scripts.Runtime.Time += 10;
                return false;
            }
            if (HostSoil.Grown <= _grownBefore + 1)
                return frame < 300 ? false : Check(false, $"the crop grows ({HostSoil.Grown})");
            var grew = HostSoil.Grown - _grownBefore;
            var drank = _health - HostSoil.Water;
            GD.Print($"[drinks] grew {grew} s on {drank} s of water");
            return Check(Mathf.Abs(grew / drank - 15f) < 0.5f, $"15 s of growth per second of water ({grew} on {drank})");
        });
        Add("the boost saves with the soil's Creation", _ =>
        {
            var creation = _host.Main.Creations.Find(_soil)!;
            var json = CreationJson.Write(creation);
            var diagnostics = new List<DataDiagnostic>();
            var read = CreationJson.Read(json, _host.Main.Survival.Catalog, "test", diagnostics);
            Check(diagnostics.Count == 0, $"it reads back cleanly ({string.Join("; ", diagnostics)})");
            return Check(read?.SoilOf(_soilPart)?.Boost == 15f, $"the read soil keeps its boost ({read?.SoilOf(_soilPart)?.Boost})");
        });
        Add("the harvest clears the boost on both machines", frame =>
        {
            if (frame == 0)
            {
                _host.Main.Scripts.Runtime.Time += 90; // a 1260 s tomato boosted 15 times is ripe after 84 s
                return false;
            }
            if (frame == 30)
            {
                _results.Clear();
                Request(Core.Logic.LogicActions.ActivatePart, new Core.Logic.LogicPartArgs(_soil, _soilPart));
            }
            if (frame < 30 || _results.Count == 0)
                return false;
            if (!_results[0].Ok)
                return Check(false, $"the harvest is refused ({_results[0]})");
            if (HostSoil.Boost != 1f || (_client.Main.SoilLooks.FertilizedOf(_soil, _soilPart) ?? 0f) > 0.01f)
                return frame < 400 ? false : Check(false, $"the boost goes ({HostSoil.Boost}, {_client.Main.SoilLooks.FertilizedOf(_soil, _soilPart)})");
            return true;
        });
    }

    // Throws a full bucket at the soil.
    private void Water()
    {
        var aim = (_soilAt - _client.Main.LocalPlayer!.Head.GlobalPosition).Normalized();
        Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Bucket), new Vec3(aim.X, aim.Y, aim.Z)));
    }

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    private int Count(string item) => _client.Main.Inventory.Own?.Items.Where(s => s.Item == item).Sum(s => s.Count) ?? 0;

    private int SlotOf(string item) => _client.Main.Inventory.Own!.Items.First(s => s.Item == item).Slot;

    private void LookAt(Vector3 target)
    {
        var player = _client.Main.LocalPlayer!;
        var d = (target - player.Head.GlobalPosition).Normalized();
        player.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Asin(d.Y) };
    }
}
