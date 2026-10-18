using Godot;
using Mechanica.Core.Characters;
using Mechanica.Game.Net;
using Mechanica.Game.Player;

namespace Mechanica.Game.Building;

/// <summary>
/// The Paint Tool in every player's hand, as every peer sees it (Chase's paint tool spec): its nozzles, tubes and the
/// paint in its tank (the prop's <c>paint_tool_tint</c> and <c>paint_tool_paint</c> nodes) take the colour the player
/// has chosen, the tank twists off and back on through the reload (<see cref="PaintToolRig"/>), and while the tool is
/// raised it sprays a mist from the nozzles: the colour when painting, a pale, sparse puff when taking paint off. It reads
/// only the prop, guard and tint bytes the player already sends, so it costs no traffic.
/// </summary>
public partial class PaintToolLook : Node
{
    /// <summary>The nozzles and tubes in the Paint Tool prop.</summary>
    public const string TintNode = "paint_tool_tint";

    /// <summary>The paint inside the tank.</summary>
    public const string PaintNode = "paint_tool_paint";

    /// <summary>The tank, which the reload twists off.</summary>
    public const string TankNode = "paint_tool_tank";

    /// <summary>The mist's colour while taking paint off.</summary>
    public static readonly Color EraseMist = new(0.92f, 0.95f, 0.96f, 0.35f);

    // The nozzle ring's front and the nozzles' axis in the prop's own space: paint_nozzle_tip() and paint_axis() in
    // tools/characters/build_characters.py, glTF y-up.
    private static readonly Vector3 Tip = new(0.1578f, 0f, -0.15099f);
    private static readonly Vector3 Axis = new Vector3(0.96593f, 0f, -0.25882f).Normalized();

    private readonly Dictionary<CharacterModel, Look> _looks = [];

    /// <summary>Players. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>The palette the tint byte indexes. Set before adding to the tree.</summary>
    public IReadOnlyList<string> Palette { get; init; } = [];

    /// <summary>The reload's tank keys. Set before adding to the tree.</summary>
    public PaintToolSettings Settings { get; init; } = new();

    /// <summary>
    /// What a player's Paint Tool shows in the model drawn for them now: its colour, whether it is spraying (and taking
    /// paint off) and the tank's twist; null when they do not hold it.
    /// </summary>
    public (Color Tint, bool Spraying, bool Erasing, float Twist)? LookOf(int peer) =>
        Players.Players.TryGetValue(peer, out var player) && Shown(player) is { } model && _looks.TryGetValue(model, out var look)
            ? (look.Tint.AlbedoColor, look.Mist.Emitting, look.Erasing, look.Twist) : null;

    /// <summary>The colour of tint byte <paramref name="tint"/> (a palette index, the erase bit ignored).</summary>
    public Color TintColor(byte tint)
    {
        var index = tint & ~CharacterRig.TintErase;
        return index < Palette.Count ? new Color(Palette[index]) : new Color(Palette.Count > 0 ? Palette[0] : "#ffffff");
    }

    public override void _Process(double delta)
    {
        var seen = new HashSet<CharacterModel>();
        foreach (var player in Players.Players.Values)
        {
            foreach (var model in new[] { player.Character as CharacterModel, player.Arms as CharacterModel })
            {
                if (model is null || !IsInstanceValid(model) || model.Animator.Prop != CharacterProp.PaintTool || model.PropNode is not { } prop)
                    continue;
                if (!_looks.TryGetValue(model, out var look) || look.Prop != prop || !IsInstanceValid(look.Mist))
                    _looks[model] = look = Dress(prop);
                seen.Add(model);
                var tint = player.ShownTint;
                var colour = TintColor(tint);
                look.Tint.AlbedoColor = colour;
                look.Paint.AlbedoColor = colour;
                look.Paint.Emission = colour;
                look.Erasing = (tint & CharacterRig.TintErase) != 0;
                var spraying = player.Pose.Guard && model.Visible;
                Spray(look, spraying, colour);
                (look.Twist, var pull) = Tank(model.Animator) ?? (0f, 0f);
                if (look.Tank is not null)
                    look.Tank.Transform = new Transform3D(new Basis(Axis, Mathf.DegToRad(look.Twist)), look.TankRest - Axis * pull);
            }
        }
        foreach (var model in _looks.Keys.Where(m => !seen.Contains(m)).ToList())
            _looks.Remove(model);
    }

    /// <summary>The tank's twist and pull for the clips <paramref name="animator"/> plays now, or null when none moves it.</summary>
    public (float Twist, float Pull)? Tank(CharacterAnimator animator)
    {
        (float Twist, float Pull)? best = null;
        var weight = 0f;
        foreach (var clip in animator.Upper)
        {
            if (clip.Weight > weight && PaintToolRig.Tank(Settings, clip.Clip, clip.Time) is { } motion)
            {
                best = (motion.Twist * clip.Weight, motion.Pull * clip.Weight);
                weight = clip.Weight;
            }
        }
        return best;
    }

    // The model whose hand shows: the first-person arms while the player looks out of its own eyes, else the body.
    private static CharacterModel? Shown(PlayerController player) =>
        player.Arms is CharacterModel { Visible: true } arms && arms.PropNode is not null ? arms : player.Character as CharacterModel;

    // Paint is a fine fast cone of droplets; taking paint off is a slower, wider puff of pale mist. The shape changes only
    // when the mode does: setting a particle system's properties restarts what it has in the air.
    private static void Spray(Look look, bool on, Color colour)
    {
        var mist = look.Mist;
        var tint = look.Erasing ? EraseMist : colour with { A = 0.9f };
        if (look.MistErasing != look.Erasing)
        {
            look.MistErasing = look.Erasing;
            mist.InitialVelocityMin = look.Erasing ? 0.6f : 2.2f;
            mist.InitialVelocityMax = look.Erasing ? 1.2f : 3.4f;
            mist.Spread = look.Erasing ? 28f : 9f;
            mist.ScaleAmountMin = look.Erasing ? 1.8f : 0.7f;
            mist.ScaleAmountMax = look.Erasing ? 2.8f : 1.2f;
        }
        if (mist.Color != tint)
            mist.Color = tint;
        if (mist.Emitting != on)
            mist.Emitting = on;
    }

    // Gives a prop's tint and paint materials of their own (so one player's colour is not everyone's) and a mist at the nozzles.
    private static Look Dress(Node3D prop)
    {
        var tint = new StandardMaterial3D { Roughness = 0.35f, AlbedoColor = new Color("#3874dc") };
        var paint = new StandardMaterial3D { Roughness = 0.2f, EmissionEnabled = true, EmissionEnergyMultiplier = 0.15f };
        Override(prop.FindChild(TintNode, recursive: true, owned: false) as MeshInstance3D, tint);
        Override(prop.FindChild(PaintNode, recursive: true, owned: false) as MeshInstance3D, paint);
        var tank = prop.FindChild(TankNode, recursive: true, owned: false) as Node3D;
        var mist = new CpuParticles3D
        {
            Name = "PaintMist",
            Position = Tip,
            Emitting = false,
            Amount = 48,
            Lifetime = 0.45,
            LocalCoords = false,
            Direction = Axis,
            Gravity = new Vector3(0f, -1.5f, 0f),
            DampingMin = 2f,
            DampingMax = 3f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.02f,
            // Round droplets: a few-sided ball reads as a spray at any angle and needs no texture.
            Mesh = new SphereMesh
            {
                Radius = 0.012f,
                Height = 0.024f,
                RadialSegments = 6,
                Rings = 3,
                Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    VertexColorUseAsAlbedo = true,
                },
            },
        };
        prop.AddChild(mist);
        return new Look(prop, tint, paint, tank, tank?.Position ?? Vector3.Zero, mist);
    }

    private static void Override(MeshInstance3D? mesh, Material material)
    {
        if (mesh is null)
            return;
        for (var i = 0; i < mesh.GetSurfaceOverrideMaterialCount(); i++)
            mesh.SetSurfaceOverrideMaterial(i, material);
    }

    private sealed class Look(Node3D prop, StandardMaterial3D tint, StandardMaterial3D paint, Node3D? tank, Vector3 tankRest, CpuParticles3D mist)
    {
        public Node3D Prop { get; } = prop;

        public StandardMaterial3D Tint { get; } = tint;

        public StandardMaterial3D Paint { get; } = paint;

        public Node3D? Tank { get; } = tank;

        public Vector3 TankRest { get; } = tankRest;

        public CpuParticles3D Mist { get; } = mist;

        public bool Erasing { get; set; }

        public bool? MistErasing { get; set; }

        public float Twist { get; set; }
    }
}
