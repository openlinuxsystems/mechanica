using Godot;
using Mechanica.Core;
using Mechanica.Core.Creations;
using Mechanica.Core.Data;
using Mechanica.Core.Survival;
using Mechanica.Game.Creations;

namespace Mechanica.Game.Survival;

/// <summary>
/// Shows a crop growing on a soil part (<see cref="SoilView"/>). A seed with a <c>plant</c> model shows it: its
/// <c>grow</c> clip is scrubbed to the stage (second n is stage n) and eases there over a couple of seconds, its
/// <c>wilt</c> clip droops it on dry soil, and its <c>sway</c> clip loops out of step with its neighbours. Without one it
/// is a green shoot that grows taller with each stage, with the produce on top in its own colour once ripe. It follows
/// the soil's Body.
/// </summary>
public partial class CropNode : Node3D
{
    private static readonly StandardMaterial3D Leaf = new() { AlbedoColor = new Color(0.3f, 0.65f, 0.25f) };

    // Seconds the plant takes to grow from one stage to the next, and to wilt or pick up again.
    private const float GrowSeconds = 2f;
    private const float WiltSeconds = 1.5f;

    private MeshInstance3D? _shoot;
    private MeshInstance3D? _fruit;
    private Node3D? _plant;
    private string? _plantSeed;
    private (AnimationPlayer Player, Animation Clip)? _grow;
    private (AnimationPlayer Player, Animation Clip)? _wilt;
    private float _grown = float.NaN;
    private float _grownTarget;
    private float _wilted = float.NaN;
    private float _wiltTarget;

    /// <summary>What the soil shows.</summary>
    public SoilView? View { get; private set; }

    /// <summary>The seed's plant model, or null while it shows the stand-in shoot.</summary>
    public Node3D? Plant => _plant;

    /// <summary>Where the plant model's <c>grow</c> clip is shown, in stages (0 to 4); NaN with no plant model.</summary>
    public float GrowShown => _grown;

    /// <summary>How wilted the plant model is shown, 0 fresh to 1 (dry soil); NaN with no plant model.</summary>
    public float WiltShown => _wilted;

    /// <summary>
    /// Shows <paramref name="view"/>: the seed's plant model from <paramref name="assets"/> when it has one, otherwise the
    /// stand-in, colouring ripe produce from <paramref name="catalog"/>.
    /// </summary>
    public void Show(SoilView view, IPartCatalog catalog, PartAssets? assets = null)
    {
        View = view;
        if (view.Seed != _plantSeed)
            MakePlant(view.Seed, catalog, assets);
        if (_plant is not null)
        {
            _grownTarget = view.Stage;
            _wiltTarget = view.Wetness == SoilWetness.Dry ? 1f : 0f;
            // The first look at a crop shows it as it is; later changes ease there.
            if (float.IsNaN(_grown))
                (_grown, _wilted) = (_grownTarget, _wiltTarget);
            Scrub(_grow, _grown);
            Scrub(_wilt, _wilted);
            return;
        }
        ShowStandIn(view, catalog);
    }

    public override void _Process(double delta)
    {
        if (_plant is null)
            return;
        var step = (float)delta;
        if (_grown != _grownTarget)
            Scrub(_grow, _grown = MoveToward(_grown, _grownTarget, step / GrowSeconds * MathF.Max(1f, MathF.Abs(_grownTarget - _grown))));
        if (_wilted != _wiltTarget)
            Scrub(_wilt, _wilted = MoveToward(_wilted, _wiltTarget, step / WiltSeconds));
    }

    /// <summary>
    /// Takes the crop away: a plant model sinks back into the soil in a puff of dirt (its fruit popping up first when it
    /// was <paramref name="harvested"/> ripe), then the node frees itself; the stand-in just goes.
    /// </summary>
    public void Remove(bool harvested)
    {
        if (_plant is null || !IsInsideTree() || !Visible)
        {
            QueueFree();
            return;
        }
        SetProcess(false);
        AddChild(DirtPuff());
        var tween = CreateTween();
        if (harvested)
        {
            // Fruit{truss}_{n}; its ripeness looks below it are Fruit{truss}_{n}_{look}.
            var fruits = _plant.FindChildren("Fruit*", nameof(Node3D), owned: false).OfType<Node3D>()
                .Where(f => f.Name.ToString().Count(c => c == '_') == 1);
            foreach (var fruit in fruits)
                tween.Parallel().TweenProperty(fruit, "scale", fruit.Scale * 1.35f, 0.12f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        }
        tween.TweenProperty(_plant, "scale", new Vector3(_plant.Scale.X * 0.4f, 0.001f, _plant.Scale.Z * 0.4f), 0.45f)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        tween.TweenInterval(0.5f);
        tween.TweenCallback(Callable.From(QueueFree));
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

    // Builds the seed's plant model with a player each for grow, wilt and sway (they move different properties of its
    // nodes, so all three hold at once), or the stand-in when it has none.
    private void MakePlant(string? seed, IPartCatalog catalog, PartAssets? assets)
    {
        _plantSeed = seed;
        _plant?.QueueFree();
        (_plant, _grow, _wilt, _grown, _wilted) = (null, null, null, float.NaN, float.NaN);
        var path = seed is null ? null : catalog.FindDefinition(seed)?.Seed?.Plant;
        if (path is null || assets?.SceneOf(seed!, path) is not { } scene)
            return;
        _shoot?.QueueFree();
        _fruit?.QueueFree();
        (_shoot, _fruit) = (null, null);
        _plant = scene;
        _plant.Name = "Plant";
        AddChild(_plant);
        if (_plant.FindChildren("*", nameof(AnimationPlayer), owned: false).OfType<AnimationPlayer>().FirstOrDefault() is not { } player)
            return;
        player.Stop();
        _grow = OwnPlayer(player, "grow");
        _wilt = OwnPlayer(player, "wilt");
        if (OwnPlayer(player, "sway") is var (sway, clip))
        {
            clip.LoopMode = Animation.LoopModeEnum.Linear;
            sway.Play("sway");
            // Out of step with the neighbours: each plant starts somewhere else in the sway.
            sway.Seek(GD.Randf() * clip.Length, update: true);
        }
    }

    private static (AnimationPlayer Player, Animation Clip)? OwnPlayer(AnimationPlayer model, string name)
    {
        if (!model.HasAnimation(name))
            return null;
        var clip = model.GetAnimation(name);
        var library = new AnimationLibrary();
        library.AddAnimation(name, clip);
        var own = new AnimationPlayer { Name = name + "_player", RootNode = model.RootNode };
        own.AddAnimationLibrary("", library);
        model.GetParent().AddChild(own);
        return (own, clip);
    }

    private static void Scrub((AnimationPlayer Player, Animation Clip)? scrubbed, float seconds)
    {
        if (scrubbed is not var (player, clip))
            return;
        var name = player.GetAnimationList()[0];
        if (player.CurrentAnimation != name)
            player.Play(name);
        player.Pause();
        player.Seek(Math.Clamp(seconds, 0f, (float)clip.Length), update: true);
    }

    private static float MoveToward(float from, float to, float by) => from < to ? MathF.Min(to, from + by) : MathF.Max(to, from - by);

    private void ShowStandIn(SoilView view, IPartCatalog catalog)
    {
        if (_shoot is null)
        {
            _shoot = new MeshInstance3D { Name = "Shoot", MaterialOverride = Leaf };
            AddChild(_shoot);
            _fruit = new MeshInstance3D { Name = "Fruit", Mesh = new SphereMesh { Radius = 0.06f, Height = 0.12f } };
            AddChild(_fruit);
        }
        var height = 0.04f + 0.06f * view.Stage;
        _shoot.Mesh = new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.025f, Height = height };
        _shoot.Position = new Vector3(0, height / 2, 0);
        _fruit!.Visible = view.Ripe;
        _fruit.Position = new Vector3(0, height + 0.03f, 0);
        var color = view.Crop is { } crop ? catalog.FindDefinition(crop)?.Visual?.Color : null;
        _fruit.MaterialOverride = new StandardMaterial3D { AlbedoColor = color is null ? new Color(0.8f, 0.25f, 0.3f) : Color.FromHtml(color) };
    }

    // A one-shot puff of soil where the plant was.
    private static CpuParticles3D DirtPuff() => new()
    {
        Name = "DirtPuff",
        OneShot = true,
        Emitting = true,
        Amount = 24,
        Lifetime = 0.7f,
        Explosiveness = 0.9f,
        Direction = Vector3.Up,
        Spread = 70f,
        InitialVelocityMin = 0.6f,
        InitialVelocityMax = 1.4f,
        Gravity = new Vector3(0, -4f, 0),
        ScaleAmountMin = 0.6f,
        ScaleAmountMax = 1.2f,
        Mesh = new SphereMesh { Radius = 0.015f, Height = 0.03f, RadialSegments = 6, Rings = 3 },
        MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.25f, 0.16f), Roughness = 1f },
    };

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
