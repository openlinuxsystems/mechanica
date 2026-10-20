using System.Diagnostics;
using Godot;
using Mechanica.Core.Survival;
using Mechanica.Game.Tests;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tools;

/// <summary>
/// Frame-cost benchmark (<c>tools/perf-bench.sh</c>): a host and a client in one process on the generated world, with
/// <see cref="Cars"/> debug scrap cars parked round the spawn. After the world settles it times
/// <see cref="Frames"/> frames and prints the mean and 95th percentile of the process and physics-process steps (every
/// node's <c>_Process</c> and <c>_PhysicsProcess</c>, both instances together) and of whole frames, plus the bytes the
/// host sent. Run it with <c>--fixed-fps 60</c> so each frame does one physics tick and its times are real costs.
/// </summary>
public partial class PerfBench : Node
{
    /// <summary>Cars parked round the spawn.</summary>
    public const int Cars = 12;

    /// <summary>Frames timed.</summary>
    public static readonly int Frames = int.TryParse(System.Environment.GetEnvironmentVariable("PERF_FRAMES"), out var f) ? f : 600;

    private const int SettleFrames = 240;

    private NetTestHarness _harness = null!;
    private Instance _host = null!;
    private readonly List<double> _process = [];
    private readonly List<double> _physics = [];
    private readonly List<double> _frames = [];
    private readonly Stopwatch _clock = new();
    private int _frame;
    private bool _spawned;

    public override void _Ready()
    {
        _harness = new NetTestHarness { Name = "Harness", GameMode = GameMode.Creative };
        AddChild(_harness);
        _host = _harness.StartHost();
        _harness.StartClient("Client");
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (!_spawned)
        {
            if (_frame < 60 || _host.Main.Session.ReadyPeers.Count == 0)
                return;
            Spawn();
            _spawned = true;
            _frame = 0;
            return;
        }
        if (_frame < SettleFrames)
            return;
        if (_frame == SettleFrames)
        {
            SentBytes();
            _clock.Restart();
            return;
        }
        _process.Add(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000);
        _physics.Add(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000);
        _frames.Add(_clock.Elapsed.TotalMilliseconds);
        _clock.Restart();
        if (_process.Count < Frames)
            return;
        Report("process", _process);
        Report("physics", _physics);
        Report("frame", _frames);
        GD.Print($"RESULT host sent {SentBytes() / (Frames / 60.0) / 1024:F1} KiB/s to one client");
        StagedTest.QuitCleanly(this, 0);
        SetProcess(false);
    }

    private void Spawn()
    {
        var spawn = _host.Main.GetNode<Node3D>("SpawnPoint").GlobalPosition;
        var settings = Creations.DebugCarSpawner.LoadSettings();
        for (var i = 0; i < Cars; i++)
        {
            var angle = Mathf.Tau * i / Cars;
            var at = spawn + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 14f;
            Creations.DebugCarSpawner.Spawn(_host.Main, at, settings with { Offset = [0, 0, 0] });
        }
    }

    // Bytes the host sent since the last call.
    private double SentBytes() =>
        _host.Main.Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet ? enet.Host.PopStatistic(ENetConnection.HostStatistic.SentData) : 0;

    private static void Report(string what, List<double> times)
    {
        var sorted = times.Order().ToList();
        GD.Print($"RESULT {what}: mean {times.Average():F2} ms, p95 {sorted[(int)(sorted.Count * 0.95)]:F2} ms, max {sorted[^1]:F2} ms");
    }
}
