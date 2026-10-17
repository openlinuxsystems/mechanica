using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.World;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// The D2 Lua benchmark in the real game: one Creation of <see cref="Parts"/> scripted wings (lift and
/// drag computed in Lua every 60 Hz physics step, pushing through <c>ApplyForce</c>) glides through
/// Jolt on a host with a client watching. It measures the host's script time and whole physics step
/// with the scripts running, then again with them paused, and prints both (the whole step only under
/// <c>--fixed-fps</c>, which <c>tools/lua-bench.sh</c> passes; otherwise steps are paced to real time). It checks only that every
/// wing ran without error and that the lift moved the machine; timings are reported, not asserted, so
/// slow CI machines don't fail it. Uses the fixture mod in <c>tests/luabench/mods</c>.
/// </summary>
public partial class LuaBenchTest : StagedTest
{
    private const int Parts = 200;
    private const int Warmup = 60;
    private const int Measured = 180;
    private const int Machine = 1;

    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private readonly List<double> _scriptMs = [];
    private readonly List<double> _stepMs = [];
    private long _lastTick;
    private (double Script, double Step, double P99Script) _withScripts;
    private float _fallWithLift;

    protected override string Tag => "luabench";

    protected override int StageTimeoutFrames => Warmup + Measured + 120;

    public override void _Ready()
    {
        _mods = ModLoader.LoadAll(ProjectSettings.GlobalizePath("res://tests/luabench/mods"));
        _harness = new NetTestHarness { Name = "Harness", WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost(mods: _mods);
        _client = _harness.StartClient("Client", mods: _mods);

        Add("the client joins", _ => _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null);
        Add($"launch a glider of {Parts} scripted wings", _ =>
        {
            Check(!_mods.HasErrors, $"fixture mod loads ({string.Join("; ", _mods.Diagnostics)})");
            Launch();
            return true;
        });
        Add("measure with the scripts running", frame => Measure(frame, () =>
        {
            var info = _host.Main.Scripts.Runtime.Instances;
            Check(info.Count == Parts && info.All(i => i.Fault is null), $"all {Parts} wing scripts run ({info.Count(i => i.Fault is null)} without errors)");
            _fallWithLift = 100f - HostY();
            _withScripts = Summary();
            _host.Main.Scripts.Running = false;
            Relaunch();
        }));
        Add("measure with the scripts paused", frame => Measure(frame, () =>
        {
            var fall = 100f - HostY();
            Check(fall > _fallWithLift + 1f, $"the wings' lift slowed the fall ({_fallWithLift:F1} m with lift, {fall:F1} m without)");
            var paused = Summary();
            var client = _client.Main.Creations.NodeOf(Machine) is not null;
            Check(client, "the client sees the glider");
            GD.Print($"[luabench] RESULT {Parts} Lua wings at {Engine.PhysicsTicksPerSecond} Hz, host + 1 client in one process, {OS.GetProcessorCount()} CPUs ({OS.GetProcessorName()}):");
            GD.Print($"[luabench] RESULT   scripts: mean {_withScripts.Script:F3} ms/step (p99 {_withScripts.P99Script:F3}), {_withScripts.Script * 1000 / Parts:F2} µs per part");
            if (OS.GetCmdlineArgs().Contains("--fixed-fps"))
                GD.Print($"[luabench] RESULT   whole physics step (both instances, Jolt included): {_withScripts.Step:F3} ms with scripts, {paused.Step:F3} ms paused");
            else
                GD.Print("[luabench] RESULT   (whole-step times need --fixed-fps; tools/lua-bench.sh runs that)");
            GD.Print($"[luabench] RESULT   scripts use {_withScripts.Script / (1000.0 / Engine.PhysicsTicksPerSecond) * 100:F1}% of a {1000.0 / Engine.PhysicsTicksPerSecond:F1} ms step");
        }));
    }

    private bool Measure(int frame, Action done)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (frame == 0)
        {
            _scriptMs.Clear();
            _stepMs.Clear();
        }
        else if (frame > Warmup)
        {
            _scriptMs.Add(_host.Main.Scripts.LastStepMilliseconds);
            // Wall time between our ticks: every node's physics work and the Jolt steps of both instances
            // (paced to real time unless Godot runs with --fixed-fps).
            _stepMs.Add(System.Diagnostics.Stopwatch.GetElapsedTime(_lastTick, now).TotalMilliseconds);
        }
        _lastTick = now;
        if (frame < Warmup + Measured)
            return false;
        done();
        return true;
    }

    private (double Script, double Step, double P99Script) Summary()
    {
        var sorted = _scriptMs.Order().ToList();
        return (_scriptMs.Average(), _stepMs.Average(), sorted[(int)(sorted.Count * 0.99)]);
    }

    private void Launch()
    {
        var glider = new Creation(Machine, _mods);
        var side = (int)Math.Ceiling(Math.Sqrt(Parts));
        for (var i = 0; i < Parts; i++)
        {
            if (!glider.TryPlacePart("bench:wing", new GridCell(i % side, 0, i / side), default, out _, out var error))
                throw new InvalidOperationException(error);
        }
        // Nose slightly up, flying along +X at 100 m.
        glider.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(0, 100, 0),
            System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, 0.08f)));
        glider.SetBodyVelocity(0, new BodyVelocity(new System.Numerics.Vector3(30, 0, 0), System.Numerics.Vector3.Zero));
        _host.Main.Creations.Add(glider, _host.Main.Assets);
    }

    private void Relaunch()
    {
        _host.Main.Creations.Remove(Machine);
        Launch();
    }

    private float HostY() => _host.Main.Creations.NodeOf(Machine)!.Bodies[0].GlobalPosition.Y;
}
