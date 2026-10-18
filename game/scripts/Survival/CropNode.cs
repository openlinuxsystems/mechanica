using Godot;
using Mechanica.Core;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;
using Mechanica.Game.Creations;

namespace Mechanica.Game.Survival;

/// <summary>
/// Shows a crop growing on a soil part (<see cref="SoilView"/>): a green shoot that grows taller with
/// each stage, and once ripe the produce on top in its own colour. It follows the soil's Body.
/// </summary>
public partial class CropNode : Node3D
{
    private static readonly StandardMaterial3D Leaf = new() { AlbedoColor = new Color(0.3f, 0.65f, 0.25f) };

    private MeshInstance3D _shoot = null!;
    private MeshInstance3D _fruit = null!;

    /// <summary>What the soil shows.</summary>
    public SoilView? View { get; private set; }

    public override void _Ready()
    {
        _shoot = new MeshInstance3D { Name = "Shoot", MaterialOverride = Leaf };
        AddChild(_shoot);
        _fruit = new MeshInstance3D { Name = "Fruit", Mesh = new SphereMesh { Radius = 0.06f, Height = 0.12f } };
        AddChild(_fruit);
    }

    /// <summary>Shows <paramref name="view"/>, colouring ripe produce from <paramref name="catalog"/>.</summary>
    public void Show(SoilView view, IPartCatalog catalog)
    {
        View = view;
        var height = 0.04f + 0.06f * view.Stage;
        _shoot.Mesh = new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.025f, Height = height };
        _shoot.Position = new Vector3(0, height / 2, 0);
        _fruit.Visible = view.Ripe;
        _fruit.Position = new Vector3(0, height + 0.03f, 0);
        var color = view.Crop is { } crop ? catalog.FindDefinition(crop)?.Visual?.Color : null;
        _fruit.MaterialOverride = new StandardMaterial3D { AlbedoColor = color is null ? new Color(0.8f, 0.25f, 0.3f) : Color.FromHtml(color) };
    }

    /// <summary>Stands on top of the soil part in <paramref name="node"/>'s Body; hidden when the part is gone.</summary>
    public void Follow(CreationNode? node, int partId)
    {
        var creation = node?.Creation;
        if (creation?.FindPart(partId) is not { } part || node!.PartNode(partId) is not { } partNode)
        {
            Visible = false;
            return;
        }
        GlobalTransform = partNode.GlobalTransform * new Transform3D(Basis.Identity, new Vector3(0, TopOf(partNode, creation, part), 0));
        Visible = true;
    }

    // The top of the part's model above its centre (a mound is lower than its cell), or the top of its cell.
    private static float TopOf(Node3D partNode, Creation creation, PartInstance part)
    {
        var top = float.NegativeInfinity;
        if (partNode.GetNodeOrNull<Node3D>("Model") is { } model)
        {
            foreach (var mesh in model.GetChildren().OfType<MeshInstance3D>())
                top = Math.Max(top, (mesh.Transform * mesh.GetAabb()).End.Y);
        }
        if (float.IsFinite(top))
            return top;
        var size = creation.DefinitionOf(part.Id).Size ?? GridSize.One;
        return Math.Abs(part.Rotation.Apply(new GridCell(size.X, size.Y, size.Z)).Y) * MechanicaInfo.GridSize / 2;
    }
}
