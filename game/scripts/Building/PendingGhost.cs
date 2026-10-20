using Godot;
using Mechanica.Core.Actions;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Building;

/// <summary>
/// What a tool shows the moment its player acts, before the host answers (UI spec §9, multiplayer prediction): a
/// copy of the preview left where the part goes. When the host takes the request the copy goes as the real part
/// arrives; when it refuses, the copy turns red and fades out over <see cref="Core.Building.PlacementSettings.RollbackSeconds"/>.
/// </summary>
public partial class PendingGhost : Node3D
{
    /// <summary>Seconds a ghost waits for an answer before it goes anyway.</summary>
    public const double TimeoutSeconds = 5;

    private readonly StandardMaterial3D _material = new()
    {
        AlbedoColor = UiTokens.Cyan with { A = 0.55f },
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    private double _age;

    /// <summary>True once the host refused and the ghost is fading out.</summary>
    public bool RollingBack { get; private set; }

    /// <summary>The ghost's colour now.</summary>
    public Color Colour => _material.AlbedoColor;

    /// <summary>Leaves a copy of every mesh under <paramref name="preview"/>, where it is now, under <paramref name="parent"/>.</summary>
    public static PendingGhost Leave(Node parent, Node3D preview)
    {
        var ghost = new PendingGhost { Name = "Pending", TopLevel = true };
        parent.AddChild(ghost);
        ghost.GlobalTransform = preview.GlobalTransform;
        // A preview is either a mesh itself or a node holding the part's meshes (the Lift's: its own and a captured Creation's).
        var meshes = new List<(MeshInstance3D, Transform3D)>();
        if (preview is MeshInstance3D self)
            meshes.Add((self, Transform3D.Identity));
        else
            Collect(preview, Transform3D.Identity, meshes);
        foreach (var (mesh, transform) in meshes)
        {
            ghost.AddChild(new MeshInstance3D
            {
                Mesh = mesh.Mesh, Transform = transform, MaterialOverride = ghost._material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        return ghost;
    }

    // The meshes under a node, placed relative to it; a hidden node below it hides its own.
    private static void Collect(Node node, Transform3D parent, List<(MeshInstance3D, Transform3D)> meshes)
    {
        foreach (var child in node.GetChildren().OfType<Node3D>().Where(c => c.Visible))
        {
            var here = parent * child.Transform;
            if (child is MeshInstance3D mesh)
                meshes.Add((mesh, here));
            Collect(child, here, meshes);
        }
    }

    /// <summary>Settles the ghost with the host's answer.</summary>
    public void Resolve(ActionResult result, double rollbackSeconds)
    {
        if (result.Ok)
        {
            QueueFree();
            return;
        }
        RollingBack = true;
        _material.AlbedoColor = UiTokens.Error with { A = 0.6f };
        var fade = CreateTween();
        fade.TweenProperty(_material, "albedo_color:a", 0f, Math.Max(0.01, rollbackSeconds));
        fade.TweenCallback(Callable.From(QueueFree));
    }

    public override void _Process(double delta)
    {
        _age += delta;
        if (_age > TimeoutSeconds && !RollingBack)
            QueueFree();
    }
}
