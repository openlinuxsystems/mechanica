using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Game.Creations;

namespace Mechanica.Game.Tests;

/// <summary>
/// Headless Creation runtime test. Loads the fixture mod in <c>tests/creation/mods</c> (hidden from the
/// Godot importer by a <c>.gdignore</c>), checks runtime glTF and shape loading, then spawns Creations
/// from the graph and checks they fall, collide, keep their hinge together, freeze on non-authority
/// peers, save their poses and rebuild after graph changes. Exits 0 on success, 1 on failure.
/// </summary>
public partial class CreationTest : Node3D
{
    private const int StageTimeoutFrames = 900;
    private const string Mod = "runtime_test";

    private readonly Queue<Stage> _stages = new();
    private Stage? _stage;
    private int _stageFrames;
    private bool _done;

    private ModLoadResult _content = null!;
    private PartAssets _assets = null!;
    private CreationNode _machine = null!;
    private CreationNode _dropped = null!;
    private CreationNode _frozen = null!;
    private int _rotorBlock;
    private Transform3D _mark;

    private sealed record Stage(string Name, Func<int, bool> Tick);

    public override void _Ready()
    {
        _content = ModLoader.LoadAll(ProjectSettings.GlobalizePath("res://tests/creation/mods"));
        Check(!_content.HasErrors && _content.Parts.Count == 4,
            $"fixture mod loads ({_content.Parts.Count} parts; {string.Join("; ", _content.Diagnostics.Select(d => d.Message))})");
        _assets = new PartAssets(_content);
        CheckAssets();

        var floor = new StaticBody3D { Name = "Floor", Position = new Vector3(0, -0.5f, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(100, 1, 100) } });
        AddChild(floor);

        // A row of blocks, heavy at one end, with a bearing on top carrying one block on its rotor.
        var machine = NewCreation(1);
        machine.Place("block", new GridCell(-1, 0, 0));
        machine.Place("block", GridCell.Zero);
        machine.Place("block", new GridCell(1, 0, 0));
        machine.Place("heavy_block", new GridCell(2, 0, 0));
        machine.Place("bearing", new GridCell(0, 1, 0));
        _rotorBlock = machine.Place("block", new GridCell(0, 2, 0)).Id;
        var spawn = new Transform3D(new Basis(Vector3.Up, 0.4f), new Vector3(0, 1.5f, 0));
        machine.SetAllBodyPoses(spawn.ToPose());
        _machine = Spawn(machine);
        CheckBuilt();

        // A knob on a block, dropped onto the machine's end block.
        var dropped = NewCreation(2);
        dropped.Place("block", GridCell.Zero);
        dropped.Place("knob", new GridCell(0, 1, 0));
        dropped.SetAllBodyPoses((spawn * new Transform3D(Basis.Identity, new Vector3(-0.25f, 1.25f, 0))).ToPose());
        _dropped = Spawn(dropped);

        // Owned by another peer, so this peer must not simulate it.
        var frozen = NewCreation(3, authorityPeer: 2);
        frozen.Place("block", GridCell.Zero);
        frozen.SetAllBodyPoses(new Transform3D(Basis.Identity, new Vector3(10, 2, 0)).ToPose());
        _frozen = Spawn(frozen);

        Add("creations fall and settle", frame =>
        {
            var bodies = _machine.Bodies.Concat(_dropped.Bodies).ToList();
            if (frame < 60 || (frame < 600 && !bodies.All(b => b.Sleeping)))
                return false;
            var main = _machine.Bodies[0].GlobalTransform;
            // Jolt lets resting contacts sink up to its 0.02 m penetration slop.
            Check(main.Origin.Y is > 0.1f and < 0.15f, $"machine rests on the floor (grid frame y={main.Origin.Y:F3}, bottom at {main.Origin.Y - 0.125f:F3})");
            Check(main.Basis.Y.Dot(Vector3.Up) > 0.99f, $"machine lands upright (up={main.Basis.Y})");
            var anchor = new Vector3(0, 0.375f, 0);
            var gap = (main * anchor).DistanceTo(_machine.Bodies[1].GlobalTransform * anchor);
            Check(gap < 0.02f, $"hinge keeps the rotor block on the bearing (gap={gap:F4} m)");
            var y = _dropped.Bodies[0].GlobalPosition.Y;
            Check(y is > 0.33f and < 0.42f, $"dropped creation lands on the machine, not through it (y={y:F3})");
            var frozenY = _frozen.Bodies[0].GlobalPosition.Y;
            Check(!_frozen.IsSimulated && Mathf.IsEqualApprox(frozenY, 2f), $"another peer's creation is not simulated here (y={frozenY:F3})");
            return true;
        });
        Add("poses save and load", _ =>
        {
            _machine.SyncToCreation();
            var json = CreationJson.Write(_machine.Creation);
            var diagnostics = new List<DataDiagnostic>();
            var loaded = CreationJson.Read(json, _content, "machine.json", diagnostics);
            Check(loaded is not null && diagnostics.Count == 0, $"saved machine loads ({string.Join("; ", diagnostics.Select(d => d.Message))})");
            var copy = new CreationNode { Creation = loaded!, Assets = _assets };
            AddChild(copy);
            for (var i = 0; i < _machine.Bodies.Count; i++)
            {
                var (a, b) = (_machine.Bodies[i].GlobalTransform, copy.Bodies[i].GlobalTransform);
                Check(a.Origin.DistanceTo(b.Origin) < 1e-4f && a.Basis.GetRotationQuaternion().AngleTo(b.Basis.GetRotationQuaternion()) < 1e-4f,
                    $"body {i} spawns where it was saved ({b.Origin})");
            }
            RemoveChild(copy);
            copy.Free();
            return true;
        });
        Add("remove the rotor block and rebuild", frame =>
        {
            if (frame == 0)
            {
                _mark = _machine.Bodies[0].GlobalTransform;
                Check(_machine.Creation.RemovePart(_rotorBlock, () => 99).SplitOff.Count == 0, "removing the rotor block splits nothing");
                _machine.Rebuild();
                Check(_machine.Bodies.Count == 1 && _machine.Joints.Count == 0, "rebuild leaves one body and no joint");
                Check(Mathf.IsEqualApprox(_machine.Bodies[0].Mass, 55f), $"rebuilt body mass is the parts' sum ({_machine.Bodies[0].Mass} kg)");
                Check(_machine.Bodies[0].GlobalTransform.Origin.DistanceTo(_mark.Origin) < 1e-4f, "rebuilt body keeps its pose");
            }
            if (frame < 30)
                return false;
            var y = _machine.Bodies[0].GlobalPosition.Y;
            Check(y is > 0.1f and < 0.15f, $"rebuilt body still rests on the floor (y={y:F3})");
            return true;
        });
        Add("place a new rotor block and rebuild", frame =>
        {
            if (frame == 0)
            {
                _mark = _machine.Bodies[0].GlobalTransform;
                Check(_machine.Creation.TryPlacePart($"{Mod}:block", new GridCell(0, 2, 0), GridRotation.Identity, out _, out var error), error ?? "rotor block placed");
                _machine.Rebuild();
                Check(_machine.Bodies.Count == 2 && _machine.Joints.Count == 1, "rebuild adds the rotor body and its hinge");
                var gap = _machine.Bodies[1].GlobalTransform.Origin.DistanceTo(_mark.Origin);
                Check(gap < 1e-3f, $"the new rotor body starts in the machine's frame (off by {gap:F4} m)");
            }
            return frame >= 30;
        });
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done)
            return;
        if (_stage is null)
        {
            if (!_stages.TryDequeue(out _stage))
            {
                Finish(0);
                return;
            }
            _stageFrames = 0;
        }

        if (_stage.Tick(_stageFrames))
        {
            GD.Print($"[creation] done: {_stage.Name} ({_stageFrames} frames)");
            _stage = null;
        }
        else if (++_stageFrames > StageTimeoutFrames)
        {
            Check(false, $"stage '{_stage.Name}' timed out");
        }
    }

    private void CheckAssets()
    {
        var block = Model("block");
        var size = block.Meshes.Count == 1 ? block.Meshes[0].Mesh.GetAabb().Size : Vector3.Zero;
        Check(!block.IsPlaceholder && size.IsEqualApprox(Vector3.One * 0.25f), $"block mesh loads from glTF at runtime (size {size})");
        Check(block.Shape is BoxShape3D { Size: var box } && box.IsEqualApprox(Vector3.One * 0.25f), "block without collision data gets a footprint box");
        var heavy = Model("heavy_block");
        Check(heavy.IsPlaceholder, "part without a mesh gets a placeholder");
        Check(heavy.Meshes[0].SurfaceMaterials?.FirstOrDefault() is StandardMaterial3D { Roughness: 0.25f, Metallic: 1f } tinted
            && tinted.AlbedoColor.IsEqualApprox(new Color(0.62f, 0.62f * 128 / 255f, 0, 1)),
            "visual colour tints the model and sets its surface");
        Check(Model("knob").Shape is ConvexPolygonShape3D { Points.Length: 8 }, "convex collision is built from the glTF mesh");
        Check(Model("bearing").Shape is CylinderShape3D { Radius: 0.125f, Height: 0.25f }, "cylinder collision comes from part data");
        Check(ReferenceEquals(Model("block"), block), "models are cached per part");
        Check(_assets.LoadMeshes("meshes/../../escape.gltf", Mod) is null, "asset paths cannot leave the mod folder");
    }

    private void CheckBuilt()
    {
        var core = _machine.Creation;
        Check(_machine.Bodies.Count == 2 && core.Bodies.Count == 2, $"machine builds one rigid body per Core body ({_machine.Bodies.Count})");
        Check(_machine.Joints is [HingeJoint3D], "the bearing becomes a hinge joint");
        for (var i = 0; i < core.Bodies.Count; i++)
        {
            var (node, body) = (_machine.Bodies[i], core.Bodies[i]);
            Check(Mathf.IsEqualApprox(node.Mass, body.Mass) && node.CenterOfMass.IsEqualApprox(body.CentreOfMass.ToGodot()),
                $"body {i} has the Core mass and centre of mass ({node.Mass} kg at {node.CenterOfMass})");
            Check(node.GetChildren().OfType<CollisionShape3D>().Count() == body.PartIds.Count, $"body {i} has one shape per part");
        }
        // Main body: 4 + 4 + 4 + 40 + 3 kg.
        Check(Mathf.IsEqualApprox(_machine.Bodies[0].Mass, 55f), "main body mass sums its parts");
        var hingeAxis = _machine.Joints[0].GlobalBasis.Z;
        Check(hingeAxis.IsEqualApprox(Vector3.Up), $"hinge turns about the bearing's rotor normal ({hingeAxis})");
    }

    private Creation NewCreation(int id, int authorityPeer = Creation.HostPeer) => new(id, _content, authorityPeer);

    private CreationNode Spawn(Creation creation)
    {
        var node = new CreationNode { Creation = creation, Assets = _assets };
        AddChild(node);
        return node;
    }

    private PartModel Model(string id) => _assets.ModelFor($"{Mod}:{id}", _content.FindDefinition($"{Mod}:{id}")!);

    private void Add(string name, Func<int, bool> tick) => _stages.Enqueue(new Stage(name, tick));

    private void Check(bool ok, string what)
    {
        GD.Print($"[creation] {(ok ? "PASS" : "FAIL")}: {what}");
        if (!ok)
            Finish(1);
    }

    private void Finish(int code)
    {
        if (_done)
            return;
        _done = true;
        GD.Print($"[creation] {(code == 0 ? "OK" : "FAILED")}");
        GetTree().Quit(code);
    }
}

internal static class CreationTestExtensions
{
    /// <summary>Places a fixture-mod part or throws.</summary>
    public static PartInstance Place(this Creation creation, string part, GridCell cell) =>
        creation.TryPlacePart($"runtime_test:{part}", cell, GridRotation.Identity, out var placed, out var error)
            ? placed
            : throw new InvalidOperationException(error);
}
