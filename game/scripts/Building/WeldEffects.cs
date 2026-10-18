using Godot;
using Mechanica.Core.Building;
using Mechanica.Core.Characters;
using Mechanica.Core.Data;
using Mechanica.Core.Sound;
using Mechanica.Game.Net;
using Mechanica.Game.Player;
using Mechanica.Game.Sound;

namespace Mechanica.Game.Building;

/// <summary>
/// The Weld Tool in every player's hand, as every peer sees it, and the sparks of a weld. While the <c>weld</c> clip
/// plays (<see cref="CharacterGesture.Weld"/>) the prop's clamping arms (<c>weld_tool_arm_l</c>, <c>weld_tool_arm_r</c>)
/// close round its tip, the tip (<c>weld_tool_tip</c>) spins and the blue ring (<c>weld_tool_glow</c>) and a light at
/// the tip brighten (<see cref="WeldMotion"/>); otherwise the arms hang open as on the reference sheet. It reads only
/// the prop and gesture each character's animator already plays, so it costs no traffic. When the host welds it plays
/// <c>weld_complete</c> at the joint for everyone in earshot (<see cref="ToolActions"/>), and a burst of blue sparks
/// rides on that sound (<see cref="SoundSync.Played"/>).
/// </summary>
public partial class WeldEffects : Node3D
{
    /// <summary>The sound the host plays where a weld joins.</summary>
    public const string CompleteSound = Identifiers.VanillaModId + ":weld_complete";

    /// <summary>The prop's moving and glowing nodes.</summary>
    public const string ArmLeft = "weld_tool_arm_l", ArmRight = "weld_tool_arm_r", TipNode = "weld_tool_tip", GlowNode = "weld_tool_glow";

    private const float SparkLife = 0.45f;

    // From tools/characters/build_characters.py (weld_arm_close, weld_tip_axis), in the prop's own space (glTF y-up):
    // the axis each arm turns about to close, the angle, and the axis the tip spins about.
    private static readonly Vector3 LeftAxis = new Vector3(-0.176f, 0.8993f, 0.4003f).Normalized();
    private static readonly Vector3 RightAxis = new Vector3(0.176f, 0.8993f, -0.4003f).Normalized();
    private const float CloseDegrees = 54.6f;
    private static readonly Vector3 TipAxis = new Vector3(0.9781f, 0f, -0.2079f).Normalized();
    private static readonly Vector3 TipPoint = new(0.1487f, 0f, -0.0991f);
    private static readonly Color Blue = new("#3aa8ff");

    private readonly Dictionary<CharacterModel, Rig> _rigs = [];

    /// <summary>Players. Set before adding to the tree.</summary>
    public PlayerSync Players { get; init; } = null!;

    /// <summary>The world's sounds. Set before adding to the tree.</summary>
    public SoundSync Sounds { get; init; } = null!;

    /// <summary>Spark bursts made so far (tests count them).</summary>
    public int Bursts { get; private set; }

    public override void _Ready() => Sounds.Played += OnPlayed;

    public override void _ExitTree() => Sounds.Played -= OnPlayed;

    /// <summary>How far a player's Weld Tool arms are closed in the model shown for them now (0 to 1), or null when they do not hold it.</summary>
    public float? ClosedOf(int peer) =>
        Players.Players.TryGetValue(peer, out var player) && Shown(player) is { } model && _rigs.TryGetValue(model, out var rig) ? rig.Closed : null;

    /// <summary>How far a player's Weld Tool tip has turned, in turns, or null when they do not hold it.</summary>
    public float? TipTurnsOf(int peer) =>
        Players.Players.TryGetValue(peer, out var player) && Shown(player) is { } model && _rigs.TryGetValue(model, out var rig) ? rig.Turns : null;

    public override void _Process(double delta)
    {
        var seen = new HashSet<CharacterModel>();
        foreach (var player in Players.Players.Values)
        {
            foreach (var model in new[] { player.Character as CharacterModel, player.Arms as CharacterModel })
            {
                if (model is null || !IsInstanceValid(model) || model.Animator.Prop != CharacterProp.WeldTool || model.PropNode is not { } prop)
                    continue;
                if (!_rigs.TryGetValue(model, out var rig) || rig.Prop != prop || !IsInstanceValid(rig.Light))
                    _rigs[model] = rig = Fit(prop);
                seen.Add(model);
                rig.Closed = WeldMotion.Closed(WeldTime(model.Animator) ?? -1f);
                rig.Turns = (rig.Turns + (float)delta * WeldMotion.TipTurnsPerSecond * rig.Closed) % 1f;
                Pose(rig);
            }
        }
        foreach (var model in _rigs.Keys.Where(m => !seen.Contains(m)).ToList())
            _rigs.Remove(model);
    }

    /// <summary>Throws a burst of blue sparks at <paramref name="at"/>, where a weld joined.</summary>
    public void Burst(Vector3 at)
    {
        Bursts++;
        var root = new Node3D { Name = "WeldSparks", Position = at };
        AddChild(root);
        root.AddChild(SledgeImpacts.Emitter(30, SparkLife, new Vector2(2f, 4.5f), -7f, 85f, SledgeImpacts.Quad(0.011f), Blue.Lightened(0.35f), glow: true));
        root.AddChild(SledgeImpacts.Emitter(6, 0.2f, new Vector2(0.3f, 0.8f), 0f, 180f, SledgeImpacts.Quad(0.025f), Blue with { A = 0.5f }, grow: true, glow: true));
        var flash = new OmniLight3D { Name = "Flash", LightColor = Blue, LightEnergy = 0.8f, OmniRange = 0.7f, ShadowEnabled = false };
        root.AddChild(flash);
        var tween = root.CreateTween();
        tween.TweenProperty(flash, "light_energy", 0f, 0.2f);
        GetTree().CreateTimer(SparkLife + 0.3f).Timeout += root.QueueFree;
    }

    private void OnPlayed(SoundEvent sound, Vector3 at)
    {
        if (sound.QualifiedId == CompleteSound)
            Burst(at);
    }

    // Seconds into the weld clip the character is, or null when it is not welding.
    private static float? WeldTime(CharacterAnimator animator)
    {
        foreach (var clip in animator.Upper)
        {
            var name = clip.Clip.EndsWith(CharacterRig.FirstPersonSuffix, StringComparison.Ordinal) ? clip.Clip[..^CharacterRig.FirstPersonSuffix.Length] : clip.Clip;
            if (name == "weld" && clip.Weight > 0.01f)
                return clip.Time;
        }
        return null;
    }

    private static void Pose(Rig rig)
    {
        var angle = Mathf.DegToRad(CloseDegrees) * rig.Closed;
        if (rig.Left is { } left)
            left.Transform = new Transform3D(rig.LeftRest.Basis * new Basis(LeftAxis, angle), rig.LeftRest.Origin);
        if (rig.Right is { } right)
            right.Transform = new Transform3D(rig.RightRest.Basis * new Basis(RightAxis, angle), rig.RightRest.Origin);
        if (rig.Tip is { } tip)
            tip.Transform = new Transform3D(rig.TipRest.Basis * new Basis(TipAxis, rig.Turns * Mathf.Tau), rig.TipRest.Origin);
        var glow = WeldMotion.Glow(rig.Closed);
        rig.Material.EmissionEnergyMultiplier = glow;
        rig.Light.LightEnergy = Mathf.Max(0f, glow - WeldMotion.IdleGlow) * 0.4f;
        rig.Light.Visible = rig.Light.LightEnergy > 0.01f;
    }

    // The model whose hand shows: the first-person arms while the player looks out of its own eyes, else the body.
    private static CharacterModel? Shown(PlayerController player) =>
        player.Arms is CharacterModel { Visible: true } arms && arms.PropNode is not null ? arms : player.Character as CharacterModel;

    // Finds a prop's moving nodes, gives its glow a material of its own (so one player's weld is not everyone's) and
    // puts a light at the tip.
    private static Rig Fit(Node3D prop)
    {
        Node3D? Find(string name) => prop.FindChild(name, recursive: true, owned: false) as Node3D;
        var material = new StandardMaterial3D
        {
            AlbedoColor = Blue,
            EmissionEnabled = true,
            Emission = Blue,
            EmissionEnergyMultiplier = WeldMotion.IdleGlow,
            Roughness = 0.3f,
        };
        if (Find(GlowNode) is MeshInstance3D glow)
        {
            for (var i = 0; i < glow.GetSurfaceOverrideMaterialCount(); i++)
                glow.SetSurfaceOverrideMaterial(i, material);
        }
        var light = new OmniLight3D { Name = "TipLight", Position = TipPoint, LightColor = Blue, OmniRange = 0.4f, LightEnergy = 0f, Visible = false, ShadowEnabled = false };
        prop.AddChild(light);
        var (left, right, tip) = (Find(ArmLeft), Find(ArmRight), Find(TipNode));
        return new Rig(prop, left, right, tip, material, light)
        {
            LeftRest = left?.Transform ?? Transform3D.Identity,
            RightRest = right?.Transform ?? Transform3D.Identity,
            TipRest = tip?.Transform ?? Transform3D.Identity,
        };
    }

    private sealed class Rig(Node3D prop, Node3D? left, Node3D? right, Node3D? tip, StandardMaterial3D material, OmniLight3D light)
    {
        public Node3D Prop { get; } = prop;

        public Node3D? Left { get; } = left;

        public Node3D? Right { get; } = right;

        public Node3D? Tip { get; } = tip;

        public StandardMaterial3D Material { get; } = material;

        public OmniLight3D Light { get; } = light;

        public Transform3D LeftRest { get; init; }

        public Transform3D RightRest { get; init; }

        public Transform3D TipRest { get; init; }

        public float Closed { get; set; }

        public float Turns { get; set; }
    }
}
