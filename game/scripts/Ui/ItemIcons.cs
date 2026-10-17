using Godot;
using Mechanica.Core.Data;
using Mechanica.Core.Items;
using Mechanica.Game.Creations;

namespace Mechanica.Game.Ui;

/// <summary>
/// Pictures of items for the hotbar and inventory (UI spec §3). A part with its own <c>icon</c> PNG uses it; any
/// other item is drawn once from its model, three-quarter view on a clear background, in an offscreen viewport, one
/// item a frame, and kept. Until a picture is ready (and with no display, where nothing renders) <see cref="For"/>
/// returns null and callers show the item's initials. Tools draw as their initials.
/// </summary>
public partial class ItemIcons : Node
{
    /// <summary>Pixels along each side of a picture.</summary>
    public const int Size = 96;

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
        camera.LookAtFromPosition(new Vector3(2, 1.6f, 2), Vector3.Zero, Vector3.Up);
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
        if (HotbarTools.IsTool(id) || Catalog.FindDefinition(id) is not { } definition)
            return _icons[id] = null;
        if (Assets.IconFor(id, definition) is { } own)
            return _icons[id] = own;
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
            _icons[id] = image is null || image.IsEmpty() ? null : ImageTexture.CreateFromImage(image);
            _rendering = null;
            _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            IconReady?.Invoke(id);
            return;
        }
        if (!_queue.TryDequeue(out var next) || Catalog.FindDefinition(next) is not { } definition)
            return;
        Stage(next, definition);
        _rendering = next;
        _wait = 2;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
    }

    // Puts the item's model in the middle of the stage, sized to fill the picture.
    private void Stage(string id, PartDefinition definition)
    {
        _model?.QueueFree();
        _model = new Node3D();
        _viewport!.AddChild(_model);
        var bounds = new Aabb();
        var first = true;
        foreach (var mesh in Assets.ModelFor(id, definition).Meshes)
        {
            var instance = mesh.Instantiate();
            _model.AddChild(instance);
            var box = instance.Transform * instance.GetAabb();
            bounds = first ? box : bounds.Merge(box);
            first = false;
        }
        var camera = _viewport.GetCamera3D()!;
        camera.Size = Mathf.Max(0.2f, bounds.Size.Length()) * 1.05f;
        _model.Position = -bounds.GetCenter();
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
