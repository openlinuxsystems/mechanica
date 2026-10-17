using Godot;
using Mechanica.Core;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.World;
using Mechanica.Scripting;
using Instance = Mechanica.Game.Tests.NetTestHarness.Instance;

namespace Mechanica.Game.Tests;

/// <summary>
/// Lua part scripts (chunk 8), host plus a client (D10). A Creation with a scripted hover thruster
/// rises to the script's target height on the host, pushing through <c>ApplyForce</c>, and the client
/// sees it there through state sync without running any script. The script is then rewritten while it
/// runs: the new code takes over within a reload poll and keeps the part's <c>state</c>; a broken
/// version is refused and the old code keeps hovering; a fixed one that switches the thrust off falls,
/// gets <c>on_collision</c> when it lands and measures the ground with a raycast. Uses a copy of the
/// fixture mod in <c>tests/scripting/mods</c>, so the test can rewrite the script.
/// </summary>
public partial class ScriptingTest : StagedTest
{
    private const string Mod = "script_test";
    private const string Script = Mod + ":scripts/hover.lua";
    private const int Hovering = 1;

    private NetTestHarness _harness = null!;
    private ModLoadResult _mods = null!;
    private string _modsRoot = null!;
    private Instance _host = null!;
    private Instance _client = null!;
    private int _hover;
    private double _stepsBeforeReload;

    protected override string Tag => "scripting";

    protected override int StageTimeoutFrames => 900;

    private string ScriptPath => Path.Combine(_modsRoot, Mod, "scripts", "hover.lua");

    private ScriptRuntime Runtime => _host.Main.Scripts.Runtime;

    public override void _Ready()
    {
        _modsRoot = Path.Combine(OS.GetUserDataDir(), "tests", $"scripting-{System.Environment.ProcessId}");
        CopyDirectory(ProjectSettings.GlobalizePath("res://tests/scripting/mods"), _modsRoot);
        _mods = ModLoader.LoadAll(_modsRoot);
        _harness = new NetTestHarness { Name = "Harness", WorldGen = WorldGenData.Flat };
        AddChild(_harness);
        _host = _harness.StartHost(mods: _mods);
        _client = _harness.StartClient("Client", mods: _mods);

        Add("the client joins", _ => _host.Main.PlayerSync.Players.Count == 2 && _client.Main.LocalPlayer is not null);
        Add("spawn a hover machine on the host", _ =>
        {
            Check(!_mods.HasErrors, $"fixture mod loads ({string.Join("; ", _mods.Diagnostics)})");
            var machine = new Creation(Hovering, _mods);
            Place(machine, $"{Mod}:block", GridCell.Zero);
            _hover = Place(machine, $"{Mod}:hover", new GridCell(0, 1, 0)).Id;
            machine.SetAllBodyPoses(new Pose(new System.Numerics.Vector3(4, 0.5f, 4), System.Numerics.Quaternion.Identity));
            _host.Main.Creations.Add(machine, _host.Main.Assets);
            return true;
        });
        Add("it rises to the script's height on the host and the client", frame =>
        {
            if (frame < 240)
                return false;
            Check(Runtime.Instances is [{ Fault: null, Script: Script }], $"the host runs one hover script ({Describe()})");
            Check(_client.Main.Scripts.Runtime.Instances.Count == 0, "the client runs no scripts");
            CheckHeight(3f, "first version");
            return true;
        });
        Add("rewriting the script reloads it in place", frame =>
        {
            if (frame == 0)
            {
                _stepsBeforeReload = State()["steps"]!.GetValue<double>();
                File.WriteAllText(ScriptPath, """
                    -- Version 2: hovers twice as high, keeping its step count.
                    function on_reload() state.target = 6; state.reloaded = true end
                    function on_update(dt)
                      state.steps = state.steps + 1
                      local p, v = part.position(), part.velocity()
                      local accel = -world.gravity().y + properties.stiffness * (state.target - p.y) - properties.damping * v.y
                      part.apply_force(vec3(0, creation.mass() * accel, 0))
                    end
                    """);
                File.SetLastWriteTimeUtc(ScriptPath, DateTime.UtcNow.AddSeconds(1));
            }
            if (frame < 300)
                return false;
            var state = State();
            Check(state["reloaded"]?.GetValue<bool>() == true, "on_reload ran");
            Check(Runtime.Instances is [{ Version: 2, Fault: null }], $"the part runs version 2 ({Describe()})");
            Check(state["steps"]!.GetValue<double>() > _stepsBeforeReload + 200, $"state survived the reload (steps {_stepsBeforeReload} → {state["steps"]})");
            CheckHeight(6f, "second version");
            return true;
        });
        Add("a broken rewrite is refused and the old code keeps running", frame =>
        {
            if (frame == 0)
            {
                File.WriteAllText(ScriptPath, "function on_update(dt) part.apply_force( end");
                File.SetLastWriteTimeUtc(ScriptPath, DateTime.UtcNow.AddSeconds(2));
            }
            if (frame < 90)
                return false;
            Check(Runtime.Messages.Any(m => m.Kind == ScriptMessageKind.Error && m.Text.Contains("previous version keeps running", StringComparison.Ordinal)),
                "the compile error is reported");
            Check(Runtime.Instances is [{ Version: 2, Fault: null }], "version 2 still runs");
            CheckHeight(6f, "after the broken rewrite");
            return true;
        });
        Add("a fixed rewrite drops it, and it feels the landing", frame =>
        {
            if (frame == 0)
            {
                File.WriteAllText(ScriptPath, """
                    -- Version 3: no thrust; count landings and measure the ground below.
                    function on_reload() state.hits = 0 end
                    function on_collision(other, impulse) state.hits = state.hits + 1; state.impulse = impulse end
                    function on_update(dt)
                      local p = part.position()
                      local hit = world.raycast(p, p - vec3(0, 20, 0))
                      state.ground = hit and hit.distance or -1
                    end
                    """);
                File.SetLastWriteTimeUtc(ScriptPath, DateTime.UtcNow.AddSeconds(3));
            }
            if (frame < 240)
                return false;
            var state = State();
            Check(Runtime.Instances is [{ Version: 3, Fault: null }], $"the part runs version 3 ({Describe()})");
            Check(state["hits"]!.GetValue<double>() >= 1 && state["impulse"]!.GetValue<double>() > 0,
                $"on_collision ran on landing (hits {state["hits"]}, impulse {state["impulse"]} N·s)");
            var ground = state["ground"]!.GetValue<double>();
            // The hover part sits 0.25 m up, its centre 0.375 m above the ground; the ray skips its own Creation.
            Check(ground is > 0.25 and < 0.5, $"the raycast found the ground {ground:F3} m below the part");
            Check(HostY() < 0.5f, $"the machine is back on the ground (y={HostY():F2})");
            return true;
        });
    }

    public override void _ExitTree()
    {
        try
        {
            Directory.Delete(_modsRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void CheckHeight(float target, string what)
    {
        var host = HostY();
        var client = _client.Main.Creations.NodeOf(Hovering)?.Bodies[0].GlobalPosition.Y ?? float.NaN;
        // The body origin is the grid origin (the block's centre); the script holds the hover part's centre.
        var expected = target - MechanicaInfo.GridSize;
        Check(Math.Abs(host - expected) < 0.1f, $"{what}: the host hovers at {host:F2} m (wants {expected:F2})");
        Check(Math.Abs(client - host) < 0.05f, $"{what}: the client shows it at {client:F2} m");
    }

    private float HostY() => _host.Main.Creations.NodeOf(Hovering)!.Bodies[0].GlobalPosition.Y;

    private System.Text.Json.Nodes.JsonNode State() => Runtime.ExportState(Hovering, _hover)!;

    private string Describe() => string.Join("; ", Runtime.Instances) + " | " + string.Join("; ", Runtime.Messages.TakeLast(3));

    private static PartInstance Place(Creation creation, string part, GridCell cell)
    {
        if (!creation.TryPlacePart(part, cell, default, out var placed, out var error))
            throw new InvalidOperationException(error);
        return placed;
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
