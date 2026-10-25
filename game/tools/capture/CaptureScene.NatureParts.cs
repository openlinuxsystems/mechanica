using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.World;
using Mechanica.Game.World;

namespace Mechanica.Game.Tools;

// Given `nature_parts`, the capture places the parts audit's Creative nature props (Boulder, Large Rock, Small Rock, Bush,
// Grass Tuft, Flower Patch, Tree Trunk, Fallen Log) in a row in a meadow in Creative, through the Action API, with the wild
// models they were built from in a row behind them, as the world draws them: the Square Rocks deposit blocks, the wild
// Bush's flora model, the meadow's tall grass and flowers, the Leafy tree, and the world's own Tree Trunk stump and Fallen
// Log; then shoots the rows together and each group close (tools/capture-nature-parts.sh).
public partial class CaptureScene
{
    // Each prop's part and its x along the row (metres), and what stands behind it: a world object (with its mesh variant)
    // or a flora model.
    private static readonly (string Part, float X, string Wild, int Variant)[] NatureRow =
    [
        ("boulder", -7.0f, "rock_outcrop_large", 0),
        ("large_rock", -4.6f, "rock_outcrop", 1),
        ("small_rock", -3.0f, "rock_outcrop_small", 0),
        ("bush", -1.4f, "flora:bush_berry", 0),
        ("grass_tuft", 0.3f, "flora:grass_tall", 0),
        ("flower_patch", 1.4f, "flora:flowers_purple", 0),
        ("tree_trunk", 3.0f, "tree_trunk", 0),
        ("fallen_log", 5.2f, "fallen_log", 0),
    ];

    private const float WildRow = -2.6f;

    private void NaturePartsShots()
    {
        Engine.TimeScale = 1.0;
        var wild = new List<Node3D>();
        Shot("nature_parts_00_wait", 60, () => { });
        Shot("nature_parts_00_go", 240, () =>
        {
            var g = _main.Streamer.Generator;
            Go(Find(p => new[] { (0f, 0f), (12f, 0f), (-12f, 0f), (0f, 12f), (0f, -12f) }.All(d => g.BiomeAt(p.X + d.Item1, p.Y + d.Item2).Id == "meadow")
                && g.SlopeAt(p.X, p.Y) < 0.05f && g.HeightAt(p.X, p.Y) > g.SeaLevel + 2f && !g.PoisAt(p.X, p.Y).Any()
                && !g.ObjectsIn(p.X - 10, p.Y - 10, p.X + 8, p.Y + 4).Any()));
            _main.Flora.BuildAll();
        });
        // One part per placement, a little above the ground (it settles): the grid origin is the centre of the part's first
        // cell, set so the footprint is centred on its spot along the row.
        foreach (var (part, x, _, _) in NatureRow)
        {
            Shot($"nature_parts_01_place_{part}", 20, () =>
            {
                var size = _main.Build.Catalog.FindDefinition($"vanilla:{part}")!.Size ?? Core.Data.GridSize.One;
                var cell = Core.MechanicaInfo.GridSize;
                var offset = new Vector3((size.X - 1) * cell / 2f, -cell / 2f - 0.12f, (size.Z - 1) * cell / 2f);
                Place($"vanilla:{part}", [GridCell.Zero], Ground(_meadow + new Vector3(x, 0, 0)) - offset);
            });
        }
        Shot("nature_parts_02_wild", 60, () =>
        {
            foreach (var (part, x, id, variant) in NatureRow)
            {
                if (WildModel(id, variant) is { } node)
                {
                    _main.AddChild(node);
                    node.GlobalPosition = Ground(_meadow + new Vector3(x, 0, WildRow));
                    wild.Add(node);
                }
                else
                {
                    GD.Print($"[capture] no wild model for {part} ({id})");
                }
            }
            // The tree the trunk and the log are cut from, further back.
            if (WildModel("leafy_tree", 0) is { } tree)
            {
                _main.AddChild(tree);
                tree.GlobalPosition = Ground(_meadow + new Vector3(4.2f, 0, -8.5f));
                wild.Add(tree);
            }
            _main.PlayerSync.HostTeleport(_main.Session.LocalPeer, Ground(_meadow + new Vector3(-10f, 0, 6f)) + Vector3.Up * 0.3f, 0f);
        });
        Shot("nature_parts_03_rows", 40, () => View(new Vector3(-0.8f, 3.4f, 9.5f), new Vector3(-0.8f, 0.6f, -1.6f)));
        Shot("nature_parts_04_rows_left", 30, () => View(new Vector3(-10.5f, 2.6f, 6.5f), new Vector3(-1.5f, 0.5f, -1.4f)));
        Shot("nature_parts_05_rocks", 30, () => View(new Vector3(-5.6f, 1.7f, 4.0f), new Vector3(-5.0f, 0.4f, -1.3f)));
        Shot("nature_parts_06_flora", 30, () => View(new Vector3(0.0f, 1.3f, 3.2f), new Vector3(0.0f, 0.3f, -1.3f)));
        Shot("nature_parts_07_wood", 30, () => View(new Vector3(4.6f, 2.4f, 5.6f), new Vector3(4.1f, 0.9f, -1.6f)));
        Shot("nature_parts_08_tree", 30, () => View(new Vector3(8.5f, 3.2f, 8.5f), new Vector3(3.8f, 2.4f, -4.5f)));
        Shot("nature_parts_09_done", 2, () => { foreach (var node in wild) node.QueueFree(); });
    }

    // A wild model as the world draws it: an object's mesh (the given variant), or a flora model (`flora:<id>`) with its
    // kind's flora material and the world's tint.
    private Node3D? WildModel(string id, int variant)
    {
        if (id.StartsWith("flora:", StringComparison.Ordinal))
        {
            if (FloraModels.Find(id["flora:".Length..]) is not { } model)
                return null;
            var multi = FloraField.New(FloraMaterials.Meshes(model).Full, 1);
            multi.SetInstanceTransform(0, Transform3D.Identity);
            multi.SetInstanceColor(0, Colors.White);
            var tint = (model.Kind == FloraKind.Bush ? new Color(0.36f, 0.68f, 0.24f) : model.Kind == FloraKind.Grass ? new Color("#5a9a3a") : new Color("#78c23e")).SrgbToLinear();
            multi.SetInstanceCustomData(0, new Color(tint.R, tint.G, tint.B, 0.3f));
            return new MultiMeshInstance3D { Name = id, Multimesh = multi, MaterialOverride = FloraMaterials.For(model.Kind) };
        }
        var data = _main.Streamer.Generator.Data;
        if (data.Definition.Objects!.FirstOrDefault(o => o.Id == id) is not { } definition || data.MeshFileOf(definition, variant) is not { } file)
            return null;
        var root = new Node3D { Name = id };
        // The mesh sits at its offset from the object's ground point (a deposit block's centre is half its height up).
        var at = new Transform3D(Basis.Identity, definition.Mesh!.At.ToGodot());
        foreach (var mesh in Creations.PartAssets.ReadGltf(file, file) ?? [])
        {
            if (mesh.Name?.StartsWith("collider", StringComparison.Ordinal) != true)
                root.AddChild(new MeshInstance3D { Mesh = mesh.Mesh, Transform = at * mesh.Transform });
        }
        return root;
    }
}
