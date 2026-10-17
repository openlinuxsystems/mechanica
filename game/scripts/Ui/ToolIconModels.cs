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
/// used in game: the Sledgehammer's hand prop, turned to lie across the picture, and the Lift's own model put down
/// (as its <c>deploy</c> clip starts: folded, the way it is carried). Other tools have none and draw as
/// their initials.
/// </summary>
public sealed class ToolIconModels(CharacterAssets characters, PartAssets parts, LiftSettings lift)
{
    // The Sledgehammer's handle runs along its prop's Z axis, head towards -Z and broad along X. Turned so that, seen
    // from the icon camera, it lies corner to corner with the head top right, its broad side towards the camera.
    private static readonly Basis SledgeTilt = Diagonal();

    private static Basis Diagonal()
    {
        var forward = -ItemIcons.ViewFrom.Normalized();
        var right = forward.Cross(Vector3.Up).Normalized();
        var head = (right + right.Cross(forward)).Normalized();
        var across = forward.Cross(head).Normalized();
        var z = -head;
        return new Basis(across, z.Cross(across), z);
    }

    /// <summary>A new, posed copy of <paramref name="tool"/>'s model, or null when it has none.</summary>
    public Node3D? For(string tool) => Identifiers.Qualify(tool) switch
    {
        HotbarTools.Sledge => Sledgehammer(),
        HotbarTools.Lift => Lift(),
        _ => null,
    };

    private Node3D? Sledgehammer()
    {
        if (characters.Prop(CharacterProp.Sledgehammer) is not { Node: { } prop })
            return null;
        prop.Transform = new Transform3D(SledgeTilt, Vector3.Zero);
        var root = new Node3D { Name = "Sledgehammer" };
        root.AddChild(prop);
        return root;
    }

    private Node3D? Lift() => LiftBody.StillModel(parts, lift, deployed: false);
}
