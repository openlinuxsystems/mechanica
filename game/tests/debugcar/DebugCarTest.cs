using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Logic;
using Mechanica.Core.Player;
using Mechanica.Core.Survival;
using Mechanica.Core.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The debug scrap car, host plus a client: a new world with <see cref="Main.DebugCar"/> on gets the car by the spawn,
/// wired and fuelled, and the client sees it; the client sits in it, drives it straight ahead on W on open ground east of
/// the spawn (with nobody steering it keeps its line and heading), backs it up on S, burning the Gas Container's gasoline,
/// and up a 15° hill (gravity tilted). Parked, it stays put though its steering holds straight. Rolled over (it comes to rest on its side or roof),
/// the car keeps its driver in the seat, tipped over with it, on both peers. The client, which does not host, never spawns one of its own.
/// </summary>
public partial class DebugCarTest : StagedTest
{
    private const string Gasoline = "vanilla:gasoline";

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _car;
    private Vector3 _start;
    private Vector3 _heading;
    private float _drift;

    protected override string Tag => "debugcar";

    protected override int StageTimeoutFrames => 900;

    private Core.Creations.Creation HostCar => _host.Main.Creations.Find(_car)!;

    private Creations.CreationBody Chassis => _host.Main.Creations.NodeOf(_car)!.Bodies[0];

    private int Part(string id) => HostCar.Parts.First(p => p.Part == id).Id;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Survival, WorldGen = WorldGenData.Flat, Setup = main => main.DebugCar = true };
        AddChild(_harness);
        _host = _harness.StartHost();
        _client = _harness.StartClient("Client");

        Add("the host's new world has the car and the client sees it", _ =>
        {
            if (_client.Main.LocalPlayer is not { } player || !player.IsOnFloor() || _host.Main.DebugCarId is not { } id
                || _client.Main.Creations.Find(id) is not { } seen || seen.Parts.Count != _host.Main.Creations.Find(id)!.Parts.Count)
                return false;
            _car = id;
            Check(_client.Main.DebugCarId is null && _host.Main.Creations.All.Count(c => c.Parts.Any(p => p.Part == "vanilla:scrap_drivers_seat")) == 1, "only the host spawns a car");
            var settings = Creations.DebugCarSpawner.LoadSettings();
            Check(HostCar.InventoryOf(Part("vanilla:gas_container"))!.Count(Gasoline) == settings.Gasoline, $"its Gas Container holds {settings.Gasoline} gasoline");
            var spawn = _host.Main.GetNode<Node3D>("SpawnPoint").GlobalPosition;
            var offset = Chassis.GlobalPosition - spawn;
            Check(new Vector2(offset.X - settings.Offset[0], offset.Z - settings.Offset[2]).Length() < 2f, $"it stands at its offset from the spawn ({offset})");
            return true;
        });
        Wait("the car settles", 90);
        Add("with nobody in the seat the engine stays off on both peers", frame =>
        {
            if (frame < 120)
                return false;
            var engine = Part("vanilla:scrap_gas_engine");
            foreach (var (instance, who) in new[] { (_host, "host"), (_client, "client") })
                Check(instance.Main.Display.Readouts.Number(_car, engine, "running") is null or 0f, $"the {who} sees the engine off");
            Check(HostCar.InventoryOf(Part("vanilla:gas_container"))!.Count(Gasoline) == Creations.DebugCarSpawner.LoadSettings().Gasoline,
                "it burns no gasoline");
            return true;
        });
        // Its steering holds straight. A hold re-set to a tiny speed every step dithered the joint and crept a parked
        // car along at several cm/s; a hold on its target stops its motor.
        Add("parked on flat ground, the car stays put", frame =>
        {
            if (frame == 0)
                _start = Chassis.GlobalPosition;
            if (frame < 600)
                return false;
            var moved = (Chassis.GlobalPosition - _start).Length();
            Check(moved < 0.15f, $"it stays put (moves {moved:F3} m in 10 s; the dithering hold crept it about 0.5 m)");
            return true;
        });
        Add("the client sits in the driver's seat", frame =>
        {
            if (frame == 0)
            {
                _host.Main.PlayerSync.HostTeleport(_client.Peer, Chassis.GlobalTransform * new Vector3(-1.2f, 0f, 1.5f), 0f);
                return false;
            }
            if (frame == 20)
                _client.Main.Actions.Request(LogicActions.EnterSeat, new LogicPartArgs(_car, Part("vanilla:scrap_drivers_seat")), _results.Add);
            if (_results.Count == 0 || !_client.Main.LocalPlayer!.Seated)
                return false;
            Check(_results[0].Ok, $"sitting down succeeds ({_results[0]})");
            return true;
        });
        Add("sitting down starts the engine", _ =>
        {
            if (_client.Main.Display.Readouts.Number(_car, Part("vanilla:scrap_gas_engine"), "running") != 1f)
                return false;
            _start = Chassis.GlobalPosition;
            return true;
        });
        Add("the car is moved onto open ground", frame =>
        {
            // East of the spawn, clear of the Main scene's test steps in front of it.
            if (frame == 0)
            {
                var pose = Chassis.GlobalTransform with { Origin = Chassis.GlobalPosition + new Vector3(20f, 0.05f, 0f) };
                HostCar.SetAllBodyPoses(pose.ToPose());
                _host.Main.Creations.Moved(_car);
                return false;
            }
            if (frame < 90)
                return false;
            _start = Chassis.GlobalPosition;
            _heading = -Chassis.GlobalBasis.Z;
            _drift = 0f;
            return true;
        });
        Add("W drives the car straight ahead", frame =>
        {
            // Nobody steers: the car keeps its heading and line (it used to wander 2-4° as its wheel bearings bent).
            _client.Main.LocalPlayer!.CurrentInput = new PlayerInput(0, 1, 0, 0, false, false);
            _drift = MathF.Max(_drift, MathF.Abs(Mathf.RadToDeg(_heading.SignedAngleTo(-Chassis.GlobalBasis.Z, Vector3.Up))));
            if (frame < 300)
                return false;
            _client.Main.LocalPlayer.CurrentInput = new PlayerInput(0, 0, 0, 0, false, false);
            var moved = Chassis.GlobalPosition - _start;
            var ahead = moved.Dot(_heading);
            var aside = moved.Dot(_heading.Cross(Vector3.Up));
            GD.Print($"[debugcar] drove {ahead:0.00} m ahead, {aside:0.00} m aside, turned at most {_drift:0.00}°");
            Check(ahead > 15f, $"the car drives forward ({ahead:0.0} m)");
            Check(MathF.Abs(aside) < 0.4f, $"it keeps its line ({aside:0.00} m aside after {ahead:0.0} m)");
            Check(_drift < 1.5f, $"it keeps its heading (turned at most {_drift:0.00}°)");
            return true;
        });
        Add("the car rolls to a stop", frame =>
        {
            if (frame < 30 || Chassis.LinearVelocity.Length() > 0.3f)
                return false;
            _start = Chassis.GlobalPosition;
            return true;
        });
        Add("S backs the car south and burns gasoline", frame =>
        {
            _client.Main.LocalPlayer!.CurrentInput = new PlayerInput(0, -1, 0, 0, false, false);
            if (Chassis.GlobalBasis.Y.Dot(Vector3.Up) < 0.9f)
                return !Check(false, $"the car stays upright (tipped at frame {frame})");
            if (frame < 240)
                return false;
            var moved = Chassis.GlobalPosition - _start;
            Check(moved.Z > 2f && MathF.Abs(moved.X) < 0.25f * moved.Z, $"the car backed up along +Z ({moved})");
            var settings = Creations.DebugCarSpawner.LoadSettings();
            Check(HostCar.InventoryOf(Part("vanilla:gas_container"))!.Count(Gasoline) < settings.Gasoline, "the engine burns the Gas Container's gasoline");
            _client.Main.LocalPlayer.CurrentInput = new PlayerInput(0, 0, 0, 0, false, false);
            return true;
        });
        Add("S backs the car up a 15° hill", frame =>
        {
            // Gravity tilted toward -Z puts the car on a 15° slope rising to the south, the way it backs up.
            var space = Chassis.GetWorld3D().Space;
            if (frame == 0)
            {
                var slope = Mathf.DegToRad(15f);
                PhysicsServer3D.AreaSetParam(space, PhysicsServer3D.AreaParameter.GravityVector, new Vector3(0f, -Mathf.Cos(slope), -Mathf.Sin(slope)));
                _start = Chassis.GlobalPosition;
            }
            _client.Main.LocalPlayer!.CurrentInput = new PlayerInput(0, -1, 0, 0, false, false);
            if (frame < 240)
                return false;
            PhysicsServer3D.AreaSetParam(space, PhysicsServer3D.AreaParameter.GravityVector, Vector3.Down);
            _client.Main.LocalPlayer.CurrentInput = new PlayerInput(0, 0, 0, 0, false, false);
            var moved = Chassis.GlobalPosition - _start;
            Check(moved.Z > 8f, $"the Scrap Gas Engine pulls the car up the hill ({moved}, {Chassis.LinearVelocity.Z:0.0} m/s at the end)");
            return true;
        });
        Add("the car rolls onto its roof", frame =>
        {
            // Twisted about its length until it lies on its roof.
            if (Chassis.GlobalBasis.Y.Dot(Vector3.Up) > -0.7f)
            {
                Chassis.Sleeping = false;
                Chassis.ApplyTorque(Chassis.GlobalBasis.Z * Chassis.Mass * 40f);
                return frame > 600 && !Check(false, $"the car rolls over (still {Chassis.GlobalBasis.Y.Dot(Vector3.Up):0.00} up)");
            }
            return true;
        });
        Add("the upturned car settles", frame =>
        {
            // The client watches its own driver from outside, as the chase camera does.
            _client.Main.LocalPlayer!.ThirdPerson = true;
            return frame >= 90;
        });
        Add("the driver stays in the seat, tipped over with the car, on both peers", _ =>
        {
            var seat = Part("vanilla:scrap_drivers_seat");
            Check(_host.Main.Logic.Seats.SeatOf(_client.Peer) is { } held && held.Part == seat, "the host still has the client in the seat");
            foreach (var (instance, who) in new[] { (_host, "host"), (_client, "client") })
            {
                var player = instance.Main.PlayerSync.Players[_client.Peer];
                Check(player.Seated, $"the {who} shows the driver seated");
                if (instance.Main.Logic.SeatTransform(_car, seat) is not { } at)
                {
                    Check(false, $"the {who} shows the seat");
                    continue;
                }
                Check(player.GlobalPosition.DistanceTo(at.Feet.Origin) < 0.3f, $"the {who} keeps the driver's feet on the seat ({player.GlobalPosition.DistanceTo(at.Feet.Origin):0.00} m off)");
                if (!Check(player.Character is Player.CharacterModel, $"the {who} draws the driver's body"))
                    continue;
                var body = (Player.CharacterModel)player.Character!;
                Check(body.GlobalBasis.Y.Normalized().Dot(at.Feet.Basis.Y.Normalized()) > 0.95f && body.GlobalBasis.Y.Normalized().Dot(Vector3.Up) < 0.8f,
                        $"the {who} draws the driver tipped over with the seat (body up {body.GlobalBasis.Y.Normalized()}, seat up {at.Feet.Basis.Y.Normalized()})");
            }
            return true;
        });
    }
}
