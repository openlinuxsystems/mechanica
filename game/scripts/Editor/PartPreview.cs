using Godot;
using Mechanica.Core;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Editor;
using Mechanica.Game.Creations;
using Mechanica.Game.Ui;

namespace Mechanica.Game.Editor;

/// <summary>
/// The Part Editor's 3D view: the part's model, its footprint grid, its collision shape as a
/// wireframe, its sockets as arrows, joint axes with their range of motion, the centre of mass, and the
/// connection preview on every face. It lives in its own
/// <see cref="SubViewport"/>; <see cref="OrbitBy"/> and <see cref="ZoomBy"/> move the camera around the part.
/// </summary>
public partial class PartPreview : Node3D
{
    private static readonly Color FootprintColor = UiTokens.Text with { A = 0.35f };
    private static readonly Color CollisionColor = UiTokens.Collision;
    private static readonly Color SelectedColor = UiTokens.GizmoSelected;
    private static readonly Color CentreOfMassColor = UiTokens.CentreOfMass;

    private Camera3D _camera = null!;
    private Node3D _content = null!;
    private float _yaw = 0.6f;
    private float _pitch = -0.45f;
    private float _distance = 1.4f;

    /// <summary>The model node the last <see cref="Show"/> built (a CSG combiner when the model cuts).</summary>
    public Node3D? Model { get; private set; }

    /// <summary>Whether the collision wireframe is drawn.</summary>
    public bool ShowCollision { get; set; } = true;

    /// <summary>Whether sockets are drawn.</summary>
    public bool ShowSockets { get; set; } = true;

    /// <summary>Whether joint axes and ranges, and the centre of mass, are drawn.</summary>
    public bool ShowJoints { get; set; } = true;

    /// <summary>Whether the connection preview tiles are drawn.</summary>
    public bool ShowConnections { get; set; } = true;

    /// <summary>The faces drawn by the last <see cref="Show"/>, for tests and the status line.</summary>
    public IReadOnlyList<FaceContact> Contacts { get; private set; } = [];

    public override void _Ready()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = UiTokens.PreviewBackground,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.55f, 0.55f, 0.6f),
            },
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 35, 0), LightEnergy = 1.1f });
        _camera = new Camera3D { Current = true, Near = 0.01f, Fov = 50 };
        AddChild(_camera);
        _content = new Node3D { Name = "Content" };
        AddChild(_content);
        UpdateCamera();
    }

    /// <summary>Rotates the camera around the part by radians.</summary>
    public void OrbitBy(float yaw, float pitch)
    {
        _yaw += yaw;
        _pitch = Mathf.Clamp(_pitch + pitch, -1.5f, 1.5f);
        UpdateCamera();
    }

    /// <summary>Moves the camera closer (factor below 1) or further (above 1).</summary>
    public void ZoomBy(float factor)
    {
        _distance = Mathf.Clamp(_distance * factor, 0.2f, 20f);
        UpdateCamera();
    }

    /// <summary>Frames a part of <paramref name="size"/>.</summary>
    public void Frame(GridSize size)
    {
        _distance = Mathf.Max(0.8f, size.Metres.Length * 2.2f);
        UpdateCamera();
    }

    /// <summary>
    /// Redraws everything for <paramref name="definition"/> and <paramref name="source"/>. The highlighted
    /// socket index is drawn in the selected-gizmo colour.
    /// </summary>
    public void Show(PartDefinition definition, PartSource source, PartAssets assets, string modId, int selectedSocket = -1)
    {
        foreach (var child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }

        var size = definition.Size ?? GridSize.One;
        Model = ModelBuilder.Build(source, path => assets.LoadMeshes(path, modId));
        if (source.Pieces.Count == 0)
        {
            // Nothing modelled yet: show the part as the game would, a placeholder or its existing mesh.
            var (shape, transform) = assets.BuildShape(definition, modId);
            var meshes = definition.Mesh is { } path ? assets.LoadMeshes(path, modId) : null;
            foreach (var mesh in meshes ?? [new PartMesh(PartAssets.PlaceholderMesh(definition, shape), transform)])
                Model.AddChild(new MeshInstance3D { Mesh = mesh.Mesh, Transform = mesh.Transform });
        }
        if (definition.Visual is { } visual)
            Tint(Model, visual, assets, modId);
        _content.AddChild(Model);

        if (definition.IsPlaceable)
            _content.AddChild(Lines("Footprint", FootprintLines(size), FootprintColor));
        if (ShowCollision && definition.IsPlaceable)
        {
            var (shape, transform) = assets.BuildShape(definition, modId);
            _content.AddChild(new MeshInstance3D
            {
                Name = "Collision",
                Mesh = shape.GetDebugMesh(),
                Transform = transform,
                MaterialOverride = Unshaded(CollisionColor, onTop: true),
            });
        }
        if (ShowSockets)
        {
            var sockets = definition.Sockets ?? [];
            for (var i = 0; i < sockets.Count; i++)
                _content.AddChild(SocketGizmo(sockets[i], i == selectedSocket));
        }
        if (ShowJoints)
        {
            foreach (var socket in definition.Sockets ?? [])
            {
                if (JointGizmo.FrameOf(socket) is { } frame)
                    _content.AddChild(JointGizmo.Mesh($"Joint_{socket.Id}", JointGizmo.RangeLines(frame), JointGizmo.ColorOf(frame.Type)));
            }
            if (definition.IsPlaceable)
                _content.AddChild(JointGizmo.Mesh("CentreOfMass", JointGizmo.CrossLines((definition.CentreOfMass ?? Vec3.Zero).ToGodot()), CentreOfMassColor));
        }
        Contacts = definition.IsPlaceable ? SocketTools.PreviewConnections(definition) : [];
        if (ShowConnections)
        {
            foreach (var contact in Contacts)
                _content.AddChild(FaceTile(size, contact));
        }
    }

    // Visual tint for the preview: the same materials the runtime builds.
    private static void Tint(Node3D model, VisualDefinition visual, PartAssets assets, string modId)
    {
        if (model is CsgShape3D csg)
        {
            csg.MaterialOverride = assets.VisualMaterial(visual, modId);
            return;
        }
        foreach (var instance in model.GetChildren().OfType<MeshInstance3D>())
        {
            var tinted = assets.ApplyVisual(new PartMesh(instance.Mesh, instance.Transform), visual, modId);
            for (var i = 0; i < (tinted.SurfaceMaterials?.Count ?? 0); i++)
                instance.SetSurfaceOverrideMaterial(i, tinted.SurfaceMaterials![i]);
        }
    }

    private void UpdateCamera()
    {
        if (_camera is null)
            return;
        var direction = new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch) * Vector3.Back;
        _camera.Position = direction * _distance;
        _camera.LookAt(Vector3.Zero, Vector3.Up);
    }

    private static Node3D SocketGizmo(SocketDefinition socket, bool selected)
    {
        var color = selected ? SelectedColor : SocketColor(socket.Type);
        var node = new Node3D { Name = $"Socket_{socket.Id}", Position = (socket.Position ?? Vec3.Zero).ToGodot() };
        node.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.018f, Height = 0.036f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = Unshaded(color, onTop: true),
        });
        var normal = (socket.Normal ?? new Vec3(0, 1, 0)).ToGodot();
        if (normal.LengthSquared() > 0)
        {
            normal = normal.Normalized();
            // Cylinders point along +Y; turn +Y onto the normal.
            var axis = Vector3.Up.Cross(normal);
            var basis = axis.LengthSquared() < 1e-6f
                ? (normal.Y < 0 ? new Basis(Vector3.Right, Mathf.Pi) : Basis.Identity)
                : new Basis(axis.Normalized(), Vector3.Up.AngleTo(normal));
            node.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0, BottomRadius = 0.012f, Height = 0.08f, RadialSegments = 10 },
                Transform = new Transform3D(basis, normal * 0.04f),
                MaterialOverride = Unshaded(color, onTop: true),
            });
        }
        return node;
    }

    /// <summary>The gizmo colour of each socket type.</summary>
    public static Color SocketColor(SocketType? type) => type switch
    {
        SocketType.Rotational => UiTokens.JointHinge,
        SocketType.Linear => UiTokens.JointSlider,
        SocketType.Spring => UiTokens.JointSpring,
        _ => UiTokens.JointRigid,
    };

    private static MeshInstance3D FaceTile(GridSize size, FaceContact contact)
    {
        var color = contact.Connection switch
        {
            ConnectionType.Structural => UiTokens.JointRigid with { A = 0.35f },
            null => UiTokens.NoConnection with { A = 0.25f },
            ConnectionType.Rotational => SocketColor(SocketType.Rotational) with { A = 0.45f },
            ConnectionType.Linear => SocketColor(SocketType.Linear) with { A = 0.45f },
            _ => SocketColor(SocketType.Spring) with { A = 0.45f },
        };
        var normal = contact.Direction.ToGodot();
        var centre = SocketTools.FaceCentre(size, contact.Cell, contact.Direction).ToGodot() + normal * 0.002f;
        // QuadMesh faces +Z; turn +Z onto the face normal.
        var basis = Basis.LookingAt(-normal, Mathf.Abs(normal.Y) > 0.5f ? Vector3.Forward : Vector3.Up);
        return new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = Vector2.One * MechanicaInfo.GridSize * 0.8f },
            Transform = new Transform3D(basis, centre),
            MaterialOverride = Unshaded(color, onTop: false),
        };
    }

    private static IEnumerable<(Vector3, Vector3)> FootprintLines(GridSize size)
    {
        var half = size.Metres.ToGodot() / 2;
        var g = MechanicaInfo.GridSize;
        // Cell lines on all six sides of the footprint box.
        for (var x = 0; x <= size.X; x++)
            for (var y = 0; y <= size.Y; y++)
                yield return (new Vector3(x * g, y * g, 0) - half, new Vector3(x * g, y * g, size.Z * g) - half);
        for (var x = 0; x <= size.X; x++)
            for (var z = 0; z <= size.Z; z++)
                yield return (new Vector3(x * g, 0, z * g) - half, new Vector3(x * g, size.Y * g, z * g) - half);
        for (var y = 0; y <= size.Y; y++)
            for (var z = 0; z <= size.Z; z++)
                yield return (new Vector3(0, y * g, z * g) - half, new Vector3(size.X * g, y * g, z * g) - half);
    }

    private static MeshInstance3D Lines(string name, IEnumerable<(Vector3 A, Vector3 B)> lines, Color color)
    {
        var mesh = new ImmediateMesh();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        foreach (var (a, b) in lines)
        {
            mesh.SurfaceAddVertex(a);
            mesh.SurfaceAddVertex(b);
        }
        mesh.SurfaceEnd();
        return new MeshInstance3D { Name = name, Mesh = mesh, MaterialOverride = Unshaded(color, onTop: false) };
    }

    private static StandardMaterial3D Unshaded(Color color, bool onTop) => new()
    {
        AlbedoColor = color,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = color.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        NoDepthTest = onTop,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
}
