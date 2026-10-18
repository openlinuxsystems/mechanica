using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Core.Ui;
using Mechanica.Game.Creations;

namespace Mechanica.Game.Ui;

/// <summary>
/// Pictures of items for the hotbar and inventory (UI spec §3). A part with its own <c>icon</c> PNG uses it; any
/// other item is drawn once from its model, three-quarter view on a clear background, in an offscreen viewport, one
/// item a frame, and kept. Until a picture is ready (and with no display, where nothing renders) <see cref="For"/>
/// returns null and callers show the item's initials. Every picture is cropped, framed and outlined by <see cref="Finish"/>. Tools with a model (<see cref="ToolModel"/>) are drawn from it the
/// same way; the rest draw as their initials.
/// </summary>
public partial class ItemIcons : Node
{
    /// <summary>Pixels along each side of a picture.</summary>
    public const int Size = 96;

    /// <summary>Where the camera looks at a model from (it looks at the model's middle, with Y up).</summary>
    public static readonly Vector3 ViewFrom = new(2, 1.6f, 2);

    private readonly Dictionary<string, Texture2D?> _icons = [];
    private readonly Queue<string> _queue = new();
    private SubViewport? _viewport;
    private Node3D? _model;
    private string? _rendering;
    private int _wait;

    /// <summary>Item definitions and models. Set before adding to the tree.</summary>
    public IPartCatalog Catalog { get; init; } = null!;

    /// <summary>Where models come from. Set before adding to the tree.</summary>
    public PartAssets Assets { get; init; } = null!;

    /// <summary>A new, posed copy of a tool's model, or null when the tool has none (see <see cref="ToolIconModels"/>).</summary>
    public Func<string, Node3D?>? ToolModel { get; init; }

    /// <summary>Raised when a picture becomes ready.</summary>
    public event Action<string>? IconReady;

    private static bool CanRender => DisplayServer.GetName() != "headless";

    public override void _Ready()
    {
        if (!CanRender)
            return;
        _viewport = new SubViewport
        {
            Name = "IconStage", Size = new Vector2I(Size, Size), OwnWorld3D = true, TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        AddChild(_viewport);
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Current = true };
        _viewport.AddChild(camera);
        camera.LookAtFromPosition(ViewFrom, Vector3.Zero, Vector3.Up);
        var light = new DirectionalLight3D { LightEnergy = 1.2f };
        _viewport.AddChild(light);
        light.LookAtFromPosition(new Vector3(1, 2, 1.5f), Vector3.Zero, Vector3.Up);
        _viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment { AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.6f, BackgroundMode = Godot.Environment.BGMode.ClearColor },
        });
    }

    /// <summary>The picture of <paramref name="id"/>, or null while it is not ready or there is none.</summary>
    public Texture2D? For(string id)
    {
        if (_icons.TryGetValue(id, out var icon))
            return icon;
        if (HotbarTools.IsTool(id))
        {
            if (ToolModel is null)
                return _icons[id] = null;
        }
        else if (Catalog.FindDefinition(id) is not { } definition)
            return _icons[id] = null;
        else if (Assets.IconFor(id, definition) is { } own)
            return _icons[id] = own.GetImage() is { } picture && !picture.IsEmpty() ? ImageTexture.CreateFromImage(Finish(picture)) : own;
        if (!CanRender)
            return _icons[id] = null;
        if (!_queue.Contains(id) && _rendering != id)
            _queue.Enqueue(id);
        return null;
    }

    public override void _Process(double delta)
    {
        if (_viewport is null)
            return;
        if (_rendering is { } id)
        {
            // Two frames: one to draw, one for the texture to be read back.
            if (--_wait > 0)
                return;
            var image = _viewport.GetTexture().GetImage();
            _icons[id] = image is null || image.IsEmpty() || image.GetUsedRect().Size == Vector2I.Zero ? null : ImageTexture.CreateFromImage(Finish(image));
            _rendering = null;
            _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            IconReady?.Invoke(id);
            return;
        }
        if (!_queue.TryDequeue(out var next))
            return;
        if (!Stage(next))
        {
            _icons[next] = null;
            IconReady?.Invoke(next);
            return;
        }
        _rendering = next;
        _wait = 2;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
    }

    // Puts the item's model in the middle of the stage, sized to fill the picture. False when it has no model.
    private bool Stage(string id)
    {
        _model?.QueueFree();
        _model = null;
        var model = HotbarTools.IsTool(id) ? ToolModel?.Invoke(id) : PartModel(id);
        if (model is null)
            return false;
        _model = new Node3D();
        _viewport!.AddChild(_model);
        _model.AddChild(model);
        var camera = _viewport.GetCamera3D()!;
        if (!HotbarTools.IsTool(id))
        {
            if (Bounds(_model, Transform3D.Identity) is not { } bounds)
                return false;
            camera.Size = Mathf.Max(0.2f, bounds.Size.Length()) * 1.05f;
            _model.Position = -bounds.GetCenter();
            return true;
        }
        // Tools are long and thin: fitted to what the camera sees of them, not to their box, so they fill the picture.
        var view = camera.GlobalTransform.Basis;
        if (Bounds(_model, new Transform3D(view.Inverse(), Vector3.Zero)) is not { } seen)
            return false;
        camera.Size = Mathf.Max(0.2f, Mathf.Max(seen.Size.X, seen.Size.Y)) * 1.08f;
        _model.Position = -(view * seen.GetCenter());
        return true;
    }

    private Node3D? PartModel(string id)
    {
        if (Catalog.FindDefinition(id) is not { } definition)
            return null;
        var root = new Node3D();
        foreach (var mesh in Assets.ModelFor(id, definition).Meshes)
            root.AddChild(mesh.Instantiate());
        return root;
    }

    // The box around every mesh under root, in root's space turned by `view`, or null when there are none.
    private static Aabb? Bounds(Node3D root, Transform3D view)
    {
        Aabb? bounds = null;
        var toRoot = view * root.GlobalTransform.AffineInverse();
        foreach (var mesh in root.FindChildren("*", nameof(MeshInstance3D), owned: false).OfType<MeshInstance3D>())
        {
            if (!mesh.IsVisibleInTree())
                continue;
            var box = toRoot * mesh.GlobalTransform * mesh.GetAabb();
            bounds = bounds is { } b ? b.Merge(box) : box;
        }
        return bounds;
    }

    /// <summary>
    /// A picture as tiles show it (UI spec §0): cropped to the item, scaled so its largest side fills
    /// <see cref="IconFinish.Fill"/> of a <see cref="Size"/> square, centred, with the ink outline and contact shadow.
    /// </summary>
    public static Image Finish(Image source)
    {
        var image = (Image)source.Duplicate();
        if (image.IsCompressed())
            image.Decompress();
        image.Convert(Image.Format.Rgba8);
        var used = image.GetUsedRect();
        if (used.Size.X <= 0 || used.Size.Y <= 0)
            return image;
        var item = image.GetRegion(used);
        var scale = Size * IconFinish.Fill / Mathf.Max(used.Size.X, used.Size.Y);
        item.Resize(Mathf.Max(1, Mathf.RoundToInt(used.Size.X * scale)), Mathf.Max(1, Mathf.RoundToInt(used.Size.Y * scale)), Image.Interpolation.Lanczos);
        var framed = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        framed.BlitRect(item, new Rect2I(Vector2I.Zero, item.GetSize()), (new Vector2I(Size, Size) - item.GetSize()) / 2);
        return Image.CreateFromData(Size, Size, false, Image.Format.Rgba8, IconFinish.Apply(framed.GetData(), Size));
    }

    /// <summary>Short text standing in for a picture: the initials of <paramref name="name"/>.</summary>
    public static string Initials(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)],
            _ => $"{words[0][0]}{words[1][0]}",
        };
    }
}
