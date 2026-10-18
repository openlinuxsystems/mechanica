using Godot;
using Mechanica.Core.Building;
using Mechanica.Core.Characters;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Game.Building;
using Mechanica.Game.Creations;
using Mechanica.Game.Player;

namespace Mechanica.Game.Ui;

/// <summary>
/// The models <see cref="ItemIcons"/> draws tools from, so their hotbar and inventory pictures always match the models
/// used in game: the Sledgehammer's hand prop, turned to lie across the picture, the Connect Tool's hand prop on its
/// side with its lit display towards the camera, the Weld Tool's the same way with its silver side panel and open
/// clamping arms towards the camera, and the Lift's own model put down (as its <c>deploy</c> clip starts:
/// folded, the way it is carried). The same picture serves the hotbar and the Fabricator. Other tools have none and draw as
/// their initials.
/// </summary>
public sealed class ToolIconModels(CharacterAssets characters, PartAssets parts, LiftSettings lift)
{
    // The Sledgehammer's handle runs along its prop's Z axis, head towards -Z and broad along X. Turned so that, seen
    // from the icon camera, it lies corner to corner with the head top right, its broad side towards the camera.
    private static readonly Basis SledgeTilt = Diagonal();

    // The Connect Tool's probe runs along its prop's X axis raked up (towards -Z) by ConnectRake degrees, its display
    // faces -Y. Turned so the display faces the camera and the probe points up and to the left, the grip below.
    private const float ConnectRake = 14f;
    private static readonly Basis ConnectSide = Side(ConnectRake);

    // The Weld Tool is modelled the same way, its grip raked WeldRake degrees, its left side towards -Y.
    private const float WeldRake = 12f;
    private static readonly Basis WeldSide = Side(WeldRake);

    private static Basis Diagonal()
    {
        var forward = -ItemIcons.ViewFrom.Normalized();
        var right = forward.Cross(Vector3.Up).Normalized();
        var head = (right + right.Cross(forward)).Normalized();
        var across = forward.Cross(head).Normalized();
        var z = -head;
        return new Basis(across, z.Cross(across), z);
    }

    private static Basis Side(float rakeDegrees)
    {
        var forward = -ItemIcons.ViewFrom.Normalized();
        var right = forward.Cross(Vector3.Up).Normalized();
        var up = right.Cross(forward);
        var facing = -forward;
        var probe = (-right + 0.45f * up).Normalized();
        var rake = Mathf.DegToRad(rakeDegrees);
        var x = Mathf.Cos(rake) * probe - Mathf.Sin(rake) * probe.Cross(facing);
        var top = x.Cross(facing);
        return new Basis(x, -facing, -top).Orthonormalized();
    }

    /// <summary>A new, posed copy of <paramref name="tool"/>'s model, or null when it has none.</summary>
    public Node3D? For(string tool) => Identifiers.Qualify(tool) switch
    {
        HotbarTools.Sledge => Prop(CharacterProp.Sledgehammer, SledgeTilt, "Sledgehammer"),
        HotbarTools.Lift => Lift(),
        HotbarTools.Connect => Prop(CharacterProp.ConnectTool, ConnectSide, "ConnectTool"),
        HotbarTools.Weld => Prop(CharacterProp.WeldTool, WeldSide, "WeldTool"),
        _ => null,
    };

    private Node3D? Prop(CharacterProp which, Basis pose, string name)
    {
        if (characters.Prop(which) is not { Node: { } prop })
            return null;
        prop.Transform = new Transform3D(pose, Vector3.Zero);
        var root = new Node3D { Name = name };
        root.AddChild(prop);
        return root;
    }

    private Node3D? Lift() => LiftBody.StillModel(parts, lift, deployed: false);
}
