using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Logic;
using Mechanica.Core.Player;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Scripting;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The survival loop, host plus client (chunk 20, D10), with the vanilla content on a flat world. The
/// client starts with the starting items and pays for what it builds; it plants, waters and harvests a
/// carrot, eats, shoots the host with a Potato Blaster (harmless until the host turns friendly fire on; seeds, water, food and weapons are Lua item scripts, chunk 20b),
/// throws a Popper that arcs on both machines and bursts, falls, dies, drops its things, respawns, picks them back up, and respawns at a bed
/// it slept in. Every step is checked on the host and on the client.
/// </summary>
public partial class SurvivalTest : StagedTest
{
    private const string Seed = "vanilla:carrot_seed";
    private const string Carrot = "vanilla:carrot";
    private const string Bucket = "vanilla:water_bucket";
    private const string Soil = "vanilla:soil";
    private const string Blaster = "vanilla:potato_blaster";
    private const string Potato = "vanilla:potato";
    private const string Popper = "vanilla:popper";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _soil;
    private int _soilPart;
    private int _bed;
    private Vector3 _diedAt;
    private int _projectile;
    private bool _projectileSeen;
    private double _thrownAt;
    private readonly Dictionary<int, double> _landed = [];

    protected override string Tag => "survival";

    // A respawn waits for the delay (5 s).
    protected override int StageTimeoutFrames => 900;

    private SurvivalActions HostSurvival => _host.Main.Survival.Actions!;

    private int Peer => _client.Peer;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Stage("the client joins a Survival world with the starting items", _ =>
        {
            if (_client.Main.Survival.Own is not { } own || _client.Main.Inventory.Own is null || Count(Seed) == 0
                || !_client.Main.Survival.Vitals.ContainsKey(1) || _client.Main.LocalPlayer is not { } player || !player.IsOnFloor())
                return false;
            Check(own is { Mode: GameMode.Survival, Dead: false } && own.Health == own.MaxHealth, $"full health in Survival ({own})");
            Check(Count(Seed) == 4 && Count(Bucket) == 2 && Count(Soil) == 8 && Count(Carrot) == 3, $"the starting items ({Items()})");
            Check(_host.Main.Survival.Vitals.ContainsKey(Peer), "the host shows the client's health too");
            return true;
        });
        Stage("making items from nothing is refused in Survival", frame =>
        {
            if (frame == 0)
                Request(InventoryActions.GiveItems, new GiveItemsArgs(Blaster, 1));
            if (_results.Count == 0)
                return false;
            Check(!_results[0].Ok && _results[0].Message!.Contains("Creative"), $"GiveItems is refused ({_results[0]})");
            return true;
        });
        Stage("the client pays for the soil it places", frame =>
        {
            if (frame == 0)
                Request(BuildActions.PlacePart, new PlacePartArgs(Soil, GridCell.Zero, Position: (Feet(_client) + new Vector3(0, 0.125f, -2)).ToNumerics().ToVec3()));
            if (_results.Count == 0 || Count(Soil) != 7)
                return false;
            Check(_results[0].Ok, $"the soil is placed ({_results[0]})");
            var placed = Value<PlacePartResult>(_results[0]);
            (_soil, _soilPart) = (placed.Creation, placed.Part);
            return _client.Main.Creations.Find(_soil) is not null;
        });
        Stage("the client plants a carrot seed and waters it twice", frame =>
        {
            if (frame == 0)
            {
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Seed), Creation: _soil, Part: _soilPart));
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Bucket), Creation: _soil, Part: _soilPart));
            }
            if (frame == 30)
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Bucket), Creation: _soil, Part: _soilPart));
            if (_results.Count < 3 || _client.Main.Survival.Soil.GetValueOrDefault((_soil, _soilPart)) is not { Seed: not null } soil || soil.Water < 250)
                return false;
            Check(_results.All(r => r.Ok), $"planting and watering succeed ({string.Join("; ", _results)})");
            var planted = System.Text.Json.Nodes.JsonNode.Parse(_results[0].Value!.Value.GetRawText())!;
            Check((string?)planted["seed"] == Seed && (string?)planted["crop"] == Carrot, $"the seed's script answers with the soil ({planted.ToJsonString()})");
            var host = HostSurvival.SoilViewOf(_host.Main.Creations.Find(_soil)!, _soilPart);
            // The host's water drains as the crop grows; the client sees it on the next stage.
            Check(host.Seed == soil.Seed && host.Water <= soil.Water + 0.01f && host.Water > 200, $"the host and client agree on the soil ({host} vs {soil})");
            Check(Count(Seed) == 3 && Count(Bucket) == 0, $"a seed and both buckets are used ({Items()})");
            Check(!soil.Ripe && _client.Main.Survival.CropNodes.ContainsKey((_soil, _soilPart)), "the client shows a young crop");
            return true;
        });
        Stage("an unripe crop cannot be harvested", frame =>
        {
            if (frame == 0)
                Request(LogicActions.ActivatePart, new LogicPartArgs(_soil, _soilPart));
            if (_results.Count == 0)
                return false;
            Check(!_results[0].Ok && _results[0].Message == "The Carrot is not ripe yet.", $"the soil's script refuses ({_results[0]})");
            return true;
        });
        Stage("the crop ripens on the host and the client sees it", frame =>
        {
            // Skip the host's world clock 300 s ahead: the soil's script catches up on growth from the clock,
            // as it does for a farm that was parked.
            if (frame == 0)
                _host.Main.Scripts.Runtime.Time += 300;
            if (_client.Main.Survival.Soil.GetValueOrDefault((_soil, _soilPart)) is not { Ripe: true })
                return false;
            var crop = _client.Main.Survival.CropNodes[(_soil, _soilPart)];
            var node = _client.Main.Creations.NodeOf(_soil)!;
            var part = node.Creation.FindPart(_soilPart)!;
            var top = node.Bodies[0].GlobalTransform * (PartGeometry.CentreMetres(node.Creation.DefinitionOf(_soilPart), part.Cell, part.Rotation).ToGodot()
                + new Vector3(0, 0.125f, 0));
            Check(crop.Visible && crop.GlobalPosition.DistanceTo(top) < 0.05f, $"the crop stands on top of the soil ({crop.GlobalPosition} vs {top})");
            return true;
        });
        Stage("the client harvests carrots and a seed", frame =>
        {
            // F on ripe soil is ActivatePart: the soil script's on_activate (D2).
            if (frame == 0)
                Request(LogicActions.ActivatePart, new LogicPartArgs(_soil, _soilPart));
            if (_results.Count > 0 && !_results[0].Ok)
            {
                Check(false, $"the harvest is refused ({_results[0]})");
                return true;
            }
            if (_results.Count == 0 || Count(Carrot) != 5)
                return false;
            Check(_results[0].Ok && Count(Seed) == 4, $"two carrots and the seed back ({_results[0]}; {Items()})");
            var items = System.Text.Json.Nodes.JsonNode.Parse(_results[0].Value!.Value.GetRawText())!["items"]!.AsArray();
            Check(items.Count == 2 && (string?)items[0]!["item"] == Carrot && (int)items[0]!["count"]! == 2, $"on_activate answers with what it gave ({_results[0]})");
            if (_client.Main.Survival.Soil.GetValueOrDefault((_soil, _soilPart)) is { Seed: not null })
                return false;
            return true;
        });
        Stage("eating heals", frame =>
        {
            if (frame == 0)
                HostSurvival.Damage(Peer, 30, "test");
            if (frame == 10)
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Carrot)));
            if (_results.Count == 0 || _client.Main.Survival.Own!.Health != 80 || Count(Carrot) != 4)
                return false;
            Check(_results[0].Ok, $"a carrot heals 10 ({_results[0]})");
            return true;
        });
        Stage("the item in hand eats too", frame =>
        {
            var item = _client.Main.Tools!.Item;
            if (frame == 0)
            {
                HostSurvival.Damage(Peer, 10, "test");
                item.Choose(Carrot);
            }
            if (frame == 10)
                Check(item.InHand?.Item == Carrot && item.Use(_results.Add), "the Item tool eats the carrot in hand");
            return _results.Count > 0 && _client.Main.Survival.Own!.Health == 80 && Count(Carrot) == 3;
        });
        Stage("with friendly fire off the client's shot does not hurt the host", frame =>
        {
            if (frame == 0)
            {
                Check(_client.Main.Survival.Own is { FriendlyFire: false } && !HostSurvival.FriendlyFire, "friendly fire starts off");
                var pockets = _host.Main.Inventory.Items!.PlayerInventory(Peer);
                pockets.Add(Blaster, 1);
                pockets.Add(Potato, 5);
                LookAt(_client, Eye(_host));
            }
            if (frame == 20)
            {
                var aim = (Eye(_host) - Eye(_client)).Normalized();
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Blaster), new Vec3(aim.X, aim.Y, aim.Z)));
            }
            if (_results.Count == 0 || Count(Potato) != 4)
                return false;
            var hits = System.Text.Json.Nodes.JsonNode.Parse(_results[0].Value!.Value.GetRawText())!["hits"]!.AsArray();
            Check(hits.Count == 1 && hits[0]!["player"]!.GetValue<int>() == 1 && hits[0]!["damage"]!.GetValue<double>() == 0,
                $"the shot reaches the host but deals nothing ({_results[0]})");
            Check(_client.Main.Survival.Vitals[1].Health == 100 && HostSurvival.HealthOf(1) == 100, "the host keeps full health");
            return true;
        });
        Stage("only the host turns friendly fire on, and the client is told", frame =>
        {
            if (frame == 0)
                Request(SurvivalActions.SetFriendlyFire, new SetFriendlyFireArgs(true));
            if (frame == 1)
                _host.Main.Actions.Request(SurvivalActions.SetFriendlyFire, new SetFriendlyFireArgs(true), _results.Add);
            if (_results.Count < 2 || _client.Main.Survival.Own is not { FriendlyFire: true })
                return false;
            Check(_results.Count(r => !r.Ok && r.Message!.Contains("Only the host")) == 1, $"a client cannot change it ({string.Join("; ", _results)})");
            Check(_results.Count(r => r.Ok) == 1 && HostSurvival.FriendlyFire, "the host can");
            return true;
        });
        Stage("the client shoots the host with a Potato Blaster", frame =>
        {
            if (frame == 0)
                LookAt(_client, Eye(_host));
            if (frame == 20)
            {
                var aim = (Eye(_host) - Eye(_client)).Normalized();
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Blaster), new Vec3(aim.X, aim.Y, aim.Z)));
            }
            if (_results.Count == 0 || _client.Main.Survival.Vitals[1].Health != 80 || Count(Potato) != 3)
                return false;
            var hits = System.Text.Json.Nodes.JsonNode.Parse(_results[0].Value!.Value.GetRawText())!["hits"]!.AsArray();
            Check(hits.Count == 1 && hits[0]!["kind"]!.GetValue<string>() == "player" && hits[0]!["player"]!.GetValue<int>() == 1
                && hits[0]!["damage"]!.GetValue<double>() == 20, $"the shot hits the host ({_results[0]})");
            Check(_host.Main.Scripts.Runtime.Held.GetValueOrDefault(Peer).Item == Blaster, "the host knows the blaster is in the client's hand");
            return true;
        });
        Stage("a thrown Popper arcs on the client too and bursts", frame =>
        {
            if (frame == 0)
            {
                _host.Main.Inventory.Items!.PlayerInventory(Peer).Add(Popper, 2);
                LookAt(_client, Eye(_host) + new Vector3(0, 3, 0)); // lob it up and over, away from the thrower
                HostSurvival.ProjectileLanded += OnLanded;
            }
            if (frame == 20)
            {
                var aim = -_client.Main.LocalPlayer!.Camera.GlobalBasis.Z;
                Request(ScriptRuntime.UseItem, new UseItemArgs(SlotOf(Popper), new Vec3(aim.X, aim.Y, aim.Z)));
            }
            if (_results.Count > 0 && _projectile == 0)
            {
                Check(_results[0].Ok, $"the Popper is thrown ({_results[0]})");
                var thrown = System.Text.Json.Nodes.JsonNode.Parse(_results[0].Value!.Value.GetRawText())!["projectiles"]!.AsArray();
                Check(thrown.Count == 1, $"one Popper flies ({_results[0]})");
                _projectile = thrown[0]!.GetValue<int>();
                _thrownAt = HostSurvival.World.Now;
            }
            if (_projectile == 0)
                return false;
            if (_client.Main.Survival.ProjectileNodes.ContainsKey(_projectile))
                _projectileSeen = true;
            if (!_landed.TryGetValue(_projectile, out var landedAt) || _client.Main.Survival.ProjectileNodes.ContainsKey(_projectile))
                return false;
            HostSurvival.ProjectileLanded -= OnLanded;
            Check(_projectileSeen, "the client drew the Popper in flight");
            Check(landedAt - _thrownAt > 0.2, $"it took time to land ({landedAt - _thrownAt:F2} s)");
            Check(Count(Popper) == 1 && !HostSurvival.Projectiles.Any(), $"one Popper is used up and none is left flying ({Items()})");
            return true;
        });
        Stage("a fall hurts", frame =>
        {
            if (frame == 0)
                _host.Main.PlayerSync.HostTeleport(Peer, Feet(_host) + new Vector3(3, 12, 0));
            var own = _client.Main.Survival.Own!;
            if (frame < 10 || own.Health >= 80)
                return false;
            Check(own.Health is > 0 and < 80, $"landing from 12 m costs health ({own.Health})");
            return true;
        });
        Stage("the client dies and drops what it carried", frame =>
        {
            if (frame == 0)
            {
                _diedAt = Feet(_client);
                HostSurvival.Damage(Peer, 500, "test");
            }
            if (_client.Main.Survival.Own is not { Dead: true } || _client.Main.Survival.Drops.Count == 0 || !PlayerOn(_host).Dead || Items() != "")
                return false;
            Check(_client.Main.LocalPlayer!.Dead, "the client's own player is dead too");
            var drop = _client.Main.Survival.Drops.Values.Single();
            Check(drop.Position.ToGodot().DistanceTo(_diedAt) < 1f && _client.Main.Survival.DropNodes.ContainsKey(drop.Id), "a pile lies where it died, on the client too");
            return true;
        });
        Stage("a dead player can only wait and respawn", frame =>
        {
            if (frame == 0)
            {
                Request(ScriptRuntime.UseItem, new UseItemArgs(0));
                Request(SurvivalActions.Respawn, new NoArgs());
            }
            if (_results.Count < 2)
                return false;
            Check(!_results[0].Ok && _results[0].Message!.Contains("dead"), $"eating is refused ({_results[0]})");
            Check(!_results[1].Ok && _results[1].Message!.Contains("respawn in"), $"respawning waits ({_results[1]})");
            return true;
        });
        Stage("the client respawns at the spawn", frame =>
        {
            RespawnWhenReady(frame);
            if (_client.Main.Survival.Own is not { Dead: false } own)
                return false;
            Check(own.Health == own.MaxHealth && !PlayerOn(_host).Dead, "back at full health");
            var spawn = _host.Main.PlayerSync.SpawnSpotOf(Peer)!.Value;
            Check(PlayerOn(_host).GlobalPosition.DistanceTo(spawn) < 0.5f, $"at its spawn spot ({PlayerOn(_host).GlobalPosition} vs {spawn})");
            return true;
        });
        Stage("the client picks its things back up", frame =>
        {
            if (frame == 0)
                _host.Main.PlayerSync.HostTeleport(Peer, _diedAt);
            if (frame == 20)
                Request(SurvivalActions.PickUp, new PickUpArgs(_client.Main.Survival.Drops.Keys.Single()));
            if (_results.Count == 0 || Count(Carrot) != 3)
                return false;
            Check(_results[0].Ok && Count(Blaster) == 1, $"everything is back ({_results[0]}; {Items()})");
            return _client.Main.Survival.Drops.Count == 0 && _client.Main.Survival.DropNodes.Count == 0 && _host.Main.Survival.Drops.Count == 0;
        });
        Stage("the host puts a bed beside the client", frame =>
        {
            if (frame == 0)
            {
                var at = Feet(_client) + new Vector3(2, 0.3f, 0);
                _host.Main.Actions.Request(CreationActions.SpawnPart, new SpawnPartArgs("vanilla:respawn_bed", [at.X, at.Y, at.Z]), r => _bed = r.Value!.Value.GetInt32());
            }
            return frame > 30 && _bed != 0 && _client.Main.Creations.Find(_bed) is not null;
        });
        Stage("the client sleeps in it", frame =>
        {
            if (frame == 0)
                Request(SurvivalActions.SetSpawn, new PartUseArgs(_bed, _host.Main.Creations.Find(_bed)!.Parts.First().Id));
            if (_results.Count == 0 || _client.Main.Survival.Own is not { HasBed: true })
                return false;
            Check(_results[0].Ok, $"the bed is set ({_results[0]})");
            return true;
        });
        Stage("the client dies again", frame =>
        {
            if (frame == 0)
                HostSurvival.Damage(Peer, 500, "test");
            return _client.Main.Survival.Own is { Dead: true };
        });
        Stage("the client respawns on the bed", frame =>
        {
            RespawnWhenReady(frame);
            if (_client.Main.Survival.Own is not { Dead: false })
                return false;
            var node = _host.Main.Creations.NodeOf(_bed)!;
            var part = node.Creation.Parts.First();
            var bed = node.Bodies[0].GlobalTransform * PartGeometry.CentreMetres(node.Creation.DefinitionOf(part.Id), part.Cell, part.Rotation).ToGodot();
            var feet = PlayerOn(_host).GlobalPosition;
            Check(new Vector2(feet.X - bed.X, feet.Z - bed.Z).Length() < 0.1f && feet.Y > bed.Y, $"respawned on top of the bed ({feet} vs its centre {bed})");
            return true;
        });
    }

    private void Stage(string name, Func<int, bool> tick) => Add(name, frame =>
    {
        if (frame == 0)
            _results.Clear();
        return tick(frame);
    });

    // Asks to respawn every half second once the client's own countdown is over (the host's clock decides).
    private void RespawnWhenReady(int frame)
    {
        if (frame % 30 == 0 && _client.Main.Survival.Own is { Dead: true } && _client.Main.Survival.OwnRespawnIn <= 0)
            Request(SurvivalActions.Respawn, new NoArgs());
    }

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    private void OnLanded(ProjectileLanding landing)
    {
        _landed[landing.Id] = HostSurvival.World.Now;
    }

    private int Count(string item) => _client.Main.Inventory.Own?.Items.Where(s => s.Item == item).Sum(s => s.Count) ?? 0;

    private int SlotOf(string item) => _client.Main.Inventory.Own!.Items.First(s => s.Item == item).Slot;

    private string Items() => string.Join(", ", _client.Main.Inventory.Own?.Items.Select(s => $"{s.Item} x{s.Count}") ?? []);

    private Player.PlayerController PlayerOn(Instance who) => who.Main.PlayerSync.Players[Peer];

    private static Vector3 Feet(Instance who) => who.Main.LocalPlayer!.GlobalPosition;

    private static Vector3 Eye(Instance who) => who.Main.LocalPlayer!.Head.GlobalPosition;

    private static void LookAt(Instance who, Vector3 target)
    {
        var d = (target - Eye(who)).Normalized();
        who.Main.LocalPlayer!.CurrentInput = PlayerInput.None with { Yaw = Mathf.Atan2(-d.X, -d.Z), Pitch = Mathf.Asin(d.Y) };
    }

    private static T Value<T>(ActionResult result) => result.Value!.Value.Deserialize<T>(ActionJson.Options)!;
}
