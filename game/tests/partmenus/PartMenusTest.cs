using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Logic;
using Mechanica.Core.World;
using Mechanica.Game.Logic;
using Mechanica.Game.Mods;
using Mechanica.Game.Player;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Seats, part menus and machine feedback (UI spec PRs 13 and 17), host plus a client on vanilla parts in Creative.
/// The client sits in a driver's seat: the player glides in, the seat camera takes over facing the seat's front and
/// Ctrl switches it to the chase view; the seat's hotbar shows the switch it is linked to and uses it from beyond
/// reach; standing up puts the player beside the seat. E on a logic gate cycles its mode with a card. An engine with
/// nothing linked in runs from its On toggle, its menu shows fuel and speed, its status dot turns green and its wheel
/// bearing's highlight turns. A controller's frames wait for Apply, and Play previews them on the host; the piston it runs has its menu locked. A sensor's
/// menu shows its cone and its Detects toggles.
/// </summary>
public partial class PartMenusTest : StagedTest
{
    private const int Board = 1;

    private readonly List<ActionResult> _results = [];
    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private PartSettingsPanel _panel = null!;
    private SeatCamera _camera = null!;
    private CreationHotbar _hotbar = null!;
    private int _seat, _switch, _engine, _wheel, _gate, _controller, _arm, _piston, _sensor;
    private Vector3 _seatAt;

    protected override string Tag => "partmenus";

    protected override int StageTimeoutFrames => 900;

    private PlayerController Me => _client.Main.LocalPlayer!;

    private Creation HostBoard => _host.Main.Creations.Find(Board)!;

    public override void _Ready()
    {
        Add("a host and a client start on vanilla in Creative", frame =>
        {
            if (frame == 0)
            {
                _mods = ModLoader.Load([Path.Combine(ModPaths.ModsRoot, "vanilla")]);
                Check(!_mods.HasErrors, "vanilla loads");
                _harness = new NetTestHarness { Name = "Harness", WorldGen = WorldGenData.Flat };
                AddChild(_harness);
                _host = _harness.StartHost(mods: _mods);
                _client = _harness.StartClient("Client", mods: _mods);
            }
            return frame > 0 && _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null;
        });
        Add("the host spawns a board of machines, and the client gets a menu, a seat camera and a seat hotbar", frame =>
        {
            if (frame == 0)
            {
                SpawnBoard();
                _host.Main.PlayerSync.HostTeleport(_client.Peer, new Vector3(0, 0.1f, 1.2f));
                var main = _client.Main;
                _panel = new PartSettingsPanel
                {
                    Name = "PartSettings", Actions = main.Actions, Creations = main.Creations, Readouts = () => main.Display.Readouts,
                    Signals = main.LogicOverlay, Assets = main.Assets,
                };
                _camera = new SeatCamera { Logic = main.Logic, Players = main.PlayerSync, ReadDeviceInput = false };
                _hotbar = new CreationHotbar
                {
                    Logic = main.Logic, Players = main.PlayerSync, Creations = main.Creations, Actions = main.Actions, Signals = main.LogicOverlay,
                    ReadDeviceInput = false,
                };
                main.AddChild(_panel);
                main.AddChild(_camera);
                main.AddChild(_hotbar);
            }
            return frame >= 30 && _client.Main.Creations.NodeOf(Board) is not null;
        });
        Wait("the board settles", 60);
        Add("sitting down glides the player in and the seat camera faces the seat's front", frame =>
        {
            if (frame == 0)
            {
                Request(LogicActions.EnterSeat, new LogicPartArgs(Board, _seat));
                return false;
            }
            if (frame < 3 || _client.Main.Logic.Seats.SeatOf(_client.Peer) is null)
                return false;
            if (frame < 200 && !_camera.Active)
                return false;
            Check(_camera.Active && _camera.Camera.Current, "the seat camera is the view");
            Check(!_camera.Chase, "it starts inside");
            _seatAt = _client.Main.Logic.SeatTransform(Board, _seat)!.Value.Feet.Origin;
            return true;
        });
        Add("the glide ends in the seat, and the camera looks the way the seat faces", frame =>
        {
            if (Me.InTransition && frame < 120)
                return false;
            Check(Me.ShownPosition.DistanceTo(_seatAt) < 0.3f, $"the player shows in the seat ({Me.ShownPosition} vs {_seatAt})");
            var look = -_camera.Camera.GlobalBasis.Z;
            var front = -_client.Main.Logic.SeatTransform(Board, _seat)!.Value.Feet.Basis.Z;
            Check(new Vector2(look.X, look.Z).Normalized().Dot(new Vector2(front.X, front.Z).Normalized()) > 0.9f, $"the camera faces the seat's front ({look} vs {front})");
            return true;
        });
        Add("Ctrl switches to the chase camera behind and above", frame =>
        {
            if (frame == 0)
            {
                _camera.Toggle();
                return false;
            }
            if (frame < 60)
                return false;
            Check(_camera.Chase, "the chase view is on");
            var offset = _camera.Camera.GlobalPosition - _seatAt;
            Check(offset.Y > 1f && new Vector2(offset.X, offset.Z).Length() > 3f, $"it sits behind and above ({offset})");
            _camera.Toggle();
            return true;
        });
        Add("the seat's hotbar holds its switch and uses it from beyond reach", frame =>
        {
            if (frame == 0)
            {
                Check(_hotbar.Showing && _hotbar.Parts.SequenceEqual([_switch]), $"the bar holds the switch ({string.Join(", ", _hotbar.Parts)})");
                Check(_hotbar.Use(0), "slot 1 uses it");
                return false;
            }
            if (_results.Count == 0)
                return false;
            Answered(1);
            return true;
        });
        Add("the far switch turns on, and its slot shows it", frame =>
            (_host.Main.Scripts.Runtime.Signals.Output(Board, _switch) == 1 && _hotbar.IsOn(0)) || (frame > 200 && Check(false, $"the switch is on ({_host.Main.Scripts.Runtime.Signals.Output(Board, _switch)}, slot {_hotbar.IsOn(0)})")));
        Add("standing up puts the player beside the seat and hands the view back", frame =>
        {
            if (frame == 0)
            {
                Request(LogicActions.LeaveSeat, new NoArgs());
                return false;
            }
            if (_client.Main.Logic.Seats.SeatOf(_client.Peer) is not null || (Me.InTransition && frame < 200) || frame < 30)
                return false;
            Check(!_camera.Active && Me.Camera.Current, "the player's camera is back");
            var away = Me.GlobalPosition - _seatAt;
            Check(new Vector2(away.X, away.Z).Length() is > 0.3f and < 1.5f, $"the player stands beside the seat ({away})");
            Check(!_hotbar.Showing, "the seat's bar is gone");
            return true;
        });
        Add("E on a logic gate switches its mode and shows a card", frame =>
        {
            if (frame == 0)
            {
                Check(_panel.NextOption(Board, _gate) == "or", "and goes to or");
                Check(_panel.Cycle(Board, _gate), "the gate cycles");
                Check(_panel.CardShowing && _panel.CardName == "OR" && _panel.CardIcon is not null, $"the card shows OR with its picture ({_panel.CardName})");
                return false;
            }
            return HostBoard.FindPart(_gate)!.Settings?.GetValueOrDefault("mode").GetString() == "or" || (frame > 200 && Check(false, "the host switches the gate"));
        });
        Add("an engine with nothing linked in runs from its On toggle", frame =>
        {
            if (frame == 0)
            {
                _panel.Open(Board, _engine);
                var on = _panel.Rows.FindChild("Setting_on", true, false) as Control;
                Check(on is not null && on.FindChild("Toggle", true, false) is CheckBox, "the menu has an On toggle");
                ((CheckBox)on!.FindChild("Toggle", true, false)).ButtonPressed = true;
                Check(_panel.Rows.FindChild("Presets", true, false) is Control { } presets && presets.GetChildCount() == 3, "Power has Low, Medium and High");
                return false;
            }
            return HostBoard.FindPart(_engine)!.Settings?.GetValueOrDefault("on").ValueKind != JsonValueKind.False || (frame > 200 && Check(false, "the host turns it on"));
        });
        Add("the engine's menu shows its fuel and speed, its dot turns green and its bearing's highlight turns", frame =>
        {
            var node = _client.Main.Creations.NodeOf(Board)!;
            var dot = node.Displays.FirstOrDefault(d => d.Part == _engine);
            var ring = node.Displays.FirstOrDefault(d => d.Part == _wheel);
            var fuel = _panel.Rows.FindChild("Readout_fuel", true, false)?.FindChild("Value", true, false) as ProgressBar;
            var rpm = _panel.Rows.FindChild("Readout_rpm", true, false)?.FindChild("Value", true, false) as Label;
            if ((dot?.StatusRunning != true || ring?.Highlight?.Visible != true || fuel?.Value != 1 || rpm?.Text.EndsWith("rpm") != true) && frame < 300)
                return false;
            Check(dot?.StatusRunning == true, "the engine's status dot is green");
            Check(ring?.Highlight?.Visible == true, "the wheel bearing's highlight turns");
            Check(fuel?.Value == 1, $"Creative shows a full tank ({fuel?.Value})");
            Check(rpm?.Text.EndsWith("rpm") == true, $"the speed reads in rpm ({rpm?.Text})");
            Check(_panel.Rows.FindChild("Setting_on", true, false) is Control { Visible: true }, "nothing is linked in, so On shows");
            _panel.Close();
            return true;
        });
        Add("a controller's frames wait for Apply", frame =>
        {
            if (frame == 0)
            {
                _panel.Open(Board, _controller);
                var sequence = _panel.Sequence;
                Check(sequence is not null && sequence.Joints.Count == 2, $"the editor lists the bearing and the piston ({sequence?.Joints.Count})");
                Check(_panel.ApplyButton.Visible && _panel.ApplyButton.Disabled, "Apply shows, with nothing to apply");
                sequence!.Pick(0);
                sequence.SetStep(_arm, 45, 2);
                sequence.SetStep(_piston, 0.5f, 0.5f);
                sequence.AddFrame();
                sequence.SetStep(_arm, -30, 1);
                Check(sequence.Frames.Count == 3, $"a frame was added ({sequence.Frames.Count})");
                Check(_panel.Pending.ContainsKey("frames") && !_panel.ApplyButton.Disabled, "the change waits");
                return false;
            }
            if (frame < 30)
                return false;
            Check(HostBoard.FindPart(_controller)!.Settings?.ContainsKey("frames") != true, "the host has not changed yet");
            return true;
        });
        Add("Play previews the frames on the host", frame =>
        {
            if (frame == 0)
            {
                _panel.Sequence!.SetPlaying(true);
                return false;
            }
            return _panel.Status == "Previewing." || (frame > 200 && Check(false, $"the host previews ({_panel.Status})"));
        });
        Add("Apply sends the frames", frame =>
        {
            if (frame == 0)
            {
                _panel.Sequence!.SetPlaying(false);
                _panel.ApplyButton.EmitSignal(BaseButton.SignalName.Pressed);
                return false;
            }
            if (HostBoard.FindPart(_controller)!.Settings?.GetValueOrDefault("frames") is not { ValueKind: JsonValueKind.Array } frames)
                return frame > 200 && Check(false, $"the host takes the frames ({_panel.Status})");
            var steps = PartSettings.Frames(frames, [], 1);
            Check(steps.Count == 3 && steps[0].Any(s => s.Part == _arm && s.Target == 45 && s.Speed == 2), $"the first frame turns the arm to 45° ({frames.GetRawText()})");
            Check(steps[0].Any(s => s.Part == _piston && Math.Abs(s.Target - 0.5f) < 1e-4f), "and pushes the piston out 0.5 m");
            _panel.Close();
            return true;
        });
        Add("the controller locks the piston's menu", frame =>
        {
            if (frame == 0)
            {
                _panel.Open(Board, _piston);
                Check(_panel.IsOpen && _panel.Rows.GetChildCount() == 0, $"the piston's menu shows no settings ({_panel.Rows.GetChildCount()} rows)");
                Check(_panel.Status.Contains("is run by the Controller"), $"it says the Controller runs it ({_panel.Status})");
                _panel.Close();
                _results.Clear();
                Request(LogicActions.ConfigurePart, new ConfigurePartArgs(Board, _piston,
                    new Dictionary<string, JsonElement?> { ["length"] = JsonSerializer.SerializeToElement(3) }));
                return false;
            }
            if (_results.Count == 0)
                return frame > 200 && Check(false, "the host answers");
            Check(!_results[0].Ok && _results[0].Message!.Contains("is run by the Controller"), $"the host refuses its settings ({_results[0]})");
            return true;
        });
        Add("a sensor's menu shows its cone and what it detects", frame =>
        {
            if (frame == 0)
            {
                _panel.Open(Board, _sensor);
                return false;
            }
            Check(_panel.PreviewMesh is { Mesh: CylinderMesh }, "a cone shows in the world");
            Check(_panel.Rows.FindChild("Group_players", true, false) is Control { } group && group.FindChildren("Toggle_*", "", true, false).Count == 4,
                "Players, Bots, Blocks and Creations sit side by side");
            _panel.Close();
            Check(_panel.PreviewMesh is null, "closing takes the cone away");
            return true;
        });
    }

    private void SpawnBoard()
    {
        var board = new Creation(Board, _mods);
        for (var x = -12; x <= 6; x++)
            Place(board, "vanilla:metal_block", new GridCell(x, 0, 0));
        _seat = Place(board, "vanilla:drivers_seat", new GridCell(0, 1, 0));
        _switch = Place(board, "vanilla:switch", new GridCell(-12, 1, 0));
        _engine = Place(board, "vanilla:gas_engine", new GridCell(-4, 1, -1));
        var up = GridRotation.FromAxes(new GridCell(1, 0, 0), new GridCell(0, 1, 0));
        // A wheel bearing on the end of the row, turning about X so the engine drives it.
        _wheel = Place(board, "vanilla:bearing", new GridCell(-13, 0, 0), GridRotation.FromAxes(new GridCell(0, 1, 0), new GridCell(-1, 0, 0)));
        Place(board, "vanilla:metal_block", new GridCell(-14, 0, 0));
        _gate = Place(board, "vanilla:logic_gate", new GridCell(2, 1, 0));
        _controller = Place(board, "vanilla:controller", new GridCell(3, 1, 0));
        _arm = Place(board, "vanilla:bearing", new GridCell(4, 1, 0), up);
        Place(board, "vanilla:metal_block", new GridCell(4, 2, 0));
        _piston = Place(board, "vanilla:piston", new GridCell(5, 1, 0), up);
        Place(board, "vanilla:metal_block", new GridCell(5, 2, 0));
        _sensor = Place(board, "vanilla:sensor", new GridCell(6, 1, 0), up);
        Link(board, _seat, _switch);
        Link(board, _engine, _wheel);
        // Engines are on by default; this one starts off so its menu's On toggle can start it.
        board.SetPartSettings(_engine, new Dictionary<string, JsonElement> { ["on"] = JsonSerializer.SerializeToElement(false) });
        Link(board, _controller, _arm);
        Link(board, _controller, _piston);
        board.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(0, 0.15f, -0.6f), System.Numerics.Quaternion.Identity));
        _host.Main.Creations.Add(board, _host.Main.Assets);
    }

    private static int Place(Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        if (!creation.TryPlacePart(part, cell, rotation, out var placed, out var error))
            throw new InvalidOperationException($"{part} at {cell}: {error}");
        return placed.Id;
    }

    private static void Link(Creation creation, int from, int to)
    {
        if (!creation.TryLink(from, to, out var error))
            throw new InvalidOperationException(error);
    }

    // True once count answers are in, all taken.
    private bool Answered(int count)
    {
        if (_results.Count < count)
            return false;
        Check(_results.All(r => r.Ok), $"the host takes them ({string.Join("; ", _results.Select(r => r.Message))})");
        _results.Clear();
        return true;
    }

    private void Request<T>(string action, T args) => _client.Main.Actions.Request(action, args, _results.Add);
}
