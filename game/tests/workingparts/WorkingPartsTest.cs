using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Creations;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Parts map batch 6's working parts, host plus a client in Survival on flat ground. The host builds a floor with a Cash
/// Register, an Alarm Clock with a button to stop it, and a Sticky Wheel with a switch and a Chemical Container linked
/// in, and stands an Explosive Canister on its own beside the client. The client opens and shuts the till (E), sets
/// the clock a few minutes ahead and hears it ring then stops it with the button, switches the wheel's gel on, and
/// knocks the canister with a Sledgehammer: its fuse burns and three seconds later it blows up, which both machines show.
/// Each step is checked on both machines.
/// </summary>
public partial class WorkingPartsTest : StagedTest
{
    private const string Floor = "vanilla:metal_block";
    private const string Chemical = "vanilla:chemical";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _floor, _till, _clock, _stop, _wheel, _trigger, _tank;
    private int _canister;
    private float _health;
    private string _rings = "";

    protected override string Tag => "workingparts";

    protected override int StageTimeoutFrames => 900;

    private Creation HostFloor => _host.Main.Creations.Find(_floor)!;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins a Survival world", _ =>
            _client.Main.Inventory.Own is not null && _client.Main.LocalPlayer is { } player && player.IsOnFloor()
            && _host.Main.Inventory.Items is not null && _client.Main.Survival.Own is not null);
        Add("the client sees the floor's parts and the canister", frame =>
        {
            if (frame == 0)
                Build();
            if (Clip(_client, _floor, _till) is null || Clip(_client, _floor, _clock) is null || _client.Main.Creations.Find(_canister) is null)
                return false;
            Check(Clip(_client, _floor, _till) == "close", $"the till is shut ({Clip(_client, _floor, _till)})");
            Check(Clip(_client, _floor, _clock) == "armed", $"the clock is armed ({Clip(_client, _floor, _clock)})");
            Check(Clip(_client, _floor, _wheel) == "off", $"the wheel's gel is off ({Clip(_client, _floor, _wheel)})");
            return true;
        });

        Stage("E opens the Cash Register: the drawer opens on the client and its output is on", frame =>
        {
            if (frame == 0)
                Request(LogicActions.ActivatePart, new LogicPartArgs(_floor, _till));
            if (Clip(_client, _floor, _till) != "open" || Clip(_host, _floor, _till) != "open")
                return false;
            Check(_results.All(r => r.Ok), $"the till opens ({string.Join("; ", _results)})");
            Check(_host.Main.Scripts.Runtime.Signals.Output(_floor, _till) == 1f, "its output is on while open");
            return true;
        });
        Stage("E again shuts it", frame =>
        {
            if (frame == 0)
                Request(LogicActions.ActivatePart, new LogicPartArgs(_floor, _till));
            if (Clip(_client, _floor, _till) != "close")
                return false;
            Check(_host.Main.Scripts.Runtime.Signals.Output(_floor, _till) == 0f, "its output is off once shut");
            return true;
        });

        Stage("set a few minutes ahead, the Alarm Clock rings on both machines with its output on", frame =>
        {
            if (frame == 0)
            {
                // world.time_of_day: a 1440 s day is a game minute a second, so the next five-minute mark is at most 6 s off.
                var minutes = (int)Math.Ceiling((_host.Main.Scripts.Runtime.TimeOfDay * 60 + 1) / 5) * 5;
                _rings = $"{minutes / 60 % 24:00}:{minutes % 60:00}";
                Request(LogicActions.ConfigurePart, new ConfigurePartArgs(_floor, _clock, new Dictionary<string, JsonElement?>
                {
                    ["hour"] = JsonSerializer.SerializeToElement(minutes / 60 % 24),
                    ["minute"] = JsonSerializer.SerializeToElement(minutes % 60),
                }));
            }
            if (Clip(_client, _floor, _clock) != "ring_loop" || Clip(_host, _floor, _clock) != "ring_loop")
                return false;
            Check(_results.All(r => r.Ok), $"the client sets the time ({string.Join("; ", _results)})");
            Check(_host.Main.Scripts.Runtime.Signals.Output(_floor, _clock) == 1f, "its output is on while it rings");
            var now = _client.Main.Display.Readouts.Text(_floor, _clock, "now");
            var rings = _client.Main.Display.Readouts.Text(_floor, _clock, "rings");
            Check(rings == _rings, $"the client's menu shows when it rings ({rings}, set for {_rings})");
            Check(now is { Length: 5 } && string.CompareOrdinal(now, _rings) >= 0, $"and the time now, just past it ({now})");
            return true;
        });
        Stage("the button linked to it stops the ringing", frame =>
        {
            if (frame == 0)
                Request(LogicActions.ActivatePart, new LogicPartArgs(_floor, _stop));
            if (Clip(_client, _floor, _clock) != "armed")
                return false;
            Check(_host.Main.Scripts.Runtime.Signals.Output(_floor, _clock) == 0f, "its output is off again");
            return true;
        });

        Stage("its switch turns the Sticky Wheel's gel on: it draws a chemical and glistens on the client", frame =>
        {
            if (frame == 0)
                Request(LogicActions.ActivatePart, new LogicPartArgs(_floor, _trigger));
            if (Clip(_client, _floor, _wheel) != "on" || HostFloor.InventoryOf(_tank)!.Count(Chemical) != 4)
                return false;
            Check(Clip(_host, _floor, _wheel) == "on", "the host shows it on too");
            Check(_client.Main.Display.Readouts.Number(_floor, _wheel, "chemicals") == 4f, "the client's menu counts the chemicals left");
            return true;
        });
        Stage("switched off it lets go", frame =>
        {
            if (frame == 0)
                Request(LogicActions.ActivatePart, new LogicPartArgs(_floor, _trigger));
            return Clip(_client, _floor, _wheel) == "off";
        });

        Stage("a Sledgehammer knock lights the canister's fuse on both machines", frame =>
        {
            if (frame == 0)
            {
                _health = _client.Main.Survival.Own!.Health;
                var at = _host.Main.Creations.Find(_canister)!.BodyPose(0).Position;
                Request(ToolActions.Sledge, new SledgeArgs(Creation: _canister, Part: 1, At: new Vec3(at.X, at.Y + 0.25f, at.Z)));
            }
            if (Clip(_client, _canister, 1) != "fuse_loop" || Clip(_host, _canister, 1) != "fuse_loop")
                return false;
            Check(_results.All(r => r.Ok), $"the knock lands ({string.Join("; ", _results)})");
            Check(_host.Main.Streamer.BlastsShown == 0 && _client.Main.Streamer.BlastsShown == 0, "it has not gone off yet");
            return true;
        });
        Stage("three seconds later it blows up: both machines show the blast and the canister is gone", frame =>
        {
            if (_client.Main.Streamer.BlastsShown == 0 || _host.Main.Creations.Find(_canister) is not null || _client.Main.Creations.Find(_canister) is not null)
                return false;
            Check(_host.Main.Streamer.BlastsShown == 1, $"the host shows one blast ({_host.Main.Streamer.BlastsShown})");
            Check(_client.Main.Streamer.BlastsShown == 1, $"the client shows one blast ({_client.Main.Streamer.BlastsShown})");
            Check(frame >= 120, $"after the fuse ({frame} frames)");
            Check(_client.Main.Survival.Own!.Health == _health, $"the client, out of its reach, is unhurt ({_client.Main.Survival.Own.Health})");
            Check(HostFloor.Parts.All(p => HostFloor.PartDamage(p.Id) == 0), "the floor, out of its reach, is untouched");
            return true;
        });
    }

    private void Stage(string name, Func<int, bool> tick) => Add(name, frame =>
    {
        if (frame == 0)
            _results.Clear();
        return tick(frame);
    });

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);

    // The clip a part's model shows on a machine (its display's clip readout), or null before it has one.
    private static string? Clip(Instance instance, int creation, int part) =>
        instance.Main.Creations.NodeOf(creation)?.Displays.FirstOrDefault(d => d.Part == part)?.Clip;

    // A 12 x 8 floor 3.5 m in front of the client with the till, the clock and its button, the wheel with its switch and
    // tank; and the canister on its own 2.3 m to the client's side, over 1.75 m (its blast) from both.
    private void Build()
    {
        var catalog = _host.Main.Build.Catalog;
        var creation = new Creation(_host.Main.Build.NextCreationId(), catalog);
        for (var x = 0; x < 12; x++)
        {
            for (var z = 0; z < 8; z++)
                Place(creation, Floor, new GridCell(x, 0, z));
        }
        _till = Place(creation, "vanilla:cash_register", new GridCell(0, 1, 5));
        _clock = Place(creation, "vanilla:alarm_clock", new GridCell(5, 1, 7));
        _stop = Place(creation, "vanilla:button", new GridCell(8, 1, 7));
        _wheel = Place(creation, "vanilla:sticky_wheel", new GridCell(0, 1, 0));
        _trigger = Place(creation, "vanilla:switch", new GridCell(4, 1, 1));
        _tank = Place(creation, "vanilla:chemical_container", new GridCell(6, 1, 0));
        Link(creation, _stop, _clock);
        Link(creation, _trigger, _wheel);
        Link(creation, _tank, _wheel);
        creation.InventoryOf(_tank)!.Add(Chemical, 5);
        var feet = _host.Main.PlayerSync.Players[_client.Peer].GlobalPosition;
        creation.SetAllBodyPoses(new Pose((feet + new Vector3(-1.5f, 0.25f, -3.5f)).ToNumerics(), System.Numerics.Quaternion.Identity));
        _host.Main.Build.Added(creation);
        _floor = creation.Id;

        var canister = new Creation(_host.Main.Build.NextCreationId(), catalog);
        Place(canister, "vanilla:explosive_canister_small", GridCell.Zero);
        canister.SetAllBodyPoses(new Pose((feet + new Vector3(2.1f, 0.02f, 0.8f)).ToNumerics(), System.Numerics.Quaternion.Identity));
        _host.Main.Build.Added(canister);
        _canister = canister.Id;
    }

    private int Place(Creation creation, string part, GridCell cell)
    {
        if (!creation.TryPlacePart(part, cell, default, out var placed, out var error))
            Check(false, $"{part} fits at {cell} ({error})");
        return placed?.Id ?? 0;
    }

    private void Link(Creation creation, int from, int to)
    {
        if (!creation.TryLink(from, to, out var error))
            Check(false, $"{from} links to {to} ({error})");
    }
}
