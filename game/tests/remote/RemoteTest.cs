using System.Text.Json;
using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Building;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Motion;
using Mechanica.Core.Remote;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// External AI endpoint and record/replay (chunk 22), host plus a client (D10). An agent connects to the
/// host's WebSocket JSON-RPC endpoint, authenticates, builds a four-wheeled test car through
/// <c>PlacePart</c>, and drives it with the generic motion primitives: bearing motors to go straight and
/// to turn on the spot, and <c>ApplyForce</c> to lift it. The client sees the car move through state
/// sync and may not call the motion actions itself. Bad requests get JSON-RPC errors. The session is
/// recorded; replaying the recording over a second host's endpoint rebuilds and drives the same car
/// with every call answered as it was. Uses the fixture mod in <c>tests/remote/mods</c>.
/// </summary>
public partial class RemoteTest : StagedTest
{
    private const string Mod = "drive_test";
    private const string Token = "test-token";
    private const string WatchToken = "watch-token";
    private const float Speed = 12f; // rad/s at the wheels
    private const float WheelTorque = 25f; // N·m
    private const float TurnTorque = 80f; // N·m: skid steering scrubs the wheels sideways
    private const int DriveFrames = 120;
    private static readonly Vec3 Start = new(2f, 0.75f, 12f);

    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private Instance _replayHost = null!;
    private readonly RpcTestClient _agent = new();
    private readonly RpcTestClient _replayAgent = new();
    private readonly RpcTestClient _watcher = new();

    /// <summary>A module method, to check modules are reachable over the real transport.</summary>
    private sealed class PingModule : IRpcModule
    {
        public IEnumerable<RpcMethodInfo> Methods =>
            [RpcMethod.Create<NoArgs>("test.ping", AgentScope.Observe, "Answers pong.", (_, _) => RpcMethod.Value("pong"))];
    }
    private readonly Dictionary<string, long> _ids = [];
    private long[] _batch = [];
    private int _creation;
    private readonly List<int> _left = [];
    private readonly List<int> _right = [];
    private Vector3 _parked;
    private Vector3 _afterDrive;
    private float _yawBefore;
    private float _heightBefore;
    private ActionResult? _clientResult;
    private JsonElement _recording;
    private int _recordedCount;
    private int _replayable;

    protected override string Tag => "remote";

    protected override int StageTimeoutFrames => 900;

    public override void _Ready()
    {
        _mods = ModLoader.LoadAll(ProjectSettings.GlobalizePath("res://tests/remote/mods"));
        _harness = new NetTestHarness { Name = "Harness" };
        AddChild(_harness);
        _host = _harness.StartHost(mods: _mods);
        _client = _harness.StartClient("Client", mods: _mods);

        Add("the client joins and the host opens the endpoint", _ =>
        {
            if (_host.Main.PlayerSync.Players.Count != 2 || _client.Main.PlayerSync.Players.Count != 2)
                return false;
            Check(!_mods.HasErrors, $"fixture mod loads ({string.Join("; ", _mods.Diagnostics)})");
            Check(_host.Main.Ai.Listen(0, "0.0.0.0") is { } refusal && refusal.Contains("token"), "a network-wide endpoint needs a token");
            Check(_client.Main.Ai.Listen(0) is not null, "a client can't open the endpoint");
            _host.Main.Ai.Modules.Add(new PingModule());
            var access = new EndpointAccess().AddToken(Token, [.. EndpointAccess.All]).AddToken(WatchToken, AgentScope.Observe);
            Check(_host.Main.Ai.Listen(0, "127.0.0.1", access) is null && _host.Main.Ai.Port > 0, $"the host listens on port {_host.Main.Ai.Port}");
            Check(_agent.Connect(_host.Main.Ai.Port) == Error.Ok, "the agent starts connecting");
            Check(_watcher.Connect(_host.Main.Ai.Port) == Error.Ok, "an observe-only agent starts connecting");
            return true;
        });
        Add("the agent connects", _ => Pump() && _agent.IsOpen && _watcher.IsOpen);
        Add("calls before mechanica.auth are refused", frame =>
        {
            if (frame == 0)
            {
                _ids["early"] = _agent.Call("mechanica.info");
                _ids["wrong"] = _agent.Call("mechanica.auth", new { token = "nope" });
                _ids["auth"] = _agent.Call("mechanica.auth", new { token = Token });
                _ids["info"] = _agent.Call("mechanica.info");
            }
            if (!Pump() || Answer("info") is not { } info)
                return false;
            Check(RpcTestClient.ErrorCode(Answer("early")!.Value) == JsonRpcEndpoint.Codes.Unauthorized, "unauthenticated call refused");
            Check(RpcTestClient.ErrorCode(Answer("wrong")!.Value) == JsonRpcEndpoint.Codes.Unauthorized, "wrong token refused");
            Check(RpcTestClient.ErrorCode(Answer("auth")!.Value) is null, "right token accepted");
            var actions = RpcTestClient.Result(info).GetProperty("actions").EnumerateArray().Select(a => a.GetString()).ToList();
            Check(new[] { "PlacePart", "RemovePart", "ApplyForce", "SetBearingTarget", "GetCreation", "ListCreations" }.All(actions.Contains),
                $"mechanica.info lists the actions ({string.Join(", ", actions)})");
            return true;
        });
        Add("describe, modules, scopes and events work over the socket", frame =>
        {
            if (frame == 0)
            {
                _ids["describe"] = _agent.Call("mechanica.describe", new { names = new[] { BuildActions.PlacePart, "test.ping" } });
                _ids["describe-all"] = _agent.Call("mechanica.describe");
                _ids["ping"] = _agent.Call("test.ping");
                _ids["subscribe"] = _agent.Call("mechanica.subscribe", new { topics = new[] { "actions" } });
                _ids["watch-auth"] = _watcher.Call("mechanica.auth", new { token = WatchToken });
                _ids["watch-subscribe"] = _watcher.Call("mechanica.subscribe", new { topics = new[] { "actions" } });
                _ids["watch-place"] = _watcher.Call(BuildActions.PlacePart, new PlacePartArgs($"{Mod}:block", new GridCell(0, 0, 0), Position: Start));
                _ids["watch-list"] = _watcher.Call(MotionActions.ListCreations);
            }
            if (!Pump() || Answer("subscribe") is not { } subscribed || Answer("watch-list", _watcher) is not { } list)
                return false;
            var methods = RpcTestClient.Result(Answer("describe")!.Value).GetProperty("methods").EnumerateArray().ToList();
            Check(methods.Count == 2 && methods[0].GetProperty("params").GetProperty("properties").TryGetProperty("cell", out _),
                "mechanica.describe gives PlacePart's params schema");
            var all = RpcTestClient.Result(Answer("describe-all")!.Value).GetProperty("methods").EnumerateArray().ToList();
            var undocumented = all.Where(m => string.IsNullOrEmpty(m.GetProperty("description").GetString())).Select(m => m.GetProperty("name").GetString()).ToList();
            Check(all.Count > _host.Main.Actions.Dispatcher.Names.Count && all.All(m => m.GetProperty("params").GetProperty("type").GetString() == "object"),
                $"every one of the host's {all.Count} methods has a params schema");
            Check(undocumented.Count == 0, $"and a description ({string.Join(", ", undocumented)})");
            var names = all.Select(m => m.GetProperty("name").GetString()).ToList();
            Check(new[] { "package.list", "package.inspect", "package.export", "package.install", "package.uninstall", "package.verify" }.All(names.Contains),
                "the game registers the package.* methods");
            Check(RpcTestClient.Result(Answer("ping")!.Value).GetString() == "pong", "a module method answers");
            Check(RpcTestClient.ErrorCode(subscribed) is null, "the agent subscribes to actions");
            Check(RpcTestClient.ErrorCode(Answer("watch-auth", _watcher)!.Value) is null, "the observe token is accepted");
            Check(RpcTestClient.ErrorCode(Answer("watch-place", _watcher)!.Value) == JsonRpcEndpoint.Codes.Forbidden, "an observer can't build");
            Check(RpcTestClient.ErrorCode(list) is null, "an observer can query");
            return true;
        });
        Add("bad requests get JSON-RPC errors", frame =>
        {
            if (frame == 0)
            {
                _agent.SendRaw("{ not json");
                _ids["unknown"] = _agent.Call("FlyToTheMoon");
                _ids["badargs"] = _agent.Call("PlacePart", new { part = 3 });
                _ids["positional"] = _agent.Call("ListCreations", new System.Text.Json.Nodes.JsonArray(1, 2));
                _ids["nocreation"] = _agent.Call(MotionActions.ApplyForce, new ApplyForceArgs(999, new Vec3(1, 0, 0)));
                _agent.Notify("ListCreations");
                _ids["record"] = _agent.Call("mechanica.record.start");
            }
            if (!Pump() || Answer("record") is not { } record)
                return false;
            Check(_agent.Unmatched.Count == 1 && RpcTestClient.ErrorCode(_agent.Unmatched[0]) == JsonRpcEndpoint.Codes.ParseError, "unparseable text: parse error");
            Check(RpcTestClient.ErrorCode(Answer("unknown")!.Value) == JsonRpcEndpoint.Codes.MethodNotFound, "unknown method: method not found");
            Check(RpcTestClient.ErrorCode(Answer("badargs")!.Value) == JsonRpcEndpoint.Codes.InvalidParams, "wrong argument types: invalid params");
            Check(RpcTestClient.ErrorCode(Answer("positional")!.Value) == JsonRpcEndpoint.Codes.InvalidParams, "positional params: invalid params");
            var missing = Answer("nocreation")!.Value;
            Check(RpcTestClient.ErrorCode(missing) == JsonRpcEndpoint.Codes.Rejected
                && missing.GetProperty("error").GetProperty("data").GetProperty("code").GetString() == ActionErrors.Rejected,
                $"a refused action: rejected, with the Action API code ({missing})");
            Check(RpcTestClient.ErrorCode(record) is null && _host.Main.Recorder.Log.IsRecording, "recording started");
            return true;
        });
        Add("the agent places the chassis", frame =>
        {
            if (frame == 0)
                _ids["chassis"] = _agent.Call(BuildActions.PlacePart, new PlacePartArgs($"{Mod}:block", new GridCell(0, 0, 0), Position: Start));
            if (!Pump() || Answer("chassis") is not { } chassis)
                return false;
            Check(RpcTestClient.ErrorCode(chassis) is null, $"PlacePart accepted ({chassis})");
            _creation = RpcTestClient.Result(chassis).GetProperty("creation").GetInt32();
            return true;
        });
        Add("subscribers are told about the placement", _ =>
        {
            Pump();
            static bool Placed(JsonElement e) =>
                e.GetProperty("params") is var p && p.GetProperty("topic").GetString() == "actions"
                && p.GetProperty("data").GetProperty("action").GetString() == BuildActions.PlacePart
                && p.GetProperty("data").GetProperty("ok").GetBoolean();
            return _agent.Events.Any(Placed) && _watcher.Events.Any(Placed);
        });
        Add("the agent builds the rest of the car in one batch", frame =>
        {
            if (frame == 0)
                _batch = _agent.Batch(CarParts(_creation).Select(p => (BuildActions.PlacePart, (object?)p)));
            if (!Pump() || _agent.Response(_batch[^1]) is null)
                return false;
            for (var i = 0; i < _batch.Length; i++)
            {
                var answer = _agent.Response(_batch[i])!.Value;
                if (!Check(RpcTestClient.ErrorCode(answer) is null, $"part {i + 2} placed ({answer})"))
                    return true;
            }
            Check(_agent.MessagesReceived > 0, "a batch is answered");
            return true;
        });
        Wait("the car settles on its wheels", 90);
        Add("GetCreation describes the car", frame =>
        {
            if (frame == 0)
                _ids["describe"] = _agent.Call(MotionActions.GetCreation, new GetCreationArgs(_creation));
            if (!Pump() || Answer("describe") is not { } describe)
                return false;
            var state = RpcTestClient.Result(describe).Deserialize<CreationState>(ActionJson.Options)!;
            Check(state.Parts.Count == 12, $"12 parts ({state.Parts.Count})");
            Check(state.Bodies.Count == 5, $"one chassis body and four wheels ({state.Bodies.Count})");
            Check(state.Joints.Count == 4 && state.Joints.All(j => j.Type == ConnectionType.Rotational && !j.Locked), "four free bearings");
            Check(state.Simulated && state.AuthorityPeer == 1, "simulated by the host");
            var creation = _host.Main.Creations.Find(_creation)!;
            foreach (var joint in state.Joints)
            {
                // Bearings on the +z side turn about +z, those on the -z side about -z.
                var side = creation.FindPart(joint.Part)!.Cell.Z;
                (side > 0 ? _left : _right).Add(joint.Part);
            }
            _parked = ChassisPosition(_host);
            Check(_parked.Y is > 0.2f and < 0.4f, $"the chassis rides on the wheels (y = {_parked.Y:F3})");
            return true;
        });
        Add("a client's player may not call motion actions", frame =>
        {
            if (frame == 0)
                _client.Main.Actions.Request(MotionActions.ApplyForce, new ApplyForceArgs(_creation, new Vec3(0, 1000, 0)), r => _clientResult = r);
            if (_clientResult is null)
                return false;
            Check(_clientResult is { Ok: false, Code: ActionErrors.Rejected }, $"the host refuses it ({_clientResult})");
            return true;
        });
        Add("the agent drives the car forward", frame =>
        {
            if (frame == 0)
                SetWheels(Speed, -Speed);
            Pump();
            if (frame < DriveFrames)
                return false;
            SetWheels(0, 0);
            return true;
        });
        Wait("the car brakes", 60);
        Add("the car moved straight along its length", _ =>
        {
            _afterDrive = ChassisPosition(_host);
            var moved = _afterDrive - _parked;
            Check(Mathf.Abs(moved.X) > 2f, $"it drove {moved.X:F2} m along x");
            Check(Mathf.Abs(moved.Z) < 0.5f * Mathf.Abs(moved.X), $"and kept straight ({moved.Z:F2} m sideways)");
            var seen = ChassisPosition(_client);
            Check(seen.DistanceTo(_afterDrive) < 0.1f, $"the client sees it where the host has it ({seen.DistanceTo(_afterDrive):F3} m)");
            _yawBefore = Yaw(_host);
            return true;
        });
        Add("the agent turns the car on the spot", frame =>
        {
            if (frame == 0)
                SetWheels(Speed, Speed, TurnTorque);
            Pump();
            if (frame < DriveFrames / 2)
                return false;
            SetWheels(0, 0);
            return true;
        });
        Wait("the car stops turning", 60);
        Add("the car turned", _ =>
        {
            var turned = Mathf.RadToDeg(Mathf.AngleDifference(_yawBefore, Yaw(_host)));
            Check(Mathf.Abs(turned) > 20f, $"it turned {turned:F0}°");
            Check(ChassisPosition(_host).DistanceTo(_afterDrive) < 1f, "on the spot");
            _heightBefore = ChassisPosition(_host).Y;
            return true;
        });
        Add("ApplyForce lifts the car", frame =>
        {
            if (frame == 0)
            {
                _ids["toobig"] = _agent.Call(MotionActions.ApplyForce, new ApplyForceArgs(_creation, new Vec3(0, 1e6f, 0)));
                // About twice the car's weight for a fifth of a second.
                _ids["lift"] = _agent.Call(MotionActions.ApplyForce, new ApplyForceArgs(_creation, new Vec3(0, 1500, 0), Duration: 0.2f));
            }
            if (!Pump() || frame < 15 || Answer("lift") is not { } lift)
                return false;
            Check(RpcTestClient.ErrorCode(Answer("toobig")!.Value) == JsonRpcEndpoint.Codes.Rejected, "a force over the limit is refused");
            Check(RpcTestClient.ErrorCode(lift) is null, $"the lift is accepted ({lift})");
            return true;
        });
        Add("the car rose", _ =>
        {
            var rise = ChassisPosition(_host).Y - _heightBefore;
            Check(rise > 0.05f, $"it rose {rise:F2} m");
            return true;
        });
        Wait("the car lands", 90);
        Add("the agent stops recording", frame =>
        {
            if (frame == 0)
                _ids["stop"] = _agent.Call("mechanica.record.stop");
            if (!Pump() || Answer("stop") is not { } stop)
                return false;
            _recording = RpcTestClient.Result(stop).Clone();
            var recording = ActionRecording.FromElement(_recording);
            var names = recording.Actions.Select(a => a.Action).ToList();
            _recordedCount = recording.Actions.Count;
            _replayable = recording.Actions.Count(a => a.Result.Ok);
            Check(names.Count(n => n == BuildActions.PlacePart) == 12, $"12 PlacePart calls recorded ({string.Join(", ", names.Distinct())})");
            Check(!names.Contains(MotionActions.GetCreation) && !names.Contains(MotionActions.ListCreations), "queries are not recorded");
            Check(recording.Actions.Any(a => a is { Action: MotionActions.ApplyForce, Result.Ok: false } && a.Peer == _client.Peer),
                "the client's refused call is in the recording too");
            Check(recording.Actions.All(a => a.Tick >= 0) && recording.Actions[^1].Tick > DriveFrames, "with the ticks they ran on");
            var path = ProjectSettings.GlobalizePath("user://remote_test_recording.json");
            File.WriteAllText(path, recording.ToJson());
            Check(ActionRecording.FromJson(File.ReadAllText(path)).Actions.Count == _recordedCount, "it round-trips through a file");
            return true;
        });
        Add("a second host opens its endpoint", frame =>
        {
            if (frame == 0)
            {
                _replayHost = _harness.StartHost(name: "ReplayHost", mods: _mods);
                return false;
            }
            if (_replayHost.Main.LocalPlayer is null)
                return false;
            Check(_replayHost.Main.Ai.Listen(0) is null, "listening without a token on loopback");
            Check(_replayAgent.Connect(_replayHost.Main.Ai.Port) == Error.Ok, "the agent connects to it");
            return true;
        });
        Add("the agent connects to the second host", _ => Pump() && _replayAgent.IsOpen);
        Add("the recording replays there", frame =>
        {
            if (frame == 0)
                _ids["replay"] = _replayAgent.Call("mechanica.replay.start", new { recording = _recording, timed = true });
            if (!Pump() || Answer("replay", _replayAgent) is not { } replay)
                return false;
            Check(RpcTestClient.ErrorCode(replay) is null, $"replay started ({replay})");
            Check(RpcTestClient.Result(replay).GetProperty("total").GetInt32() == _replayable, $"every call that succeeded is replayed ({_replayable})");
            return true;
        });
        Add("the replay finishes", frame =>
        {
            if (frame == 0)
                _ids["status"] = _replayAgent.Call("mechanica.replay.status");
            if (!Pump() || Answer("status", _replayAgent) is not { } answer)
                return false;
            var status = RpcTestClient.Result(answer).Deserialize<ReplayStatus>(ActionJson.Options)!;
            if (status.Running)
            {
                _ids["status"] = _replayAgent.Call("mechanica.replay.status");
                return false;
            }
            Check(status.Dispatched == status.Total, $"all {status.Total} calls dispatched");
            Check(status.Mismatches.Count == 0,
                $"each answered as recorded ({string.Join("; ", status.Mismatches.Select(m => $"{m.Index} {m.Action}: {m.Expected} vs {m.Actual}"))})");
            return true;
        });
        Wait("the replayed car lands", 90);
        Add("the replayed car ended up where the original did", _ =>
        {
            var original = ChassisPosition(_host);
            var replayed = ChassisPosition(_replayHost);
            Check(replayed.DistanceTo(original) < 0.25f, $"within 0.25 m ({replayed} vs {original})");
            _agent.Close();
            _watcher.Close();
            _replayAgent.Close();
            return true;
        });
        Add("the endpoint drops closed connections", _ =>
        {
            Pump();
            return _host.Main.Ai.ConnectionCount == 0 && _replayHost.Main.Ai.ConnectionCount == 0;
        });
    }

    private bool Pump()
    {
        _agent.Poll();
        _watcher.Poll();
        _replayAgent.Poll();
        return true;
    }

    private JsonElement? Answer(string name, RpcTestClient? client = null) =>
        _ids.TryGetValue(name, out var id) ? (client ?? _agent).Response(id) : null;

    private void SetWheels(float left, float right, float torque = WheelTorque) =>
        _agent.Batch(_left.Select(p => (MotionActions.SetBearingTarget, (object?)new SetBearingTargetArgs(_creation, p, left, torque)))
            .Concat(_right.Select(p => (MotionActions.SetBearingTarget, (object?)new SetBearingTargetArgs(_creation, p, right, torque)))));

    /// <summary>A 4-block chassis along x, a bearing on each side of its end blocks and a wheel on each bearing.</summary>
    private static IEnumerable<PlacePartArgs> CarParts(int creation)
    {
        for (var x = 1; x <= 3; x++)
            yield return new PlacePartArgs($"{Mod}:block", new GridCell(x, 0, 0), Creation: creation);
        foreach (var x in new[] { 0, 3 })
        {
            foreach (var side in new[] { 1, -1 })
            {
                var rotation = GridRotation.FromAxes(new GridCell(1, 0, 0), new GridCell(0, 0, side));
                yield return new PlacePartArgs($"{Mod}:bearing", new GridCell(x, 0, side), rotation, creation);
                yield return new PlacePartArgs($"{Mod}:wheel", new GridCell(x, 0, 2 * side), rotation, creation);
            }
        }
    }

    private Vector3 ChassisPosition(Instance instance) =>
        instance.Main.Creations.NodeOf(_creation)!.Bodies[0].GlobalPosition;

    private float Yaw(Instance instance)
    {
        var forward = instance.Main.Creations.NodeOf(_creation)!.Bodies[0].GlobalBasis.X;
        return Mathf.Atan2(-forward.Z, forward.X);
    }
}
