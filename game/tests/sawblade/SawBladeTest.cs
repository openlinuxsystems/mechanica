using Godot;
using Mechanica.Core.Actions;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Motion;
using Mechanica.Core.World;
using Mechanica.Game.Mods;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Saw Blade spec, host plus a client, on flat ground: a saw blade lies flat on a bearing on a heavy slab, its rim just
/// short of a felled Pine Log lying on the slab. Turned slowly it does nothing; driven past cutting speed (as an engine would) its hub
/// glows on the client and its rim bites the log at the felled-log rate, throwing chips on both machines, until the log
/// breaks into Pine Rods that both machines see.
/// </summary>
public partial class SawBladeTest : StagedTest
{
    private const int StandId = 1, LogId = 2;
    private const string PineRod = "vanilla:pine_rod";
    private static readonly ActionCaller Bot = new(Creation.HostPeer, ActionSource.Bot);

    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _bearing, _saw;

    protected override string Tag => "sawblade";

    protected override int StageTimeoutFrames => 900;

    public override void _Ready()
    {
        Add("a host and a client start on flat ground", frame =>
        {
            if (frame == 0)
            {
                _mods = ModLoader.Load([Path.Combine(ModPaths.ModsRoot, "vanilla")]);
                Check(!_mods.HasErrors, $"vanilla loads ({string.Join("; ", _mods.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))})");
                _harness = new NetTestHarness { Name = "Harness", WorldGen = WorldGenData.Flat };
                AddChild(_harness);
                _host = _harness.StartHost(mods: _mods);
                _client = _harness.StartClient("Client", mods: _mods);
            }
            return frame > 0 && _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null;
        });
        Add("spawn the saw on its stand beside a Pine Log", _ =>
        {
            // An 11 x 11 slab of metal blocks, a bearing on its middle and the saw flat on the bearing: the blade's
            // middle is as high above the slab as the middle of a Pine Log lying on it.
            var stand = new Creation(StandId, _mods);
            for (var x = -5; x <= 5; x++)
                for (var z = -5; z <= 5; z++)
                    Place(stand, "vanilla:metal_block", new GridCell(x, 0, z));
            _bearing = Place(stand, "vanilla:bearing", new GridCell(0, 1, 0)).Id;
            _saw = Place(stand, "vanilla:saw_blade", new GridCell(-2, 2, -2)).Id;
            stand.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(0, 0.13f, 0), System.Numerics.Quaternion.Identity));
            _host.Main.Creations.Add(stand, _host.Main.Assets);
            // The log (0.75 m across) lies on the slab along Z, its near side 0.04 m past the blade's collider (0.6 m) and
            // inside the reach of its teeth.
            var log = new Creation(LogId, _mods);
            Place(log, "vanilla:pine_log", GridCell.Zero);
            var size = stand.DefinitionOf(_saw).Size;
            Check(size == new GridSize(5, 1, 5), $"the saw is 5 x 1 x 5 blocks ({size})");
            // Its footprint centre is (0.25, 0.25, 0.5) m from its grid origin: centred at x = 0.64 + 0.375, z = 0, on the
            // slab's top (0.255 m).
            log.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(0.64f + 0.375f - 0.25f, 0.255f + 0.375f - 0.25f + 0.005f, -0.5f),
                System.Numerics.Quaternion.Identity));
            _host.Main.Creations.Add(log, _host.Main.Assets);
            return true;
        });
        Wait("everything settles", 90);

        Add("turned slower than cutting speed it does not cut", frame =>
        {
            if (frame == 0)
                Check(Act(MotionActions.SetBearingTarget, new SetBearingTargetArgs(StandId, _bearing, 5f, 300f)).Ok, "the bearing turns the saw at 5 rad/s");
            if (frame < 90)
                return false;
            Check(LogDamage() == 0, $"the log is untouched ({LogDamage()})");
            Check(_host.Main.Streamer.Effects.Bites == 0, "no chips fly");
            return true;
        });
        Add("past cutting speed its hub glows on the client and it bites the log, chips flying on both machines", frame =>
        {
            if (frame == 0)
                Check(Act(MotionActions.SetBearingTarget, new SetBearingTargetArgs(StandId, _bearing, 14f, 300f)).Ok, "the bearing turns the saw at 14 rad/s");
            if (LogDamage() <= 0 || _client.Main.Streamer.Effects.Bites == 0)
                return frame > 600 && Fail($"the saw never cut ({Diagnose()})");
            Check(LogDamage() % 30f == 0, $"each bite takes 30, the felled-log rate ({LogDamage()})");
            Check(_host.Main.Streamer.Effects.Bites > 0, "the host shows the chips too");
            Check(_client.Main.Display.Readouts.Number(StandId, _saw, "spin") == 1f, "the client knows it runs at cutting speed");
            var glow = _client.Main.Creations.NodeOf(StandId)?.FindChildren("HubGlow*", nameof(Node3D), true, false).OfType<Node3D>().FirstOrDefault();
            Check(glow is not null && glow.Scale.X > 0.9f, $"the hub glow is full on the client ({glow?.Scale})");
            return true;
        });
        Add("the log breaks into Pine Rods on both machines", frame =>
        {
            if (Count(_host, "vanilla:pine_log") > 0 || Count(_client, "vanilla:pine_log") > 0 || Count(_host, PineRod) < 2 || Count(_client, PineRod) < 2)
                return frame > 600 && Fail($"the log never broke ({Count(_host, "vanilla:pine_log")} logs, {Count(_client, PineRod)} rods on the client)");
            Check(Count(_host, PineRod) == 2, $"the log breaks into two Pine Rods ({Count(_host, PineRod)})");
            return true;
        });
    }

    private bool Fail(string message)
    {
        Check(false, message);
        return true;
    }

    // Where the saw and the log are and how the saw runs, for a failure's message.
    private string Diagnose()
    {
        var stand = _host.Main.Creations.NodeOf(StandId);
        var log = _host.Main.Creations.NodeOf(LogId);
        var fault = _host.Main.Scripts.Runtime.Instances.FirstOrDefault(i => i.Part == _saw)?.Fault;
        return $"spin {_host.Main.Display.Readouts.Number(StandId, _saw, "spin")}, saw body {stand?.Bodies.Last().GlobalPosition} "
            + $"turning {stand?.Bodies.Last().AngularVelocity}, log {log?.Bodies[0].GlobalPosition}, "
            + $"host bites {_host.Main.Streamer.Effects.Bites}, damage {LogDamage()}, fault {fault}";
    }

    private float LogDamage() =>
        _host.Main.Creations.Find(LogId) is { } log && log.Parts.Count == 1 ? log.PartDamage(log.Parts.First().Id) : -1;

    private static int Count(Instance instance, string part) =>
        instance.Main.Creations.All.Count(c => c.Parts.Count == 1 && Identifiers.Qualify(c.Parts.First().Part) == part);

    private ActionResult Act<TArgs>(string action, TArgs args) => _host.Main.Actions.Dispatcher.Dispatch(Bot, action, args);

    private static PartInstance Place(Creation creation, string part, GridCell cell, GridRotation rotation = default)
    {
        if (!creation.TryPlacePart(part, cell, rotation, out var placed, out var error))
            throw new InvalidOperationException(error);
        return placed;
    }
}
