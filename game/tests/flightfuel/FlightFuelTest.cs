using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Logic;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Mechanica.Game.Creations;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The Hover Jet and Rotor burn batteries (parts audit batch A1), host plus a client in Survival. VanillaAeroTest flies
/// the same parts in Creative, where they burn nothing; here the host builds a hover craft (a Hover Jet under a Battery
/// Container, a switch beside it) by the client, who links the switch and the container to the jet with the Connect Tool
/// and switches it on: with no batteries it stays on the ground. The host puts one battery in the container: the jet takes
/// it and lifts the craft, and the client sees it fly with the fan's run clip playing. The client keeps it hovering a
/// metre or two up with the switch until the battery runs out (60 s of full power, run at four times speed; it flies level
/// all that time); then it comes down and stays down with the switch on, and the client sees the fan stop. Then, 15 m on,
/// the same for a rotor craft (four rotors, paired clockwise and counter-clockwise, along a beam under a Battery Container,
/// a battery each since every part burns its own): the client sees the blades pitched for their spin and the models
/// turning, and it comes down when the batteries (two minutes each) run out.
/// </summary>
public partial class FlightFuelTest : StagedTest
{
    private const string Frame = "vanilla:cardboard_block";
    private const string Battery = "vanilla:battery";
    private const string Pack = "vanilla:battery_pack";
    private const string Switch = "vanilla:switch";
    private const float Speed = 4f;

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;

    // The craft under test: its id, its switch, its Battery Container and what burns the batteries.
    private int _craft, _switch, _battery;
    private readonly List<int> _lifts = [];
    private float _ground;
    private bool _on, _pending;
    private int _down;
    // Game seconds (physics deltas grow with the time scale) since the test began, and when the battery was taken.
    private double _clock, _taken, _airborne;
    private float _highest, _clientHighest;
    private readonly HashSet<string> _clips = [];
    private float _turned;

    protected override string Tag => "flightfuel";

    // The longest stage keeps a craft up on a battery: two minutes of the rotors' full command.
    protected override int StageTimeoutFrames => 9600;

    private Creation Host => _host.Main.Creations.Find(_craft)!;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the client joins a Survival world", _ =>
            _client.Main.Inventory.Own is not null && _client.Main.LocalPlayer is { } player && player.IsOnFloor() && _host.Main.Inventory.Items is not null);
        Fly("hover craft", BuildHoverCraft, "run_loop", "off", 60f);
        Fly("rotor craft", BuildRotorCraft, "ccw", null, 120f);
    }

    public override void _ExitTree() => Engine.TimeScale = 1;

    public override void _PhysicsProcess(double delta)
    {
        _clock += delta;
        base._PhysicsProcess(delta);
    }

    // The stages for one craft: built empty, it stays down; with a battery it flies, kept low with the switch, until the
    // battery runs out and it comes down. `running` and `stopped` are the clips the client sees on the first lift while
    // it pushes and once it is out (null: the readout does not change).
    private void Fly(string name, Action build, string running, string? stopped, float seconds)
    {
        Add($"the host builds a {name} with an empty Battery Container by the client", frame =>
        {
            // A craft before this one stays where it came down: the client walks 15 m on first.
            if (frame == 0 && _craft != 0)
            {
                var feet = _host.Main.PlayerSync.Players[_client.Peer].GlobalPosition;
                _host.Main.PlayerSync.HostTeleport(_client.Peer, feet + new Vector3(15f, 0.1f, 0f));
            }
            if (frame == 60)
            {
                _lifts.Clear();
                build();
                _host.Main.Inventory.Items!.PlayerInventory(_client.Peer).Add("vanilla:connect_tool", 1);
            }
            if (frame < 120 || _client.Main.Creations.NodeOf(_craft) is null)
                return false;
            _ground = Body(_host).GlobalPosition.Y;
            return true;
        });
        Add($"the client links the switch and the container to the {name} and switches it on: with no batteries it stays down", frame =>
        {
            if (frame == 0)
            {
                foreach (var lift in _lifts)
                {
                    Request(LogicActions.ConnectParts, new LinkPartsArgs(_craft, _switch, lift));
                    Request(LogicActions.ConnectParts, new LinkPartsArgs(_craft, _battery, lift));
                }
                Request(LogicActions.ActivatePart, new LogicPartArgs(_craft, _switch));
            }
            if (frame < 150 || _results.Count < 2 * _lifts.Count + 1)
                return false;
            Check(_results.All(r => r.Ok), $"linking and switching on succeed ({string.Join("; ", _results)})");
            _results.Clear();
            _on = true;
            Check(_host.Main.Scripts.Runtime.Signals.Output(_craft, _switch) == 1f, "the switch is on");
            Check(Height(_host) < 0.1f, $"it stays on the ground ({Height(_host):F2} m up)");
            Check(!Pushing, "nothing pushes");
            Check(_client.Main.Display.Readouts.Number(_craft, _lifts[0], "fuel_items") == 0f,
                $"the client's menu shows no batteries ({_client.Main.Display.Readouts.Number(_craft, _lifts[0], "fuel_items")})");
            return true;
        });
        Add($"with a battery each in the container the {name} takes them and lifts off, and the client sees it fly", frame =>
        {
            if (frame == 0)
            {
                // A battery for each: every part burns its own.
                Host.InventoryOf(_battery)!.Add(Pack, _lifts.Count);
                _clips.Clear();
                _highest = _clientHighest = 0f;
                _turned = 0f;
            }
            Hover();
            Watch();
            if (Host.InventoryOf(_battery)!.Count(Pack) == 0 && _taken == 0)
                _taken = _clock;
            Trace(frame);
            if (_clientHighest < 1f)
                return false;
            Check(Host.InventoryOf(_battery)!.Count(Pack) == 0, "the batteries left the container");
            Check(_highest > 1f, $"the host lifted it ({_highest:F2} m)");
            Check(_clips.Contains(running), $"the client plays its {running} clip ({string.Join(", ", _clips)})");
            Check(Body(_host).GlobalBasis.Y.Dot(Vector3.Up) > 0.9f, $"it flies level (up {Body(_host).GlobalBasis.Y})");
            if (_client.Main.Creations.NodeOf(_craft)!.AnimatedModels.Any(m => m.Part == _lifts[0]))
                Check(_turned > 0.3f, $"the client's rotor model turns ({_turned:F2} rad)");
            return true;
        });
        Add($"the client keeps the {name} up with its switch until the batteries run out, and it comes down", frame =>
        {
            if (frame == 0)
                Engine.TimeScale = Speed;
            Hover();
            Watch();
            Trace(frame);
            if (Height(_host) > 0.5f && Body(_host).GlobalBasis.Y.Dot(Vector3.Up) > 0.9f)
                _airborne = _clock;
            // Out of charge, at rest on the ground with the switch on and nothing pushing, for a second. Unpowered, nothing
            // keeps it level, so it may land on its side.
            var landed = _on && !_pending && Height(_host) < 0.6f && Body(_host).LinearVelocity.Length() < 0.05f && !Pushing
                && Host.InventoryOf(_battery)!.Count(Pack) == 0 && _lifts.All(l => _host.Main.Display.Readouts.Number(_craft, l, "fuel") == 0f);
            _down = landed ? _down + 1 : 0;
            if (_down < 60)
                return false;
            Engine.TimeScale = 1;
            var flown = (float)(_airborne - _taken);
            GD.Print($"[flightfuel] RESULT the {name} flew {flown:F1} s on a battery each ({seconds} s of full power), up to {_highest:F2} m, "
                + $"landed with up {Body(_host).GlobalBasis.Y}");
            // The switch is off now and then, which burns nothing, so it stays up longer than the charge's full-power time.
            Check(flown >= seconds, $"it flew level at least as long as the battery's {seconds} s of full power ({flown:F1} s)");
            Check(Host.Parts.Count > 0 && Host.InventoryOf(_battery)!.Count(Pack) == 0, "its container is empty");
            Check(_client.Main.Display.Readouts.Number(_craft, _lifts[0], "fuel") == 0f, "the client's menu shows the charge gone");
            if (stopped is not null)
                Check(Display(_client, _lifts[0])?.Clip == stopped, $"the client plays its {stopped} clip ({Display(_client, _lifts[0])?.Clip})");
            Check(Body(_client).GlobalPosition.DistanceTo(Body(_host).GlobalPosition) < 0.3f, "the client has it on the ground there too");
            _taken = 0;
            _results.Clear();
            return true;
        });
        Add($"the client switches the {name} off", frame =>
        {
            if (frame == 0)
            {
                Request(LogicActions.ActivatePart, new LogicPartArgs(_craft, _switch));
                return false;
            }
            if (_results.Count < 1)
                return false;
            Check(_results.All(r => r.Ok), $"switching off succeeds ({string.Join("; ", _results)})");
            _results.Clear();
            _on = false;
            return true;
        });
    }

    // Every 300 frames, for a failure: how high, each lift's power and charge, the switch.
    private void Trace(int frame)
    {
        if (frame % 300 == 299)
            GD.Print($"[flightfuel] {frame}: {Height(_host):F2} m up, up {Body(_host).GlobalBasis.Y}, v {Body(_host).LinearVelocity}, on {_on}, "
                + $"power {string.Join(" ", _lifts.Select(l => _host.Main.Display.Readouts.Number(_craft, l, "power")))}, "
                + $"fuel {string.Join(" ", _lifts.Select(l => _host.Main.Display.Readouts.Number(_craft, l, "fuel")))}");
    }

    // With the switch, as a player would: off once it is 2 m up, on again as soon as it sinks (a rotor takes a second
    // or so to spool up and down, and a free fall from 2 m tips the beam over) or below 1 m.
    private void Hover()
    {
        // The client walks after it to keep its switch in reach.
        var craft = Body(_host).GlobalPosition;
        var player = _host.Main.PlayerSync.Players[_client.Peer].GlobalPosition;
        if (new Vector2(craft.X - player.X, craft.Z - player.Z).Length() > 5f)
        {
            // Behind it, out of its way.
            var drift = new Vector3(Body(_host).LinearVelocity.X, 0f, Body(_host).LinearVelocity.Z);
            var back = drift.Length() > 0.1f ? -drift.Normalized() : new Vector3(0f, 0f, 1f);
            _host.Main.PlayerSync.HostTeleport(_client.Peer, new Vector3(craft.X, player.Y + 0.1f, craft.Z) + back * 3.5f);
        }
        if (_pending)
            return;
        var height = Height(_host);
        var sinking = Body(_host).LinearVelocity.Y < -0.3f;
        if (_on ? height > 2f && !sinking : height < 1f || sinking)
        {
            _pending = true;
            _client.Main.Actions.Request(LogicActions.ActivatePart, new LogicPartArgs(_craft, _switch), result =>
            {
                _pending = false;
                if (result.Ok)
                    _on = !_on;
                else
                    Check(false, $"the client reaches the switch ({result})");
            });
        }
    }

    // What the client and host show while it flies: how high, the clips, how far the rotors' models turn.
    private void Watch()
    {
        _highest = MathF.Max(_highest, Height(_host));
        if (_client.Main.Creations.NodeOf(_craft) is { } seen)
        {
            _clientHighest = MathF.Max(_clientHighest, Body(_client).GlobalPosition.Y - _ground);
            if (Display(_client, _lifts[0])?.Clip is { } clip)
                _clips.Add(clip);
            if (seen.AnimatedModels.FirstOrDefault(m => m.Part == _lifts[0]).Model is { } model)
                _turned = MathF.Max(_turned, model.Transform.Basis.X.AngleTo(Vector3.Right));
        }
    }

    // A Hover Jet under four blocks, a Battery Container standing on them and a switch on their side (20.5 kg empty).
    private void BuildHoverCraft()
    {
        var creation = NewCraft();
        foreach (var (x, z) in new[] { (1, 1), (2, 1), (1, 2), (2, 2) })
            Place(creation, Frame, new GridCell(x, 0, z));
        _battery = Place(creation, Battery, new GridCell(0, 1, 0));
        _lifts.Add(Place(creation, "vanilla:hover_jet", new GridCell(1, -1, 2), Facing(new GridCell(0, -1, 0))));
        // On the far side from the container's overhang, so the centre of mass is over the jet.
        _switch = Place(creation, Switch, new GridCell(1, 0, 3), Facing(new GridCell(0, 0, 1)));
        Spawn(creation, new Vector3(-0.5f, 0.3f, -2.5f));
    }

    // Behind the client: a beam of fourteen blocks with a Battery Container in the middle and four rotors along it (clockwise at the ends,
    // counter-clockwise inside, so their twists cancel), a switch at each end for balance (54.5 kg empty).
    private void BuildRotorCraft()
    {
        var creation = NewCraft();
        for (var x = -5; x <= 8; x++)
            Place(creation, Frame, new GridCell(x, 0, 1));
        _battery = Place(creation, Battery, new GridCell(0, 1, 0));
        foreach (var hub in new[] { -5, -2, 5, 8 })
        {
            var rotor = Place(creation, "vanilla:rotor", new GridCell(hub - 1, 1, 0));
            if (hub is -2 or 5)
                creation.SetPartSettings(rotor, new Dictionary<string, System.Text.Json.JsonElement>
                {
                    ["spin"] = System.Text.Json.JsonSerializer.SerializeToElement("counterclockwise"),
                });
            _lifts.Add(rotor);
        }
        // The first one watched (at hub -2) is counter-clockwise.
        (_lifts[0], _lifts[1]) = (_lifts[1], _lifts[0]);
        _switch = Place(creation, Switch, new GridCell(9, 0, 1), Facing(new GridCell(1, 0, 0)));
        Place(creation, Switch, new GridCell(-6, 0, 1), Facing(new GridCell(-1, 0, 0)));
        Spawn(creation, new Vector3(-1f, 0.3f, 2f));
    }

    private Creation NewCraft() => new(_host.Main.Build.NextCreationId(), _host.Main.Build.Catalog);

    private void Spawn(Creation creation, Vector3 offset)
    {
        var feet = _host.Main.PlayerSync.Players[_client.Peer].GlobalPosition;
        creation.SetAllBodyPoses(new Pose((feet + offset).ToNumerics(), System.Numerics.Quaternion.Identity));
        GD.Print($"[flightfuel] craft: {creation.Parts.Count} parts, {creation.Bodies.Count} bodies, {creation.Bodies.Sum(b => b.Mass):F1} kg");
        _host.Main.Build.Added(creation);
        _craft = creation.Id;
    }

    private int Place(Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        if (!creation.TryPlacePart(part, cell, rotation, out var placed, out var error))
            Check(false, $"{part} at {cell} fits ({error})");
        return placed?.Id ?? 0;
    }

    // Local +Y turned to point along outward.
    private static GridRotation Facing(GridCell outward) =>
        outward.Y != 0 ? GridRotation.FromAxes(new GridCell(1, 0, 0), outward) : GridRotation.FromAxes(new GridCell(0, -outward.X - outward.Z, 0), outward);

    // Whether any lift part shows power on the host (a rotor spooling down still turns).
    private bool Pushing => _lifts.Any(l => _host.Main.Display.Readouts.Number(_craft, l, "power") > 0.01f);

    private float Height(Instance instance) => Body(instance).GlobalPosition.Y - _ground;

    private CreationBody Body(Instance instance) => instance.Main.Creations.NodeOf(_craft)!.Bodies[0];

    private PartDisplayVisual? Display(Instance instance, int part) =>
        instance.Main.Creations.NodeOf(_craft)?.Displays.FirstOrDefault(d => d.Part == part);

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);
}
