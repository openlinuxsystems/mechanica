using Godot;
using Mechanica.Core;
using Mechanica.Scripting;

namespace Mechanica.Game.Tests;

/// <summary>
/// Headless smoke test. Checks Jolt is the active engine, the main scene loads,
/// a rigid body falls and comes to rest on a floor, and Core + Lua are reachable
/// from the game assembly. Exits 0 on success, 1 on failure.
/// </summary>
public partial class SmokeTest : Node3D
{
    private const int MaxFrames = 600;
    private const string MainScene = "res://scenes/Main.tscn";

    private RigidBody3D _box = null!;
    private int _frames;
    private bool _done;

    public override void _Ready()
    {
        Check(ProjectSettings.GetSetting("physics/3d/physics_engine").AsString() == "Jolt Physics",
            "physics engine is Jolt");
        Check(ResourceLoader.Load<PackedScene>(MainScene)?.Instantiate() is Main main && Free(main),
            "main scene instantiates");
        Check(MechanicaInfo.ToGridCell(1f) == 4, "Mechanica.Core is callable");
        Check(new LuaHost().Run("return 2 + 3") is 5.0, "Lua host runs a script");

        var floor = new StaticBody3D();
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(10, 1, 10) } });
        floor.Position = new Vector3(0, -0.5f, 0);
        AddChild(floor);

        _box = new RigidBody3D { Position = new Vector3(0, 3, 0), Mass = 10 };
        _box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = Vector3.One * 0.25f } });
        AddChild(_box);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done)
            return;
        _frames++;
        if (_box.Sleeping || _frames >= MaxFrames)
        {
            var y = _box.Position.Y;
            Check(y is > 0.1f and < 0.15f, $"box rests on the floor (y={y:F3} after {_frames} frames)");
            Finish(0);
        }
    }

    private static bool Free(Node node)
    {
        node.Free();
        return true;
    }

    private void Check(bool ok, string what)
    {
        GD.Print($"[smoke] {(ok ? "PASS" : "FAIL")}: {what}");
        if (!ok)
            Finish(1);
    }

    private void Finish(int code)
    {
        if (_done)
            return;
        _done = true;
        GD.Print($"[smoke] {(code == 0 ? "OK" : "FAILED")}");
        GetTree().Quit(code);
    }
}
