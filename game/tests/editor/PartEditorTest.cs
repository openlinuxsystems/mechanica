using Godot;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Game.Creations;
using Mechanica.Game.Editor;

namespace Mechanica.Game.Tests;

/// <summary>
/// Headless Part Editor test (chunk 5). Drives the editor screen the way a modder would: a new block
/// from a template, a CSG model (a box with a hole), colour and a variant, undo/redo, export into a
/// fresh mod, then loads that mod like the game does and drops the block onto a floor. Also covers glTF
/// import, sockets and the connection preview on an interactive part, reopening, renaming and help.
/// Exits 0 on success, 1 on failure.
/// </summary>
public partial class PartEditorTest : Node3D
{
    private const string Mod = "editor_test";
    private bool _failed;

    public override void _Ready() => _ = Run();

    private async Task Run()
    {
        try
        {
            await Steps();
        }
        catch (Exception e)
        {
            Check(false, $"unexpected {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
        GD.Print($"[editor] {(_failed ? "FAILED" : "OK")}");
        GetTree().Quit(_failed ? 1 : 0);
    }

    private async Task Steps()
    {
        var modsRoot = ProjectSettings.GlobalizePath("user://tests/editor_mods");
        if (Directory.Exists(modsRoot))
            Directory.Delete(modsRoot, recursive: true);
        Directory.CreateDirectory(modsRoot);
        var screen = new PartEditorScreen { ModsRoot = modsRoot };
        AddChild(screen);
        await Frames(1);

        Check(PartEditorScreen.TemplateNames.SequenceEqual(["block", "decor", "interactive", "item"]),
            $"templates are listed ({string.Join(", ", PartEditorScreen.TemplateNames)})");
        Check(screen.NewPart(Mod, "Bad Id", "x", "block") is not null, "a bad part id is refused");
        Check(screen.NewPart(Mod, "holed_block", "Holed Block", "block") is null, "a new block starts from the template");
        var doc = screen.Document!;
        Check(doc.Definition is { Category: PartCategory.Block, Size: { X: 1, Y: 1, Z: 1 } } && doc.IsDirty, "the template's data is used");
        Check(screen.Preview.Contacts.Count == 6 && screen.Preview.Contacts.All(c => c.Connection == ConnectionType.Structural),
            "a block connects rigidly on all six faces");
        Check(screen.ProblemsText.Contains("No problems"), $"a fresh block has no problems ({screen.ProblemsText})");

        // A 0.25 m box with a 0.1 m hole through it along Z.
        doc.AddPiece(ModelPiece.NewBox(new Vec3(0.25f, 0.25f, 0.25f)));
        doc.AddPiece(new ModelPiece { Kind = ModelPieceKind.Cylinder, Operation = CsgOperation.Subtract, Radius = 0.05f, Height = 0.3f, Rotation = new Vec3(90, 0, 0) });
        Check(ModelBuilder.UsesCsg(doc.Source) && screen.Preview.Model is CsgCombiner3D { } csg && csg.GetChildCount() == 2,
            "a subtract piece builds a CSG preview");
        doc.Apply("Colour", d => d with { Visual = d.Visual! with { Color = "#3060c0" } });
        doc.AddVariant(new ColorVariant { Id = "red", Name = "Red", Color = "#c03030" });
        doc.Apply("Mass", d => d with { Mass = 0 });
        Check(screen.ProblemsText.Contains("mass (kg) must be greater than 0"), "a bad value shows as a problem right away");
        Check(doc.Undo() && doc.Definition.Mass == 2, "undo restores the mass");
        Check(doc.Undo() && doc.Definition.Visual!.Variants is null, "undo removes the variant");
        Check(doc.Redo() && doc.Definition.Visual!.Variants!.Count == 1, "redo brings it back");

        var result = await screen.ExportAsync();
        Check(result is { Succeeded: true, QualifiedId: "editor_test:holed_block" },
            $"export succeeds ({string.Join("; ", result?.Diagnostics.Select(d => d.ToString()) ?? [])})");
        var modDir = Path.Combine(modsRoot, Mod);
        Check(File.Exists(Path.Combine(modDir, "mod.json")) && File.Exists(Path.Combine(modDir, "parts", "holed_block.json"))
            && File.Exists(Path.Combine(modDir, "meshes", "holed_block.glb")) && File.Exists(Path.Combine(modDir, "editor", "parts", "holed_block.json")),
            "export writes the manifest, part, baked mesh and editor source");
        Check(!doc.IsDirty && screen.ProblemsText.Contains("Exported editor_test:holed_block"), "the document is saved and says so");

        // Load the mod the way the game does and drop the block.
        var content = ModLoader.LoadAll(modsRoot);
        Check(!content.HasErrors && content.FindPart($"{Mod}:holed_block") is not null,
            $"the exported mod loads ({string.Join("; ", content.Diagnostics)})");
        var assets = new PartAssets(content);
        var model = assets.ModelFor($"{Mod}:holed_block", content.FindDefinition($"{Mod}:holed_block")!);
        var faces = model.Meshes.Sum(m => m.Mesh.GetFaces().Length / 3);
        Check(!model.IsPlaceholder && faces > 12, $"the baked mesh loads with the hole cut ({faces} triangles)");
        Check(model.Meshes[0].SurfaceMaterials?[0] is BaseMaterial3D tinted && tinted.AlbedoColor.B > tinted.AlbedoColor.R,
            "the blue colour tints it");
        var floor = new StaticBody3D { Position = new Vector3(0, -0.5f, 0) };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(20, 1, 20) } });
        AddChild(floor);
        var creation = new Creation(1, content);
        creation.TryPlacePart($"{Mod}:holed_block", GridCell.Zero, GridRotation.Identity, out _, out _);
        creation.SetAllBodyPoses(new Transform3D(Basis.Identity, new Vector3(0, 1, 0)).ToPose());
        var node = new CreationNode { Creation = creation, Assets = assets };
        AddChild(node);
        await PhysicsFrames(150);
        var y = node.Bodies[0].GlobalPosition.Y;
        Check(y is > 0.09f and < 0.14f, $"the block falls and rests on the floor (y={y:F3})");

        // Reopen the exported file.
        Check(screen.OpenPart(Path.Combine(modDir, "parts", "holed_block.json")) is null, "the exported part opens");
        Check(screen.Document!.Source.Pieces.Count == 2 && !screen.Document.IsDirty, "its model pieces come back from the editor source");

        // Import a glTF and use it as the only piece: the part points at the imported file.
        Check(screen.NewPart(Mod, "cube_prop", "Cube Prop", "decor") is null, "a decor part starts");
        var gltf = ProjectSettings.GlobalizePath("res://tests/creation/mods/runtime_test/meshes/cube.gltf");
        Check(screen.ImportMesh(gltf) is null && screen.Document!.Source.Pieces.Single().Mesh == "meshes/cube.gltf",
            "importing a glTF copies it into the mod and adds a mesh piece");
        Check(screen.Preview.Model?.GetChildCount() == 1, "the imported mesh shows in the preview");
        result = await screen.ExportAsync();
        Check(result is { Succeeded: true } && screen.Document.Definition.Mesh == "meshes/cube.gltf", "a single mesh is referenced, not baked");

        // Sockets on an interactive part decide where it connects.
        Check(screen.NewPart(Mod, "pivot", "Pivot", "interactive") is null, "an interactive part starts");
        var contacts = screen.Preview.Contacts.ToDictionary(c => c.Direction);
        Check(contacts[new GridCell(0, -1, 0)].Connection == ConnectionType.Structural && contacts[new GridCell(1, 0, 0)].Connection is null,
            "the template's base socket is the only connecting face");
        screen.Document!.AddSocket(SocketTools.SideSocket(GridSize.One, new GridCell(0, 1, 0), "top", SocketType.Rotational));
        screen.SelectSocket(1);
        contacts = screen.Preview.Contacts.ToDictionary(c => c.Direction);
        Check(contacts[new GridCell(0, 1, 0)] is { Connection: ConnectionType.Rotational, Socket: "top" }, "a rotational socket on top previews a joint");
        screen.Document.SetSocket(1, screen.Document.Definition.Sockets![1] with { Position = new Vec3(0.05f, 0.2f, 0), Normal = new Vec3(0.1f, 1, 0) });
        Check(screen.ProblemsText.Contains("outside the part's"), "a socket off the footprint is a problem");
        screen.Document.SetSocket(1, screen.Document.Definition.Sockets![1] with { Normal = new Vec3(0, 1, 0) });
        var (snapped, _) = SocketTools.SnapToFace(GridSize.One, new Vec3(0.05f, 0.2f, 0), new Vec3(0, 1, 0));
        Check(snapped == new Vec3(0.0625f, 0.125f, 0), "snapping puts the socket back on the top face");
        result = await screen.ExportAsync();
        Check(result is { Succeeded: false }, "export refuses the part while the socket is off the footprint");

        // Rename an exported part.
        Check(screen.OpenPart(Path.Combine(modDir, "parts", "holed_block.json")) is null, "reopen for rename");
        screen.Document!.Apply("Rename", d => d with { Id = "ring_block" });
        result = await screen.ExportAsync();
        Check(result is { Succeeded: true } && !File.Exists(Path.Combine(modDir, "parts", "holed_block.json"))
            && File.Exists(Path.Combine(modDir, "meshes", "ring_block.glb")), "renaming moves the part and re-bakes its mesh");
        Check(ModLoader.LoadAll(modsRoot).Parts.Keys.Order().SequenceEqual([$"{Mod}:cube_prop", $"{Mod}:ring_block"]),
            "the mod has exactly the exported parts");

        // A batch makes several parts, baked and exported the same way.
        var batchFile = Path.Combine(modDir, "editor", "batches", "pair.json");
        Directory.CreateDirectory(Path.GetDirectoryName(batchFile)!);
        File.WriteAllText(batchFile, """
            { "parts": [
              { "file": "parts/pair/pair_block.json", "template": "block", "set": { "name": "Pair Block" },
                "model": [{ "kind": "box", "size": [0.25, 0.25, 0.25] }] },
              { "file": "parts/pair/pair_wedge.json", "base": "pair_block", "set": { "name": "Pair Wedge", "mass": 1 },
                "model": [{ "kind": "wedge", "size": [0.25, 0.25, 0.25] }], "collision_from_model": true }
            ] }
            """);
        var batch = await screen.RunBatchAsync(batchFile);
        Check(batch is { Succeeded: true, Exports.Count: 2 } && screen.ProblemsText.Contains("exported 2 parts"),
            $"Run batch exports both parts ({string.Join("; ", batch.Diagnostics)})");
        Check(screen.Document?.Definition is { Id: "pair_wedge", Collision.Shape: CollisionShape.Convex } && !screen.Document.IsDirty,
            "the last part it made is open and saved");
        content = ModLoader.LoadAll(modsRoot);
        Check(!content.HasErrors && content.FindPart($"{Mod}:pair_wedge") is not null && File.Exists(Path.Combine(modDir, "meshes", "pair", "pair_wedge.glb")),
            $"the batch's parts load with their baked meshes ({string.Join("; ", content.Diagnostics)})");
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task PhysicsFrames(int count)
    {
        for (var i = 0; i < count; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(bool ok, string what)
    {
        GD.Print($"[editor] {(ok ? "PASS" : "FAIL")}: {what}");
        _failed |= !ok;
    }
}
