using Godot;
using Mechanica.Core.Creations;
using Mechanica.Game.Creations;

namespace Mechanica.Game.Ui;

/// <summary>
/// Pictures of whole Creations for the blueprint menu (UI spec §5): the Creation's parts, three-quarter view on a
/// clear background, drawn in an offscreen viewport and saved as a PNG. One at a time; with no display nothing is
/// drawn and <see cref="Take"/> says so.
/// </summary>
public partial class CreationSnapshots : Node
{
    /// <summary>Picture size in pixels (16:9).</summary>
    public static readonly Vector2I Size = new(320, 180);

    private readonly Queue<(Node3D Model, Aabb Bounds, string File, Action<bool>? Done)> _queue = new();
    private SubViewport? _viewport;
    private (Node3D Model, string File, Action<bool>? Done)? _rendering;
    private int _wait;

    /// <summary>Where part models come from. Set before adding to the tree.</summary>
    public PartAssets Assets { get; init; } = null!;

    /// <summary>True where pictures can be drawn (not headless).</summary>
    public static bool CanRender => DisplayServer.GetName() != "headless";

    public override void _Ready()
    {
        if (!CanRender)
            return;
        _viewport = new SubViewport
        {
            Name = "SnapshotStage", Size = Size, OwnWorld3D = true, TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(_viewport);
        _viewport.AddChild(new Camera3D { Name = "Camera", Projection = Camera3D.ProjectionType.Orthogonal, Current = true });
        var light = new DirectionalLight3D { LightEnergy = 1.2f };
        _viewport.AddChild(light);
        light.LookAtFromPosition(new Vector3(1, 2, 1.5f), Vector3.Zero, Vector3.Up);
        _viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.6f,
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
            },
        });
    }

    /// <summary>
    /// Draws <paramref name="creation"/> (its Bodies as they are now) and saves the picture to <paramref name="file"/>;
    /// <paramref name="done"/> hears whether it was written. False at once where nothing can be drawn.
    /// </summary>
    public bool Take(Creation creation, string file, Action<bool>? done = null)
    {
        if (_viewport is null || creation.Parts.Count == 0)
            return false;
        var model = new Node3D { Name = "Snapshot" };
        var first = creation.BodyPose(0).ToTransform().AffineInverse();
        var bounds = new Aabb();
        var any = false;
        foreach (var part in creation.Parts)
        {
            var definition = creation.DefinitionOf(part.Id);
            var body = first * creation.BodyPose(creation.BodyIndexOf(part.Id)).ToTransform();
            var placed = body * new Transform3D(part.Rotation.ToBasis(), PartGeometry.CentreMetres(definition, part.Cell, part.Rotation).ToGodot());
            foreach (var mesh in Assets.ModelFor(part.Part, definition).Meshes)
            {
                var instance = mesh.Instantiate();
                instance.Transform = placed * instance.Transform;
                if (part.Color is { } color)
                    instance.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(color) };
                model.AddChild(instance);
                var box = instance.Transform * instance.GetAabb();
                bounds = any ? bounds.Merge(box) : box;
                any = true;
            }
        }
        _queue.Enqueue((model, bounds, file, done));
        return true;
    }

    public override void _Process(double delta)
    {
        if (_viewport is null)
            return;
        if (_rendering is { } job)
        {
            // Two frames: one to draw, one for the texture to be read back.
            if (--_wait > 0)
                return;
            var image = _viewport.GetTexture().GetImage();
            var ok = image is not null && !image.IsEmpty() && image.SavePng(job.File) == Error.Ok;
            job.Model.QueueFree();
            _rendering = null;
            _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            job.Done?.Invoke(ok);
            return;
        }
        if (!_queue.TryDequeue(out var next))
            return;
        _viewport.AddChild(next.Model);
        next.Model.Position = -next.Bounds.GetCenter();
        var camera = _viewport.GetNode<Camera3D>("Camera");
        var reach = Mathf.Max(0.3f, next.Bounds.Size.Length());
        camera.Size = reach * 0.75f;
        camera.LookAtFromPosition(new Vector3(1, 0.8f, 1).Normalized() * reach * 2, Vector3.Zero, Vector3.Up);
        _rendering = (next.Model, next.File, next.Done);
        _wait = 2;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
    }
}
